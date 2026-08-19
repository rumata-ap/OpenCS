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
        var result = new BiaxialCurvatureCurveResult
        {
            HasMx = Math.Abs(Mx0) > DirectionEps,
            HasMy = Math.Abs(My0) > DirectionEps,
            NMode = nMode,
            UsePsi = usePsi
        };

        if (!result.HasMx && !result.HasMy)
        {
            result.Status = "error";
            return result;
        }

        (result.Ea0, result.B0x, result.B0y) = ComputeElasticStiffness();

        // Предпроверка "предел раньше трещины" (ten=true LimitForceSolverFast) — точный
        // event-solve, см. спеку "Обнаружение нет трещины / предел раньше трещины".
        var tensionCracker = new CrackingSolver(_section, _calcCrc, solverTol: _solverTol, solverMaxIter: _solverMaxIter, solverH: _solverH);
        double tensionLimit;
        try { tensionLimit = tensionCracker.TensionLimit(); }
        catch (InvalidOperationException)
        {
            result.Status = "error";
            return result;
        }

        LimitForceResult? ultTen = null;
        try
        {
            var tenSolver = new LimitForceSolverFast(_section, _calcCrc, ten: true);
            ultTen = nMode == CurvatureNMode.Constant
                ? tenSolver.MomentFactor(N0, Mx0, My0)
                : tenSolver.AllFactor(N0, Mx0, My0);
        }
        catch (InvalidOperationException) { ultTen = null; }

        // Точка "0" (t=0, Ky=Kz=0) — чисто осевое равновесие (Ky=Kz=0 фиксированы, не через
        // пин-Ньютон — пин с epsPin=0 при ненулевом целевом моменте в общем случае не имеет
        // решения, т.к. при нулевой деформации в экстремальной точке сечение либо всё
        // растянуто, либо всё сжато, что несовместимо с произвольным (Mx,My)). Решается
        // одномерно: e0 из N(e0)=N_target при Ky=Kz=0. N_target = N0 (Constant) или 0.0
        // (Proportional, т.к. в этом режиме t=0 соответствует λ=0).
        double zeroTargetN = nMode == CurvatureNMode.Constant ? N0 : 0.0;
        CurvatureEquilibriumResult zeroEq;
        try
        {
            zeroEq = CurvatureEquilibrium8232.Solve(
                e0 => _section.Integral(new Kurvature { e0 = e0, ky = 0, kz = 0 }, _calcCrc, ten: true, ca: true),
                targetN: zeroTargetN, tolerance: _solverTol, maxIterations: 100);
        }
        catch (InvalidOperationException)
        {
            result.Status = "error";
            return result;
        }
        if (!zeroEq.Converged)
        {
            result.Status = "error";
            return result;
        }
        var zeroPoint = new BiaxialCurveScanPoint
        {
            N = zeroEq.Load.N, Mx = zeroEq.Load.Mx, My = zeroEq.Load.My,
            E0 = zeroEq.E0, Ky = 0.0, Kz = 0.0, T = 0.0, Segment = 1, Converged = true
        };
        result.Points.Add(zeroPoint);

        if (ultTen is { Converged: true, StrainPlane: Kurvature ultTenPlane } &&
            tensionCracker.MaxTensionStrain(ultTenPlane) < tensionLimit)
        {
            // Сечение исчерпывает несущую способность раньше образования трещины — единственный
            // узел результата. Развёртка — СЖАТЫЙ пин (не растянутый), "аналогично петле":
            // TensionPinSolverFast здесь физически неприменим (тот же аргумент, что и для
            // точки 0 — целевой момент несовместим с epsPin вблизи нуля на растянутой стороне).
            var load = _section.Integral(ultTenPlane, _calcCrc, ten: true, ca: true);
            var single = new BiaxialCurveScanPoint
            {
                N = load.N, Mx = load.Mx, My = load.My,
                E0 = ultTenPlane.e0, Ky = ultTenPlane.ky, Kz = ultTenPlane.kz,
                T = nMode == CurvatureNMode.Constant
                    ? Math.Sqrt(ultTenPlane.ky * ultTenPlane.ky + ultTenPlane.kz * ultTenPlane.kz)
                    : ultTen!.Factor,
                Segment = 1, Converged = true
            };
            result.Cracking = null;
            result.CrackTransitionPoint = null;
            result.Yield = null;
            result.Ultimate = single;
            result.UltimateReference = single;

            double epsCompressAtZero = ExtremeContourConcreteStrain(new Kurvature { e0 = zeroPoint.E0, ky = 0, kz = 0 });
            double epsCompressAtUlt = ExtremeContourConcreteStrain(ultTenPlane);
            var compressionPinNoCrack = new CompressionPinSolverFast(_section, _calcCrc, ten: true,
                hDiff: _solverH, newtonMaxIter: _solverMaxIter);
            result.Points.AddRange(BuildCompressionSweep(
                compressionPinNoCrack, N0, Mx0, My0, nMode, zeroPoint, epsCompressAtZero, epsCompressAtUlt, single));
            result.Status = "ok";
            return result;
        }

        // Обычный путь: точка 1 через развёртку растянутого пина.
        var tensionPin = new TensionPinSolverFast(_section, _calcCrc, solverTol: _solverTol, newtonMaxIter: _solverMaxIter, hDiff: _solverH);
        double dNdkCrack = nMode == CurvatureNMode.Constant ? 0.0 : N0;
        var crack = tensionPin.Solve(tensionLimit, N0, Mx0, My0, dNdkCrack, seed: null);
        if (!crack.Converged)
        {
            result.Status = "error";
            return result;
        }

        double crackT = nMode == CurvatureNMode.Constant
            ? Math.Sqrt(crack.Plane.ky * crack.Plane.ky + crack.Plane.kz * crack.Plane.kz)
            : SolveLambdaForCrack(crack, N0, Mx0, My0);
        var crackPoint = new BiaxialCurveScanPoint
        {
            N = crack.Load.N, Mx = crack.Load.Mx, My = crack.Load.My,
            E0 = crack.Plane.e0, Ky = crack.Plane.ky, Kz = crack.Plane.kz,
            T = crackT, Segment = 1, Converged = true
        };
        result.Cracking = crackPoint;
        result.Points.AddRange(BuildTensionSweep(tensionPin, N0, Mx0, My0, nMode, 0.0, crackT, crackPoint));
        _cancellationToken.ThrowIfCancellationRequested();

        // Точка 2 — прямое равновесие на векторе усилий точки 1, ten=false, без ψs, без поиска.
        var point2Solver = new StrainSolver(_section, _calcService, ten: false, ca: true,
            tol: _solverTol, maxIter: _solverMaxIter, h: _solverH, centralJacobian: _centralJacobian);
        var plane2 = point2Solver.Solve(crackPoint.N, crackPoint.Mx, crackPoint.My,
            new Kurvature { e0 = crackPoint.E0, ky = crackPoint.Ky, kz = crackPoint.Kz });
        if (!point2Solver.Converged)
        {
            result.Status = "partial";
            return result;
        }
        var load2 = _section.Integral(plane2, _calcService, ten: false, ca: true);
        var transition = new BiaxialCurveScanPoint
        {
            N = load2.N, Mx = load2.Mx, My = load2.My,
            E0 = plane2.e0, Ky = plane2.ky, Kz = plane2.kz,
            T = 0.0, // T точки 2 — не в единицах уч.1/петли (офф-рэй), не используется как
                     // t-граница для уч.3/4 (там используется μ, не T)
            Segment = 2, Converged = true
        };
        result.CrackTransitionPoint = transition;

        // Петля: сжатый пин, ten=true, продолжение направления/N-режима уч."1", от eps сжатия
        // в точке1 до eps сжатия в точке2.
        double epsCompressAtCrack = ExtremeContourConcreteStrain(
            new Kurvature { e0 = crackPoint.E0, ky = crackPoint.Ky, kz = crackPoint.Kz });
        double epsCompressAtT2 = ExtremeContourConcreteStrain(plane2);
        var compressionPin = new CompressionPinSolverFast(_section, _calcCrc, ten: true,
            hDiff: _solverH, newtonMaxIter: _solverMaxIter);
        result.Points.AddRange(BuildCompressionSweep(
            compressionPin, N0, Mx0, My0, nMode, crackPoint, epsCompressAtCrack, epsCompressAtT2, transition));
        _cancellationToken.ThrowIfCancellationRequested();

        // ... уч. "3"/"4" / точка3/точка4 — Task 8-9 продолжают этот метод отсюда.
        throw new NotImplementedException("Продолжение конвейера — см. Task 8-9.");
    }

    /// <summary>
    /// Строит уч. "1" (0 → конечная точка `endpoint`) равномерной развёрткой параметра между
    /// начальной точкой (epsPin=0 у растянутого пина) и конечной. В ByMoment — прямые
    /// `StrainSolver.Solve` на целевых векторах момента, интерполированных от 0 до `endpoint`
    /// (уч. "1" физически начинается в нуле — для уч. "3"/"4", начинающихся НЕ с нуля, такая
    /// же формула была бы ошибочной, см. Task 8, там начало интерполируется явно).
    /// </summary>
    List<BiaxialCurveScanPoint> BuildTensionSweep(
        TensionPinSolverFast solver, double N0, double Mx0, double My0, CurvatureNMode nMode,
        double epsPinFrom, double tEndKnown, BiaxialCurveScanPoint endpoint)
    {
        var points = new List<BiaxialCurveScanPoint>(_auxPointsPerSegment);
        if (_stepMode == CurveStepMode.ByMoment)
        {
            // Один StrainSolver на весь участок (не по одному на итерацию).
            var solverS = new StrainSolver(_section, _calcCrc, ten: true, ca: true, tol: _solverTol, maxIter: _solverMaxIter, h: _solverH, centralJacobian: _centralJacobian);
            for (int i = 1; i <= _auxPointsPerSegment; i++)
            {
                double frac = (double)i / (_auxPointsPerSegment + 1);
                double mxT = frac * endpoint.Mx, myT = frac * endpoint.My;
                double nT = nMode == CurvatureNMode.Constant ? N0 : frac * N0;
                var plane = solverS.Solve(nT, mxT, myT);
                if (!solverS.Converged) continue;
                var load = _section.Integral(plane, _calcCrc, ten: true, ca: true);
                points.Add(new BiaxialCurveScanPoint
                {
                    N = load.N, Mx = load.Mx, My = load.My,
                    E0 = plane.e0, Ky = plane.ky, Kz = plane.kz,
                    T = frac * endpoint.T, Segment = 1, Converged = true
                });
                _cancellationToken.ThrowIfCancellationRequested();
            }
            points.Add(endpoint);
            return points;
        }

        // ByCurvature: развёртка epsPin, определяемая через T конечной точки.
        double epsPinTo = EndpointTensionEps(endpoint);
        double dNdk = nMode == CurvatureNMode.Constant ? 0.0 : N0;
        Kurvature? seed = null;
        for (int i = 1; i <= _auxPointsPerSegment; i++)
        {
            double frac = (double)i / (_auxPointsPerSegment + 1);
            double epsPin = epsPinFrom + frac * (epsPinTo - epsPinFrom);
            var point = solver.Solve(epsPin, N0, Mx0, My0, dNdk, seed);
            if (!point.Converged) continue;
            seed = point.Plane;
            points.Add(new BiaxialCurveScanPoint
            {
                N = point.Load.N, Mx = point.Load.Mx, My = point.Load.My,
                E0 = point.Plane.e0, Ky = point.Plane.ky, Kz = point.Plane.kz,
                T = frac * endpoint.T, Segment = 1, Converged = true
            });
            _cancellationToken.ThrowIfCancellationRequested();
        }
        points.Add(endpoint);
        return points;
    }

    static double EndpointTensionEps(BiaxialCurveScanPoint endpoint)
    {
        // Деформация в вершине endpoint's собственной плоскости, взятая как e0 — приближение
        // epsPin конечной точки без хранения дополнительного поля в BiaxialCurveScanPoint;
        // конечная точка (i=N_aux+1) в любом случае подставляется явно как endpoint, не
        // пересчитывается.
        return endpoint.E0;
    }

    double SolveLambdaForCrack(PinPointResult crack, double N0, double Mx0, double My0)
    {
        double denom = N0 * N0 + Mx0 * Mx0 + My0 * My0;
        if (denom < 1e-30) return 0.0;
        return (crack.Load.N * N0 + crack.Load.Mx * Mx0 + crack.Load.My * My0) / denom;
    }

    /// <summary>
    /// Строит участок со сжатым пином, `ten=true`, продолжением направления/N-режима уч. "1"
    /// (используется дважды: веткой "предел раньше трещины" в этом же методе, где
    /// <paramref name="fromPoint"/> — точка 0, и петлёй в Task 7, где <paramref name="fromPoint"/>
    /// — точка 1). `endpoint.T` уже известен заранее — этот метод только расставляет внутренние
    /// точки между `fromPoint.T` и `endpoint.T`.
    /// </summary>
    List<BiaxialCurveScanPoint> BuildCompressionSweep(
        CompressionPinSolverFast solver, double N0, double Mx0, double My0, CurvatureNMode nMode,
        BiaxialCurveScanPoint fromPoint, double epsFrom, double epsTo, BiaxialCurveScanPoint endpoint)
    {
        var points = new List<BiaxialCurveScanPoint>(_auxPointsPerSegment);
        if (_stepMode == CurveStepMode.ByMoment)
        {
            // Петля/"предел раньше трещины" в ByMoment не строится вообще (нестабильна) —
            // прямая линия fromPoint→endpoint.
            points.Add(endpoint);
            return points;
        }

        double endpointT = Math.Sqrt(endpoint.Ky * endpoint.Ky + endpoint.Kz * endpoint.Kz);
        double dNdk = nMode == CurvatureNMode.Constant ? 0.0 : N0;
        Kurvature? seed = new Kurvature { e0 = fromPoint.E0, ky = fromPoint.Ky, kz = fromPoint.Kz };
        for (int i = 1; i <= _auxPointsPerSegment; i++)
        {
            double frac = (double)i / (_auxPointsPerSegment + 1);
            double epsPin = epsFrom + frac * (epsTo - epsFrom);
            var point = solver.Solve(epsPin, N0, Mx0, My0, dNdk, seed);
            if (!point.Converged) continue;
            seed = point.Plane;
            points.Add(new BiaxialCurveScanPoint
            {
                N = point.Load.N, Mx = point.Load.Mx, My = point.Load.My,
                E0 = point.Plane.e0, Ky = point.Plane.ky, Kz = point.Plane.kz,
                T = fromPoint.T + frac * (endpointT - fromPoint.T),
                Segment = 2, Converged = true
            });
            _cancellationToken.ThrowIfCancellationRequested();
        }
        points.Add(endpoint);
        return points;
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
