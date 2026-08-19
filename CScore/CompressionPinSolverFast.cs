namespace CScore;

/// <summary>
/// Быстрый Ньютон-решатель петли диаграммы кривизна-момент (точка1 → точка2): пин на самой
/// сжатой вершине контура бетона, модель <c>ten</c> задаётся конструктором (для петли — всегда
/// <c>true</c>, продолжение дотрещинной модели уч. "1"). Развёртка целевого <c>epsPin</c> —
/// от деформации сжатия в точке1 до деформации сжатия в точке2. Без арматурной фазы (петля не
/// проверяется на предел растяжения арматуры — вне её физического смысла). При расхождении
/// Ньютона — fallback на <see cref="StrainSolver"/> (без pin-подстановки, обычный 3-неизвестных
/// Ньютон на явном целевом (N,Mx,My), теряет гарантию точного epsPin, но сохраняет равновесие).
/// </summary>
public sealed class CompressionPinSolverFast
{
    readonly CrossSection _section;
    readonly CalcType _calc;
    readonly bool _ten;
    readonly double _hDiff;
    readonly int _maxIter;
    readonly (double X, double Y)[] _contourPts;
    readonly double _yRef, _xRef;

    public CompressionPinSolverFast(
        CrossSection section, CalcType calc = CalcType.N, bool ten = true,
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
        if (_contourPts.Length == 0)
            throw new InvalidOperationException("CompressionPinSolverFast: нет бетонного контура.");

        _yRef = Math.Max(_contourPts.Max(p => Math.Abs(p.Y)), 1e-12);
        _xRef = Math.Max(_contourPts.Max(p => Math.Abs(p.X)), 1e-12);
    }

    /// <summary>
    /// Находит равновесную плоскость при заданной деформации в наиболее сжатой точке контура.
    /// N зависит от <paramref name="dNdk"/> — та же конвенция, что и в
    /// <see cref="TensionPinSolverFast.Solve"/>/`LimitForceSolverFast.LoadAtK`
    /// (<c>|dNdk|&lt;=1e-30</c> → N фиксировано на <paramref name="n"/>; иначе N=k·n).
    /// </summary>
    public PinPointResult Solve(double epsPin, double n, double mx, double my, double dNdk, Kurvature? seed)
    {
        var elasticGuess = seed ?? _section.Guess(new Load { N = n, Mx = mx, My = my });
        var strains = _contourPts.Select(p => elasticGuess.e0 + elasticGuess.ky * p.Y + elasticGuess.kz * p.X).ToArray();
        double minEps = strains.Min();
        int iMin = Array.IndexOf(strains, minEps);
        (double xA, double yA) = _contourPts[iMin];

        double kx0 = elasticGuess.kz, ky0 = elasticGuess.ky, k0 = 1.0;
        if (Math.Abs(minEps) > 1e-12)
        {
            double scale = epsPin / minEps;
            if (double.IsFinite(scale) && scale > 0) { kx0 *= scale; ky0 *= scale; k0 = scale; }
        }

        Func<double, double> nFn = Math.Abs(dNdk) > 1e-30 ? (k => k * n) : (_ => n);
        var pinned = PinnedEquilibriumNewton.Solve(
            k => ForcesAt(k), xA, yA, epsPin, kx0, ky0, k0,
            nFn, k => k * mx, k => k * my,
            dNdk, mx, my, _yRef, _xRef, _hDiff, _maxIter);

        if (pinned.Converged)
        {
            var load = _section.Integral(pinned.Plane, _calc, ten: _ten, ca: true);
            return new PinPointResult(pinned.Plane, load, true, UsedFallback: false);
        }

        return SolveFallback(n, mx, my, seed);
    }

    (double N, double Mx, double My) ForcesAt(Kurvature k)
    {
        var load = _section.Integral(k, _calc, ten: _ten, ca: true);
        return (load.N, load.Mx, load.My);
    }

    PinPointResult SolveFallback(double n, double mx, double my, Kurvature? seed)
    {
        var solver = new StrainSolver(_section, _calc, ten: _ten, ca: true);
        var plane = solver.Solve(n, mx, my, seed);
        if (!solver.Converged)
            return new PinPointResult(default, default, false, UsedFallback: true);
        var load = _section.Integral(plane, _calc, ten: _ten, ca: true);
        return new PinPointResult(plane, load, true, UsedFallback: true);
    }

    static bool IsConcreteArea(MaterialArea area) =>
        area.Material?.Type == MatType.Concrete ||
        (area.Material?.Type == MatType.Custom && area.Material.BaseType == MatType.Concrete);
}
