using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CScore;

/// <summary>Режим продольной силы при скане диаграммы кривизна-момент.</summary>
public enum CurvatureNMode { Constant, Proportional }

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
    readonly int _pointsPerSegment;
    readonly CancellationToken _cancellationToken;

    const double DirectionEps = 1e-9;
    const double InitialCurvatureBracket = 1e-5;

    public BiaxialCurvatureCurveSolver(
        CrossSection section,
        CalcType calcCrc = CalcType.N,
        CalcType calcService = CalcType.N,
        double solverTol = 0.5,
        int solverMaxIter = 80,
        double solverH = 1e-7,
        bool centralJacobian = true,
        int pointsPerSegment = 401,
        CancellationToken cancellationToken = default)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _calcCrc = calcCrc;
        _calcService = calcService;
        _solverTol = solverTol;
        _solverMaxIter = solverMaxIter;
        _solverH = solverH;
        _centralJacobian = centralJacobian;
        _pointsPerSegment = pointsPerSegment < 2 ? 2 : pointsPerSegment;
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

        double mag = Math.Sqrt(Mx0 * Mx0 + My0 * My0);
        double uky = Mx0 / mag, ukz = My0 / mag;

        var crackSolver = new CrackingSolver(_section, _calcCrc,
            solverTol: _solverTol, solverMaxIter: _solverMaxIter, solverH: _solverH);

        BiaxialCurveScanPoint crackPoint;
        bool crackFound;
        if (nMode == CurvatureNMode.Constant)
        {
            var crc = crackSolver.CrackingCurvature(N0, uky, ukz);
            crackFound = crc.Converged;
            crackPoint = new BiaxialCurveScanPoint
            {
                N = crc.N, Mx = crc.Mx, My = crc.My, E0 = crc.E0, Ky = crc.Ky, Kz = crc.Kz,
                T = Math.Sqrt(crc.Ky * crc.Ky + crc.Kz * crc.Kz),
                Segment = 1, Converged = crc.Converged
            };
        }
        else
        {
            var crc = crackSolver.CrackingLoadFactor(N0, Mx0, My0);
            crackFound = crc.Converged;
            var plane = crc.StrainPlane ?? default;
            crackPoint = new BiaxialCurveScanPoint
            {
                N = crc.N, Mx = crc.Mx, My = crc.My, E0 = plane.e0, Ky = plane.ky, Kz = plane.kz,
                T = crc.Lambda,
                Segment = 1, Converged = crc.Converged
            };
        }

        if (!crackFound)
            return ComputeUncrackedOnly(result, N0, Mx0, My0, nMode, uky, ukz);

        result.Cracking = crackPoint;
        result.Points.AddRange(BuildSegment1(N0, Mx0, My0, nMode, uky, ukz, crackPoint));
        _cancellationToken.ThrowIfCancellationRequested();

        // ВАЖНО: "t" передаваемый ниже в EvaluatePostCrackAtT — это ГЛОБАЛЬНЫЙ параметр скана
        // (κ-магнитуда вдоль луча либо λ от исходного (N0,Mx0,My0)), а не относительный к
        // crackPoint масштаб. Пересчётная точка получается вызовом с t=crackPoint.T — тем
        // самым она гарантированно нацелена ровно на (crackPoint.N,Mx,My) в обоих режимах
        // (см. блокер 2 ревью плана 2026-08-17-biaxial-moment-curvature-plan-review.md).
        var crackSeed = new Kurvature { e0 = crackPoint.E0, ky = crackPoint.Ky, kz = crackPoint.Kz };
        var transition = EvaluatePostCrackAtT(
            crackPoint.T, N0, Mx0, My0, nMode, uky, ukz, crackSeed, epsCrc: null);
        transition.T = crackPoint.T;
        transition.Segment = 2;
        result.CrackTransitionPoint = transition;
        if (!transition.Converged)
        {
            result.Status = "error";
            return result;
        }
        if (!usePsi)
            result.Points.Add(transition);

        // εs,crc снимается с ПОСТтрещинной плоскости (transition), а не с дотрещинной
        // (crackPoint) — методология проекта, см. CScore/CrackWidthSolver.cs:706-713 и
        // CScore/TotalCurvatureSolver.cs:162 (EpsCrcByFiber вызывается от planeCrcShort,
        // т.е. от плоскости ПОСЛЕ пересчёта по посттрещинной модели, не от исходной).
        var epsCrc = EpsCrcByFiber(transition);
        var epsCrcForMain = usePsi ? epsCrc : null;
        var seedPlane = new Kurvature { e0 = transition.E0, ky = transition.Ky, kz = transition.Kz };
        double tStart = transition.T;

        // Единая граница (модель БЕЗ ψs) — одновременно эталон для клиппинга и область
        // сканирования уч. 3-4 (см. "Отклонения от буквы спеки" п.3 этого плана).
        double tUltimate = FindUltimateT(seedPlane, N0, Mx0, My0, nMode, uky, ukz, tStart);
        var ultimateReferencePoint = EvaluatePostCrackAtT(
            tUltimate, N0, Mx0, My0, nMode, uky, ukz, seedPlane, epsCrc: null);
        ultimateReferencePoint.T = tUltimate;
        ultimateReferencePoint.Segment = 4;
        result.UltimateReference = ultimateReferencePoint;

        double? tYield = FindYieldT(tStart, tUltimate, N0, Mx0, My0, nMode, uky, ukz,
            seedPlane, epsCrcForMain);
        // Вырожденный уч.3: текучесть наступает практически сразу после трещины — не строить
        // отдельный участок из повторяющихся точек (см. замечание 5 ревью плана).
        if (tYield.HasValue && Math.Abs(tYield.Value - tStart) <= 1e-6 * Math.Max(1.0, Math.Abs(tUltimate - tStart)))
            tYield = null;

        var clipReference = usePsi ? ultimateReferencePoint : null;

        if (tYield.HasValue)
        {
            var segment3 = BuildPostCrackSegment(tStart, tYield.Value, 3,
                N0, Mx0, My0, nMode, uky, ukz, seedPlane, epsCrcForMain,
                clipReference, result.HasMx, result.HasMy);
            result.Points.AddRange(segment3);
            result.Yield = segment3.Count > 0 ? segment3[^1] : null;
        }

        double tSegment4From = tYield ?? tStart;
        var segment4 = BuildPostCrackSegment(tSegment4From, tUltimate, 4,
            N0, Mx0, My0, nMode, uky, ukz, seedPlane, epsCrcForMain,
            clipReference, result.HasMx, result.HasMy);
        result.Points.AddRange(segment4);
        result.Ultimate = segment4.Count > 0 ? segment4[^1] : null;

        result.Status = result.Ultimate is { Converged: true } ? "ok" : "partial";
        return result;
    }

    List<BiaxialCurveScanPoint> BuildSegment1(
        double N0, double Mx0, double My0, CurvatureNMode nMode, double uky, double ukz,
        BiaxialCurveScanPoint crackPoint)
    {
        var points = new List<BiaxialCurveScanPoint>(_pointsPerSegment);
        double tEnd = crackPoint.T;
        double? seedE0 = 0.0;
        Kurvature? seedPlane = null;
        for (int i = 0; i < _pointsPerSegment; i++)
        {
            double t = tEnd * i / (_pointsPerSegment - 1);
            BiaxialCurveScanPoint point;
            if (nMode == CurvatureNMode.Constant)
            {
                point = SolvePreCrackConstant(N0, t * uky, t * ukz, seedE0);
                if (point.Converged) seedE0 = point.E0;
            }
            else
            {
                point = SolvePreCrackProportional(t, N0, Mx0, My0, seedPlane);
                if (point.Converged) seedPlane = new Kurvature { e0 = point.E0, ky = point.Ky, kz = point.Kz };
            }
            point.T = t;
            point.Segment = 1;
            points.Add(point);
            _cancellationToken.ThrowIfCancellationRequested();
        }
        if (points.Count > 0) points[^1] = crackPoint;
        return points;
    }

    List<BiaxialCurveScanPoint> BuildPostCrackSegment(
        double tFrom, double tTo, int segment,
        double N0, double Mx0, double My0, CurvatureNMode nMode, double uky, double ukz,
        Kurvature seedPlane, IReadOnlyDictionary<Fiber, double>? epsCrc,
        BiaxialCurveScanPoint? clipReference, bool hasMx, bool hasMy)
    {
        var points = new List<BiaxialCurveScanPoint>(_pointsPerSegment);
        Kurvature? seed = seedPlane;
        for (int i = 0; i < _pointsPerSegment; i++)
        {
            double t = tFrom + (tTo - tFrom) * i / (_pointsPerSegment - 1);
            var point = EvaluatePostCrackAtT(t, N0, Mx0, My0, nMode, uky, ukz, seed, epsCrc);
            if (point.Converged) seed = new Kurvature { e0 = point.E0, ky = point.Ky, kz = point.Kz };
            point.T = t;
            point.Segment = segment;
            if (clipReference != null) ClipPoint(point, clipReference, hasMx, hasMy);
            points.Add(point);
            _cancellationToken.ThrowIfCancellationRequested();
        }
        return points;
    }

    static void ClipPoint(BiaxialCurveScanPoint point, BiaxialCurveScanPoint reference, bool hasMx, bool hasMy)
    {
        if (!point.Converged || !reference.Converged) return;
        if (hasMx && Math.Abs(point.Mx) > Math.Abs(reference.Mx))
        {
            point.Mx = Math.Sign(point.Mx == 0 ? reference.Mx : point.Mx) * Math.Abs(reference.Mx);
            point.Clipped = true;
        }
        if (hasMy && Math.Abs(point.My) > Math.Abs(reference.My))
        {
            point.My = Math.Sign(point.My == 0 ? reference.My : point.My) * Math.Abs(reference.My);
            point.Clipped = true;
        }
    }

    BiaxialCurveScanPoint EvaluatePostCrackAtT(
        double t, double N0, double Mx0, double My0, CurvatureNMode nMode, double uky, double ukz,
        Kurvature? seed, IReadOnlyDictionary<Fiber, double>? epsCrc)
        => nMode == CurvatureNMode.Constant
            ? SolveConstantPostCrack(N0, t * uky, t * ukz, seed?.e0, epsCrc)
            : SolveProportionalPostCrack(t, N0, Mx0, My0, seed, epsCrc);

    BiaxialCurveScanPoint SolvePreCrackConstant(double n, double ky, double kz, double? seedE0)
    {
        CurvatureEquilibriumResult eq;
        try
        {
            eq = CurvatureEquilibrium8232.Solve(
                e0 => _section.Integral(new Kurvature { e0 = e0, ky = ky, kz = kz }, _calcCrc, ten: true, ca: true),
                targetN: n, initialE0: seedE0 ?? 0.0, tolerance: _solverTol, maxIterations: 100);
        }
        catch (InvalidOperationException)
        {
            return new BiaxialCurveScanPoint { N = n, Ky = ky, Kz = kz, Converged = false };
        }
        if (!eq.Converged)
            return new BiaxialCurveScanPoint { N = eq.Load.N, Mx = eq.Load.Mx, My = eq.Load.My, E0 = eq.E0, Ky = ky, Kz = kz, Converged = false };
        return new BiaxialCurveScanPoint { N = eq.Load.N, Mx = eq.Load.Mx, My = eq.Load.My, E0 = eq.E0, Ky = ky, Kz = kz, Converged = true };
    }

    BiaxialCurveScanPoint SolvePreCrackProportional(double lambda, double n0, double mx0, double my0, Kurvature? seed)
    {
        var solver = new StrainSolver(_section, _calcCrc, ten: true, ca: true,
            tol: _solverTol, maxIter: _solverMaxIter, h: _solverH, centralJacobian: _centralJacobian);
        var plane = solver.Solve(lambda * n0, lambda * mx0, lambda * my0, seed);
        if (!solver.Converged)
            return new BiaxialCurveScanPoint { N = lambda * n0, Ky = plane.ky, Kz = plane.kz, Converged = false };
        var load = _section.Integral(plane, _calcCrc, ten: true, ca: true);
        return new BiaxialCurveScanPoint { N = load.N, Mx = load.Mx, My = load.My, E0 = plane.e0, Ky = plane.ky, Kz = plane.kz, Converged = true };
    }

    BiaxialCurveScanPoint SolveConstantPostCrack(
        double n, double ky, double kz, double? seedE0, IReadOnlyDictionary<Fiber, double>? epsCrc)
    {
        Func<double, Load> evaluate = e0 =>
        {
            var k = new Kurvature { e0 = e0, ky = ky, kz = kz };
            var raw = _section.Integral(k, _calcService, ten: false, ca: true);
            return epsCrc == null ? raw : Curvature8232.ApplyPsiCorrection(_section, k, raw, epsCrc);
        };
        CurvatureEquilibriumResult eq;
        try
        {
            eq = CurvatureEquilibrium8232.Solve(evaluate, targetN: n,
                initialE0: seedE0 ?? 0.0, tolerance: _solverTol, maxIterations: 100);
        }
        catch (InvalidOperationException)
        {
            return new BiaxialCurveScanPoint { N = n, Ky = ky, Kz = kz, Converged = false };
        }
        if (!eq.Converged)
            return new BiaxialCurveScanPoint { N = eq.Load.N, Mx = eq.Load.Mx, My = eq.Load.My, E0 = eq.E0, Ky = ky, Kz = kz, Converged = false };
        return new BiaxialCurveScanPoint
        {
            N = eq.Load.N, Mx = eq.Load.Mx, My = eq.Load.My, E0 = eq.E0, Ky = ky, Kz = kz,
            Converged = true, PsiActive = epsCrc != null
        };
    }

    BiaxialCurveScanPoint SolveProportionalPostCrack(
        double lambda, double n0, double mx0, double my0, Kurvature? seed, IReadOnlyDictionary<Fiber, double>? epsCrc)
    {
        Func<Kurvature, Load>? evaluate = epsCrc == null
            ? null
            : k => Curvature8232.ApplyPsiCorrection(_section, k, _section.Integral(k, _calcService, ten: false, ca: true), epsCrc);
        var solver = new StrainSolver(_section, _calcService, ten: false, ca: true,
            tol: _solverTol, maxIter: _solverMaxIter, h: _solverH,
            centralJacobian: _centralJacobian, evaluate: evaluate);
        var plane = solver.Solve(lambda * n0, lambda * mx0, lambda * my0, seed);
        if (!solver.Converged)
            return new BiaxialCurveScanPoint { N = lambda * n0, Ky = plane.ky, Kz = plane.kz, Converged = false };
        var load = evaluate != null ? evaluate(plane) : _section.Integral(plane, _calcService, ten: false, ca: true);
        return new BiaxialCurveScanPoint
        {
            N = load.N, Mx = load.Mx, My = load.My, E0 = plane.e0, Ky = plane.ky, Kz = plane.kz,
            Converged = true, PsiActive = epsCrc != null
        };
    }

    double FindUltimateT(
        Kurvature seed, double N0, double Mx0, double My0, CurvatureNMode nMode,
        double uky, double ukz, double tStart)
    {
        double lo = tStart;
        double hi = Math.Max(tStart * 1.5, tStart + InitialCurvatureBracket);
        var hiPoint = EvaluatePostCrackAtT(hi, N0, Mx0, My0, nMode, uky, ukz, seed, null);
        if (hiPoint.Converged) seed = new Kurvature { e0 = hiPoint.E0, ky = hiPoint.Ky, kz = hiPoint.Kz };
        for (int i = 0; i < 40 && hiPoint.Converged && !IsAtUltimateStrain(hiPoint); i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            lo = hi;
            hi *= 1.5;
            hiPoint = EvaluatePostCrackAtT(hi, N0, Mx0, My0, nMode, uky, ukz, seed, null);
            if (hiPoint.Converged) seed = new Kurvature { e0 = hiPoint.E0, ky = hiPoint.Ky, kz = hiPoint.Kz };
        }

        // seed для бисекции — последняя сошедшаяся точка на текущей границе (важно для
        // Proportional: StrainSolver — Ньютон, далёкий старт может не сойтись).
        var bisectSeed = seed;
        for (int i = 0; i < 60; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            double mid = 0.5 * (lo + hi);
            var midPoint = EvaluatePostCrackAtT(mid, N0, Mx0, My0, nMode, uky, ukz, bisectSeed, null);
            if (midPoint.Converged)
                bisectSeed = new Kurvature { e0 = midPoint.E0, ky = midPoint.Ky, kz = midPoint.Kz };
            if (midPoint.Converged && IsAtUltimateStrain(midPoint)) hi = mid; else lo = mid;
        }
        return hi;
    }

    double? FindYieldT(
        double tStart, double tUltimate, double N0, double Mx0, double My0, CurvatureNMode nMode,
        double uky, double ukz, Kurvature seed, IReadOnlyDictionary<Fiber, double>? epsCrc)
    {
        double yieldStrain = MinRebarYieldStrain();
        if (double.IsPositiveInfinity(yieldStrain)) return null;

        var atUltimate = EvaluatePostCrackAtT(tUltimate, N0, Mx0, My0, nMode, uky, ukz, seed, epsCrc);
        if (!atUltimate.Converged || MaxTensileRebarStrain(atUltimate) < yieldStrain)
            return null;

        double lo = tStart, hi = tUltimate;
        var bisectSeed = seed;
        for (int i = 0; i < 60; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            double mid = 0.5 * (lo + hi);
            var midPoint = EvaluatePostCrackAtT(mid, N0, Mx0, My0, nMode, uky, ukz, bisectSeed, epsCrc);
            if (midPoint.Converged)
                bisectSeed = new Kurvature { e0 = midPoint.E0, ky = midPoint.Ky, kz = midPoint.Kz };
            if (midPoint.Converged && MaxTensileRebarStrain(midPoint) >= yieldStrain) hi = mid; else lo = mid;
        }
        return hi;
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

    BiaxialCurvatureCurveResult ComputeUncrackedOnly(
        BiaxialCurvatureCurveResult result, double N0, double Mx0, double My0,
        CurvatureNMode nMode, double uky, double ukz)
    {
        double tUltimate;
        try
        {
            tUltimate = FindUncrackedUltimateT(N0, Mx0, My0, nMode, uky, ukz);
        }
        catch (InvalidOperationException)
        {
            result.Status = "error";
            return result;
        }

        var points = new List<BiaxialCurveScanPoint>(_pointsPerSegment);
        Kurvature? seedPlane = null;
        double? seedE0 = 0.0;
        for (int i = 0; i < _pointsPerSegment; i++)
        {
            double t = tUltimate * i / (_pointsPerSegment - 1);
            BiaxialCurveScanPoint point;
            if (nMode == CurvatureNMode.Constant)
            {
                point = SolvePreCrackConstant(N0, t * uky, t * ukz, seedE0);
                if (point.Converged) seedE0 = point.E0;
            }
            else
            {
                point = SolvePreCrackProportional(t, N0, Mx0, My0, seedPlane);
                if (point.Converged) seedPlane = new Kurvature { e0 = point.E0, ky = point.Ky, kz = point.Kz };
            }
            point.Segment = 1;
            points.Add(point);
            _cancellationToken.ThrowIfCancellationRequested();
        }
        result.Points.AddRange(points);
        result.Cracking = new BiaxialCurveScanPoint { Converged = false };
        result.Ultimate = points.Count > 0 ? points[^1] : null;
        result.Status = result.Ultimate is { Converged: true } ? "partial" : "error";
        return result;
    }

    double FindUncrackedUltimateT(double N0, double Mx0, double My0, CurvatureNMode nMode, double uky, double ukz)
    {
        double lo = 0.0, hi = InitialCurvatureBracket;
        BiaxialCurveScanPoint hiPoint;
        int expand;
        for (expand = 0; expand < 60; expand++)
        {
            hiPoint = nMode == CurvatureNMode.Constant
                ? SolvePreCrackConstant(N0, hi * uky, hi * ukz, null)
                : SolvePreCrackProportional(hi, N0, Mx0, My0, null);
            if (hiPoint.Converged && IsAtUltimateStrain(hiPoint)) break;
            lo = hi;
            hi *= 1.5;
        }
        if (expand >= 60)
            throw new InvalidOperationException("Не удалось дойти до предельной деформации без трещины.");

        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5 * (lo + hi);
            var midPoint = nMode == CurvatureNMode.Constant
                ? SolvePreCrackConstant(N0, mid * uky, mid * ukz, null)
                : SolvePreCrackProportional(mid, N0, Mx0, My0, null);
            if (midPoint.Converged && IsAtUltimateStrain(midPoint)) hi = mid; else lo = mid;
        }
        return hi;
    }
}
