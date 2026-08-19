using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CScore;

/// <summary>Режим продольной силы при скане диаграммы кривизна-момент.</summary>
public enum CurvatureNMode { Constant, Proportional }

/// <summary>Способ расстановки вспомогательных точек участка диаграммы.</summary>
public enum CurveStepMode
{
    /// <summary>Равномерно по параметру кривизны/масштаба (по умолчанию).</summary>
    ByCurvature,
    /// <summary>Равномерно по целевому вектору момента (прямые решения, без пина).</summary>
    ByMoment
}

/// <summary>Одна точка составной диаграммы кривизна-момент.</summary>
public sealed class BiaxialCurveScanPoint
{
    public double N { get; set; }
    public double Mx { get; set; }
    public double My { get; set; }
    public double E0 { get; set; }
    public double Ky { get; set; }
    public double Kz { get; set; }
    /// <summary>
    /// Значение параметра скана в ЭТОЙ точке: величина кривизны |κ| вдоль луча (режим
    /// Constant) или коэффициент нагрузки λ (режим Proportional). Используется как единая
    /// точка отсчёта для продолжения скана после контрольных точек — критично для режима
    /// Proportional, где λ_crc, как правило, НЕ равен 1.0 (см. блокер 2 ревью плана
    /// 2026-08-17-biaxial-moment-curvature-plan-review.md).
    /// </summary>
    public double T { get; set; }
    /// <summary>1 — до трещины, 2 — пересчётная точка, 3 — до текучести, 4 — до предела.</summary>
    public int Segment { get; set; }
    public bool Converged { get; set; }
    public bool PsiActive { get; set; }
    /// <summary>true, если Mx/My обрезаны по эталонному пределу без ψs (см. решение 5 спеки).</summary>
    public bool Clipped { get; set; }
}

/// <summary>Результат расчёта составной диаграммы кривизна-момент для двухплоскостного изгиба.</summary>
public sealed class BiaxialCurvatureCurveResult
{
    public bool HasMx { get; set; }
    public bool HasMy { get; set; }
    public CurvatureNMode NMode { get; set; }
    public bool UsePsi { get; set; }
    public List<BiaxialCurveScanPoint> Points { get; } = [];

    public BiaxialCurveScanPoint? Cracking { get; set; }
    public BiaxialCurveScanPoint? CrackTransitionPoint { get; set; }
    public BiaxialCurveScanPoint? Yield { get; set; }
    public BiaxialCurveScanPoint? Ultimate { get; set; }
    public BiaxialCurveScanPoint? UltimateReference { get; set; }

    public double Ea0 { get; set; }
    public double B0x { get; set; }
    public double B0y { get; set; }

    /// <summary>"ok" | "partial" | "error" — см. спеку, раздел "Технические примечания".</summary>
    public string Status { get; set; } = "error";
}

/// <summary>
/// Строит составную диаграмму кривизна-момент (участки: до трещины / пересчётная точка /
/// до текучести / до предела несущей способности) по нелинейной деформационной модели
/// СП 63.13330 п. 8.2.23-8.2.32, для произвольного (в т.ч. двухплоскостного) направления
/// изгиба и в двух режимах продольной силы. Обобщение StrainTest/Example47Curvature.cs
/// (диагностический прототип для одноосного случая при N=0). Единицы: кН, кН·м, м, 1/м.
/// </summary>
public sealed class BiaxialCurvatureCurveSolver
{
    readonly CrossSection _section;
    readonly CalcType _calcCrc;
    readonly CalcType _calcService;
    readonly double _solverTol;
    readonly int _solverMaxIter;
    readonly double _solverH;
    readonly bool _centralJacobian;
    readonly int _auxPointsPerSegment;
    readonly CurveStepMode _stepMode;
    readonly CancellationToken _cancellationToken;

    const double DirectionEps = 1e-9;

    public BiaxialCurvatureCurveSolver(
        CrossSection section,
        CalcType calcCrc = CalcType.N,
        CalcType calcService = CalcType.N,
        double solverTol = 0.5,
        int solverMaxIter = 80,
        double solverH = 1e-7,
        bool centralJacobian = true,
        int auxPointsPerSegment = 10,
        CurveStepMode stepMode = CurveStepMode.ByCurvature,
        CancellationToken cancellationToken = default)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _calcCrc = calcCrc;
        _calcService = calcService;
        _solverTol = solverTol;
        _solverMaxIter = solverMaxIter;
        _solverH = solverH;
        _centralJacobian = centralJacobian;
        _auxPointsPerSegment = auxPointsPerSegment < 0 ? 0 : auxPointsPerSegment;
        _stepMode = stepMode;
        _cancellationToken = cancellationToken;
    }

    public BiaxialCurvatureCurveResult Compute(
        double N0, double Mx0, double My0, CurvatureNMode nMode, bool usePsi)
    {
        throw new NotImplementedException("Пайплайн переписывается в Task 6-9.");
    }

    bool IsAtUltimateStrain(BiaxialCurveScanPoint point)
    {
        var k = new Kurvature { e0 = point.E0, ky = point.Ky, kz = point.Kz };
        if (ExtremeContourConcreteStrain(k) <= ConcreteUltimateStrain()) return true;
        return MaxTensileRebarStrain(point) >= RebarUltimateStrain();
    }

    // Совпадает с CrackingSolver.IsConcreteArea — учитывает и MatType.Custom с
    // BaseType==Concrete (пользовательская диаграмма бетона), см. замечание 10 ревью плана.
    static bool IsConcreteArea(MaterialArea area) =>
        area.Material?.Type == MatType.Concrete ||
        (area.Material?.Type == MatType.Custom && area.Material.BaseType == MatType.Concrete);

    double ExtremeContourConcreteStrain(Kurvature k)
    {
        double min = double.PositiveInfinity;
        foreach (var area in _section.Areas)
        {
            if (!IsConcreteArea(area)) continue;
            if (area.Hull == null) continue;
            var xs = area.Hull.X;
            var ys = area.Hull.Y;
            for (int i = 0; i < xs.Count; i++)
            {
                double eps = k.e0 + k.ky * ys[i] + k.kz * xs[i];
                if (eps < min) min = eps;
            }
        }
        return min;
    }

    // Наиболее критичный (самый отрицательный) Ec2 среди всех бетонных областей — при
    // нескольких разных бетонах в сечении предел должен определяться самым слабым из них,
    // не первым попавшимся (см. замечание 9 ревью плана).
    double ConcreteUltimateStrain()
    {
        double min = double.PositiveInfinity;
        foreach (var area in _section.Areas)
        {
            if (!IsConcreteArea(area)) continue;
            var chars = area.Material?.GetChars(_calcService);
            if (chars == null) continue;
            if (chars.Ec2 < min) min = chars.Ec2;
        }
        return double.IsPositiveInfinity(min) ? double.NegativeInfinity : min;
    }

    double RebarUltimateStrain()
    {
        double min = double.PositiveInfinity;
        foreach (var area in _section.Areas)
        {
            if (area.Material?.Type is not (MatType.ReSteelF or MatType.ReSteelU)) continue;
            var chars = area.Material?.GetChars(_calcService);
            if (chars == null) continue;
            if (chars.Et2 < min) min = chars.Et2;
        }
        return min;
    }

    // ИЗВЕСТНОЕ ОГРАНИЧЕНИЕ (см. замечание 8 ревью плана): корректно только для арматуры с
    // физическим пределом текучести (ReSteelF, площадка на диаграмме). Для ReSteelU (условный
    // предел σ0.2) точка текучести физически лежит при деформации заметно БОЛЬШЕ Ry/E
    // (упругая часть + остаточная 0.2%) — участок 3 для такой арматуры оборвётся раньше
    // физического наступления текучести. Уточнение критерия для ReSteelU — вне объёма этой
    // итерации (follow-up).
    double MinRebarYieldStrain()
    {
        double min = double.PositiveInfinity;
        foreach (var area in _section.Areas)
        {
            if (area.Material?.Type is not (MatType.ReSteelF or MatType.ReSteelU)) continue;
            var chars = area.Material?.GetChars(_calcService);
            if (chars == null || chars.E == 0.0) continue;
            double eps = chars.Ry / chars.E;
            if (eps < min) min = eps;
        }
        return min;
    }

    double MaxTensileRebarStrain(BiaxialCurveScanPoint point)
    {
        var k = new Kurvature { e0 = point.E0, ky = point.Ky, kz = point.Kz };
        double max = 0.0;
        foreach (var (area, ka) in _section.EnumerateAreas(k))
        {
            if (area.Material?.Type is not (MatType.ReSteelF or MatType.ReSteelU)) continue;
            foreach (var fiber in area.Fibers)
            {
                if (fiber.TypeFiber != FiberType.point) continue;
                double eps = ka.e0 + ka.ky * fiber.Y + ka.kz * fiber.X + fiber.Eps_p;
                if (eps > max) max = eps;
            }
        }
        return max;
    }

    // Принимает ПОСТтрещинную плоскость (transition), не дотрещинную (crackPoint) —
    // см. комментарий у вызова в Compute.
    Dictionary<Fiber, double> EpsCrcByFiber(BiaxialCurveScanPoint postCrackPoint)
    {
        var map = new Dictionary<Fiber, double>(ReferenceEqualityComparer.Instance);
        var k = new Kurvature { e0 = postCrackPoint.E0, ky = postCrackPoint.Ky, kz = postCrackPoint.Kz };
        foreach (var (area, ka) in _section.EnumerateAreas(k))
        {
            if (area.Material?.Type is not (MatType.ReSteelF or MatType.ReSteelU)) continue;
            foreach (var fiber in area.Fibers)
            {
                if (fiber.TypeFiber != FiberType.point) continue;
                map[fiber] = ka.e0 + ka.ky * fiber.Y + ka.kz * fiber.X + fiber.Eps_p;
            }
        }
        return map;
    }

    (double ea0, double b0x, double b0y) ComputeElasticStiffness()
    {
        var zero = new Kurvature { e0 = 0.0, ky = 0.0, kz = 0.0 };
        var l0 = _section.Integral(zero, _calcCrc, ten: true, ca: true);
        var lN = _section.Integral(new Kurvature { e0 = _solverH, ky = 0.0, kz = 0.0 }, _calcCrc, ten: true, ca: true);
        var lY = _section.Integral(new Kurvature { e0 = 0.0, ky = _solverH, kz = 0.0 }, _calcCrc, ten: true, ca: true);
        var lX = _section.Integral(new Kurvature { e0 = 0.0, ky = 0.0, kz = _solverH }, _calcCrc, ten: true, ca: true);

        double ea0 = (lN.N - l0.N) / _solverH;
        double b0x = (lY.Mx - l0.Mx) / _solverH;
        double b0y = (lX.My - l0.My) / _solverH;
        return (ea0, b0x, b0y);
    }

}
