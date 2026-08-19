namespace CScore;

/// <summary>Результат решения управляющего пина с указанием, какая фибра/стержень governing.</summary>
public readonly record struct GoverningPinResult(
    Kurvature Plane, Load Load, bool Converged, bool UsedFallback, string Governing);

/// <summary>
/// Быстрый Ньютон-решатель уч. "3"/"4" диаграммы (точка2 → точка3/точка4): пин на управляющей
/// точке контура (сжатый бетон) или арматуре (растянутый стержень) — какая ближе к своему
/// пределу использования, та и pin. Развёртка по нормализованной доле использования
/// <c>μ = |eps|/|eps_limit(pin)|</c> вместо абсолютной деформации (сравнима между разными
/// материалами/точками, см. спеку "Переключение управляющей точки внутри участка"). Поддерживает
/// ψs-коррекцию (<paramref name="epsCrc"/> — карта фибра→деформация трещинообразования,
/// <c>null</c> — ψs выключена). При смене governing между вызовами — автоматический перепин
/// с одной повторной итерацией на том же целевом μ (аналог
/// <c>LimitForceSolverFast.RebarPhase</c>). При расхождении Ньютона — fallback на бисекцию по
/// масштабу цели с обычным <see cref="StrainSolver"/>.
/// </summary>
public sealed class GoverningPinSolverFast
{
    readonly CrossSection _section;
    readonly CalcType _calc;
    readonly bool _ten;
    readonly double _hDiff;
    readonly int _maxIter;
    readonly (double X, double Y)[] _contourPts;
    readonly (double X, double Y, double EpsLimit)[] _rebarPoints;
    readonly double _epsCuLimit;
    readonly double _yRef, _xRef;

    public GoverningPinSolverFast(
        CrossSection section, CalcType calc = CalcType.N, bool ten = false,
        double hDiff = 1e-6, int newtonMaxIter = 60)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _calc = calc;
        _ten = ten;
        _hDiff = hDiff;
        _maxIter = newtonMaxIter;

        _contourPts = section.Areas
            .Where(IsConcreteArea)
            .Where(a => a.Hull != null)
            .SelectMany(a => a.Hull!.X.Zip(a.Hull!.Y, (x, y) => (X: x, Y: y)))
            .ToArray();
        _epsCuLimit = ConcreteUltimateStrain(section, calc);

        _rebarPoints = section.Areas
            .Where(a => a.Material?.Type is MatType.ReSteelF or MatType.ReSteelU)
            .SelectMany(a => a.Fibers.Where(f => f.TypeFiber == FiberType.point)
                .Select(f => (f.X, f.Y, EpsLimit: RebarUltimateStrain(a, calc))))
            .ToArray();

        var allPts = _contourPts.Select(p => (p.X, p.Y))
            .Concat(_rebarPoints.Select(p => (p.X, p.Y))).ToArray();
        _yRef = Math.Max(allPts.Length > 0 ? allPts.Max(p => Math.Abs(p.Y)) : 1e-12, 1e-12);
        _xRef = Math.Max(allPts.Length > 0 ? allPts.Max(p => Math.Abs(p.X)) : 1e-12, 1e-12);
    }

    /// <summary>
    /// Целевая доля использования (1.0 = точка 4/предел, соответствует существующим критериям
    /// <see cref="LimitForceSolverFast"/>). Для точки 3 (текучесть) вызывающая сторона
    /// пересчитывает "долю использования" ОТНОСИТЕЛЬНО деформации текучести Ry/E конкретного
    /// governing-стержня — см. <see cref="BiaxialCurvatureCurveSolver"/> использование. N
    /// зависит от <paramref name="dNdk"/> — та же конвенция, что и в
    /// <see cref="TensionPinSolverFast.Solve"/> (Constant N: 0.0; Proportional N: <paramref
    /// name="n"/>).
    /// </summary>
    public GoverningPinResult Solve(
        double targetUtilization, double n, double mx, double my, double dNdk,
        Kurvature seed, IReadOnlyDictionary<Fiber, double>? epsCrc)
    {
        var (xA, yA, epsLimit, governing) = FindGoverningPin(seed);
        double epsPin = epsLimit * targetUtilization;
        Func<double, double> nFn = Math.Abs(dNdk) > 1e-30 ? (k => k * n) : (_ => n);

        for (int outer = 0; outer < 3; outer++)
        {
            // Начальное приближение: (kx0,ky0) масштабируются по деформации (та же плоскость,
            // что даёт epsPin в pin-точке), но k0 — НЕ через ту же деформационную пропорцию
            // (материал существенно нелинеен вблизи предела, линейная экстраполяция даёт k0 в
            // разы/десятки раз больше истинного — воспроизведено эмпирически на Example47,
            // Mx=-36: линейная оценка давала k0≈41, истинное k≈2.48). Вместо этого — проекция
            // ФАКТИЧЕСКИХ (нелинейных) усилий на масштабированной плоскости на целевое
            // направление, тот же приём, что и в
            // `LimitForceSolverFast.TryEstimateCompressionStart`. Пересчитывается на каждой
            // итерации — после перепина seed/xA/yA уже другие.
            double kx0 = seed.kz, ky0 = seed.ky, k0 = 1.0;
            double epsAtSeed = seed.e0 + seed.ky * yA + seed.kz * xA;
            if (Math.Abs(epsAtSeed) > 1e-12)
            {
                double scale0 = epsPin / epsAtSeed;
                if (double.IsFinite(scale0) && scale0 > 0)
                {
                    kx0 *= scale0; ky0 *= scale0;
                    var spScaled = PinnedEquilibriumNewton.MakeSp(kx0, ky0, xA, yA, epsPin);
                    var fScaled = ForcesAt(spScaled, epsCrc);
                    double denom = dNdk * dNdk + mx * mx + my * my;
                    if (denom > 1e-30)
                    {
                        double k0Proj = (fScaled.N * dNdk + fScaled.Mx * mx + fScaled.My * my) / denom;
                        if (double.IsFinite(k0Proj) && k0Proj > 0) k0 = k0Proj;
                    }
                }
            }

            var pinned = PinnedEquilibriumNewton.Solve(
                k => ForcesAt(k, epsCrc), xA, yA, epsPin,
                kx0, ky0, k0,
                nFn, k => k * mx, k => k * my,
                dNdk, mx, my, _yRef, _xRef, _hDiff, _maxIter);

            if (!pinned.Converged)
                return SolveFallback(n, mx, my, dNdk, xA, yA, epsPin, seed, epsCrc, governing);

            var (newXA, newYA, newEpsLimit, newGoverning) = FindGoverningPin(pinned.Plane);
            if (newGoverning == governing || (Math.Abs(newXA - xA) < 1e-9 && Math.Abs(newYA - yA) < 1e-9))
            {
                var f = ForcesAt(pinned.Plane, epsCrc);
                return new GoverningPinResult(
                    pinned.Plane, new Load { N = f.N, Mx = f.Mx, My = f.My },
                    true, UsedFallback: false, governing);
            }

            xA = newXA; yA = newYA; epsLimit = newEpsLimit; governing = newGoverning;
            epsPin = epsLimit * targetUtilization;
            seed = pinned.Plane;
        }

        return SolveFallback(n, mx, my, dNdk, xA, yA, epsPin, seed, epsCrc, governing);
    }

    (double N, double Mx, double My) ForcesAt(Kurvature k, IReadOnlyDictionary<Fiber, double>? epsCrc)
    {
        var raw = _section.Integral(k, _calc, ten: _ten, ca: true);
        var load = epsCrc == null ? raw : Curvature8232.ApplyPsiCorrection(_section, k, raw, epsCrc);
        return (load.N, load.Mx, load.My);
    }

    (double X, double Y, double EpsLimit, string Governing) FindGoverningPin(Kurvature at)
    {
        double bestRatio = double.NegativeInfinity;
        (double X, double Y, double EpsLimit, string Governing) best = default;

        foreach (var p in _contourPts)
        {
            double eps = at.e0 + at.ky * p.Y + at.kz * p.X;
            double ratio = eps / _epsCuLimit; // оба отрицательны при сжатии — ratio положителен
            if (ratio > bestRatio) { bestRatio = ratio; best = (p.X, p.Y, _epsCuLimit, "concrete"); }
        }
        foreach (var r in _rebarPoints)
        {
            double eps = at.e0 + at.ky * r.Y + at.kz * r.X;
            double ratio = r.EpsLimit > 0 ? eps / r.EpsLimit : 0.0;
            if (ratio > bestRatio) { bestRatio = ratio; best = (r.X, r.Y, r.EpsLimit, "rebar"); }
        }
        if (best.EpsLimit == 0.0)
            throw new InvalidOperationException("GoverningPinSolverFast: не удалось определить governing-точку.");
        return best;
    }

    /// <summary>
    /// Fallback при расхождении пин-Ньютона: бисекция по масштабу <c>k</c> цели
    /// <c>(dNdk!=0 ? k·n : n, k·mx, k·my)</c>, на каждом шаге — обычный `StrainSolver.Solve`
    /// (без pin-подстановки), критерий — деформация в УЖЕ известной pin-точке
    /// (<paramref name="xA"/>,<paramref name="yA"/>) равна <paramref name="epsPin"/>. Та же
    /// схема, что и у старого `FindYieldT`/`FindUltimateT` (внешняя бисекция + внутреннее
    /// равновесие), используется только как редкий safety net.
    /// </summary>
    GoverningPinResult SolveFallback(
        double n, double mx, double my, double dNdk, double xA, double yA, double epsPin,
        Kurvature seed, IReadOnlyDictionary<Fiber, double>? epsCrc, string governing)
    {
        Func<Kurvature, Load>? evaluate = epsCrc == null
            ? null
            : k => Curvature8232.ApplyPsiCorrection(_section, k, _section.Integral(k, _calc, _ten, true), epsCrc);

        (Kurvature plane, double epsAtPin, bool ok) EvalAtK(double k)
        {
            double nTarget = Math.Abs(dNdk) > 1e-30 ? k * n : n;
            var solver = new StrainSolver(_section, _calc, ten: _ten, ca: true, evaluate: evaluate);
            var plane = solver.Solve(nTarget, k * mx, k * my, seed);
            if (!solver.Converged) return (default, 0.0, false);
            double eps = plane.e0 + plane.ky * yA + plane.kz * xA;
            return (plane, eps, true);
        }

        double lo = 0.0, hi = 1.0;
        var (loPlane, loEps, loOk) = EvalAtK(lo);
        var (hiPlane, hiEps, hiOk) = EvalAtK(hi);
        for (int expand = 0; expand < 20 && hiOk && Math.Sign(hiEps - epsPin) == Math.Sign(loEps - epsPin); expand++)
        {
            lo = hi; loEps = hiEps; hi *= 1.5;
            (hiPlane, hiEps, hiOk) = EvalAtK(hi);
        }
        if (!loOk || !hiOk)
            return new GoverningPinResult(default, default, false, UsedFallback: true, governing);

        Kurvature bestPlane = loPlane;
        for (int i = 0; i < 40; i++)
        {
            double mid = 0.5 * (lo + hi);
            var (midPlane, midEps, midOk) = EvalAtK(mid);
            if (!midOk) { hi = mid; continue; }
            bestPlane = midPlane;
            if (Math.Sign(midEps - epsPin) == Math.Sign(loEps - epsPin)) lo = mid; else hi = mid;
        }

        var load = evaluate != null ? evaluate(bestPlane) : _section.Integral(bestPlane, _calc, _ten, true);
        return new GoverningPinResult(bestPlane, load, true, UsedFallback: true, governing);
    }

    static bool IsConcreteArea(MaterialArea area) =>
        area.Material?.Type == MatType.Concrete ||
        (area.Material?.Type == MatType.Custom && area.Material.BaseType == MatType.Concrete);

    static double ConcreteUltimateStrain(CrossSection section, CalcType calc)
    {
        double min = double.PositiveInfinity;
        foreach (var area in section.Areas)
        {
            if (!IsConcreteArea(area)) continue;
            var chars = area.Material?.GetChars(calc);
            if (chars == null) continue;
            if (chars.Ec2 < min) min = chars.Ec2;
        }
        return double.IsPositiveInfinity(min) ? -0.0035 : min;
    }

    static double RebarUltimateStrain(MaterialArea area, CalcType calc)
    {
        var chars = area.Material?.GetChars(calc);
        return chars != null && double.IsFinite(chars.Et2) && chars.Et2 > 0 ? chars.Et2 : 0.025;
    }
}
