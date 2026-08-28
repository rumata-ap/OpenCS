namespace CScore;

/// <summary>Результат пин-решения — плоскость деформаций и усилия в найденной точке.</summary>
public readonly record struct PinPointResult(Kurvature Plane, Load Load, bool Converged, bool UsedFallback);

/// <summary>
/// Быстрый Ньютон-решатель уч. "1" диаграммы кривизна-момент (0 → точка трещинообразования):
/// пин на самой растянутой вершине контура бетона, деформация в ней подставляется напрямую
/// (см. <see cref="PinnedEquilibriumNewton"/>), развёртка целевого <c>epsPin</c> от 0 до
/// <see cref="CrackingSolver.TensionLimit"/> даёт как саму точку 1 (epsPin = TensionLimit()),
/// так и вспомогательные точки уч. "1". Модель — всегда <c>ten:true</c> (бетон работает на
/// растяжение). При расхождении Ньютона — fallback на бисекционный
/// <see cref="CrackingSolver.CrackingMoment"/>.
/// </summary>
public sealed class TensionPinSolverFast
{
    readonly CrossSection _section;
    readonly CalcType _calc;
    readonly double _hDiff;
    readonly int _maxIter;
    readonly double _solverTol;
    readonly (double X, double Y)[] _contourPts;
    readonly (double X, double Y)[] _pinCandidates;
    readonly Func<double, double, bool>? _tensionZone;
    readonly double _yRef, _xRef;

    /// <param name="tensionZone">
    /// Предикат «эту точку контура растягивает внешняя нагрузка». Пин выбирается только среди
    /// таких точек: у сильно обжатого сечения выгиб от преднапряжения растягивает
    /// противоположную грань сильнее, чем нагрузка — свою, и пин садился бы на неё, давая
    /// «момент трещинообразования» там, где нагрузка на самом деле разгружает. <c>null</c> или
    /// предикат, не оставивший ни одной точки, — поведение прежнее (весь контур).
    /// </param>
    public TensionPinSolverFast(
        CrossSection section, CalcType calc = CalcType.N,
        double solverTol = 0.5, int newtonMaxIter = 60, double hDiff = 1e-6,
        Func<double, double, bool>? tensionZone = null)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _calc = calc;
        _hDiff = hDiff;
        _maxIter = newtonMaxIter;
        _solverTol = solverTol;
        _tensionZone = tensionZone;

        _contourPts = section.Areas
            .Where(IsConcreteArea)
            .Where(a => a.Hull != null)
            .SelectMany(a => a.Hull!.X.Zip(a.Hull!.Y, (x, y) => (X: x, Y: y)))
            .ToArray();
        if (_contourPts.Length == 0)
            throw new InvalidOperationException("TensionPinSolverFast: нет бетонного контура.");

        var candidates = tensionZone == null
            ? _contourPts
            : _contourPts.Where(p => tensionZone(p.X, p.Y)).ToArray();
        _pinCandidates = candidates.Length > 0 ? candidates : _contourPts;

        _yRef = Math.Max(_contourPts.Max(p => Math.Abs(p.Y)), 1e-12);
        _xRef = Math.Max(_contourPts.Max(p => Math.Abs(p.X)), 1e-12);
    }

    /// <summary>
    /// Находит равновесную плоскость при заданной абсолютной деформации <paramref name="epsPin"/>
    /// в наиболее растянутой точке контура, для цели <c>(Mx,My)=k·(mx,my)</c>, где N зависит от
    /// <paramref name="dNdk"/>: <c>|dNdk|&lt;=1e-30</c> — N фиксировано на <paramref name="n"/>
    /// (Constant N); иначе — N масштабируется вместе с моментом как <c>k·n</c> (Proportional N,
    /// вызывающая сторона передаёт <paramref name="dNdk"/><c>=n</c>). Тот же приём, что и в
    /// <c>LimitForceSolverFast.LoadAtK</c> — сохраняем единообразие условия по проекту.
    /// </summary>
    public PinPointResult Solve(double epsPin, double n, double mx, double my, double dNdk, Kurvature? seed)
    {
        var elasticGuess = seed ?? _section.Guess(new Load { N = n, Mx = mx, My = my });
        var strains = _pinCandidates.Select(p => elasticGuess.e0 + elasticGuess.ky * p.Y + elasticGuess.kz * p.X).ToArray();
        double maxEps = strains.Max();
        int iMax = Array.IndexOf(strains, maxEps);
        (double xA, double yA) = _pinCandidates[iMax];

        double kx0 = elasticGuess.kz, ky0 = elasticGuess.ky;
        double k0 = 1.0;
        if (Math.Abs(maxEps) > 1e-12)
        {
            double scale = epsPin / maxEps;
            if (double.IsFinite(scale) && scale > 0)
            {
                kx0 *= scale; ky0 *= scale; k0 = scale;
            }
        }

        Func<double, double> nFn = Math.Abs(dNdk) > 1e-30 ? (k => k * n) : (_ => n);
        var pinned = PinnedEquilibriumNewton.Solve(
            k => ForcesAt(k), xA, yA, epsPin, kx0, ky0, k0,
            nFn, k => k * mx, k => k * my,
            dNdk, mx, my, _yRef, _xRef, _hDiff, _maxIter);

        if (pinned.Converged)
        {
            var load = _section.Integral(pinned.Plane, _calc, ten: true, ca: true);
            return new PinPointResult(pinned.Plane, load, true, UsedFallback: false);
        }

        return SolveFallback(epsPin, n, mx, my);
    }

    (double N, double Mx, double My) ForcesAt(Kurvature k)
    {
        var load = _section.Integral(k, _calc, ten: true, ca: true);
        return (load.N, load.Mx, load.My);
    }

    PinPointResult SolveFallback(double epsPin, double n, double mx, double my)
    {
        // allowPinSolver: false — иначе бисекция позвала бы этот же решатель обратно.
        var legacy = new CrackingSolver(_section, _calc, epsTensionLimit: epsPin, solverTol: _solverTol,
            tensionZone: _tensionZone, allowPinSolver: false);
        var res = legacy.CrackingMoment(n, mx, my);
        if (!res.Converged || res.StrainPlane is not Kurvature plane)
            return new PinPointResult(default, default, false, UsedFallback: true);
        var load = _section.Integral(plane, _calc, ten: true, ca: true);
        return new PinPointResult(plane, load, true, UsedFallback: true);
    }

    static bool IsConcreteArea(MaterialArea area) =>
        area.Material?.Type == MatType.Concrete ||
        (area.Material?.Type == MatType.Custom && area.Material.BaseType == MatType.Concrete);
}
