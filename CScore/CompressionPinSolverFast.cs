namespace CScore;

/// <summary>
/// Быстрый Ньютон-решатель петли диаграммы кривизна-момент (точка1 → точка2): пин на самой
/// сжатой вершине контура бетона, модель <c>ten</c> задаётся конструктором (для петли — всегда
/// <c>true</c>, продолжение дотрещинной модели уч. "1"). Развёртка целевого <c>epsPin</c> —
/// от деформации сжатия в точке1 до деформации сжатия в точке2. Без арматурной фазы (петля не
/// проверяется на предел растяжения арматуры — вне её физического смысла). При расхождении
/// Ньютона — fallback на бисекцию по масштабу цели с обычным <see cref="StrainSolver"/>
/// (без pin-подстановки), критерий — деформация в известной pin-точке равна epsPin (тот же
/// паттерн, что и в <see cref="GoverningPinSolverFast"/>).
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
        // При seed с нулевой (или очень малой) кривизной (типично — точка "0", Ky=Kz=0)
        // деформация одинакова по всему контуру, и выбор "самой сжатой" вершины по индексу
        // произволен — под развившейся кривизной эта вершина может оказаться совсем не той,
        // где деформация реально минимальна, что ломает и Ньютон с пин-подстановкой, и
        // bisection-фолбэк (обнаружено на "нет трещины" сценарии при N вблизи предельной
        // несущей способности по сжатию, 2026-08-19). Разрешаем тай направлением цели (mx,my).
        if (strains.Count(s => Math.Abs(s - minEps) < 1e-12) > 1)
        {
            var dirGuess = _section.Guess(new Load { N = n, Mx = mx, My = my });
            if (Math.Abs(dirGuess.ky) > 1e-12 || Math.Abs(dirGuess.kz) > 1e-12)
            {
                var dirStrains = _contourPts.Select(p => dirGuess.ky * p.Y + dirGuess.kz * p.X).ToArray();
                iMin = Array.IndexOf(dirStrains, dirStrains.Min());
            }
        }
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

        return SolveFallback(n, mx, my, dNdk, xA, yA, epsPin, k0, seed);
    }

    (double N, double Mx, double My) ForcesAt(Kurvature k)
    {
        var load = _section.Integral(k, _calc, ten: _ten, ca: true);
        return (load.N, load.Mx, load.My);
    }

    /// <summary>
    /// Фолбэк при расхождении пин-Ньютона — бисекция по масштабу цели <c>k</c>
    /// (<c>(dNdk!=0 ? k·n : n, k·mx, k·my)</c>), критерий — деформация в УЖЕ известной
    /// pin-точке (<paramref name="xA"/>,<paramref name="yA"/>) равна <paramref name="epsPin"/>.
    /// В отличие от <see cref="GoverningPinSolverFast"/> (где бисекция стартует от [0,1] —
    /// там 1.0 физически осмыслен, это ВСЕГДА предел несущей способности), здесь k=1
    /// соответствует НЕ локально ожидаемому масштабу, а направлению КОНЕЧНОЙ точки всей
    /// диаграммы (Mx0,My0) — диапазон [0,1] может охватывать несколько физически разных
    /// равновесий (разная кривизна даёт то же |eps| в pin-точке), и бисекция от фиксированных
    /// границ иногда попадает не на ту ветвь (обнаружено на реальном сечении пользователя
    /// 2026-08-19 — "провал" момента внутри петли сразу после успешного соседнего шага).
    /// Поэтому поиск здесь стартует от локальной линейной оценки <paramref name="k0"/>
    /// (та же, что и для начального приближения в Solve выше) и раскрывает границу только в
    /// сторону, требуемую знаком невязки — держит бисекцию на одной физической ветви вместо
    /// широкого поиска по всему [0,1].
    /// </summary>
    PinPointResult SolveFallback(
        double n, double mx, double my, double dNdk, double xA, double yA, double epsPin, double k0,
        Kurvature? seed)
    {
        (Kurvature plane, double epsAtPin, bool ok) EvalAtK(double k)
        {
            double nTarget = Math.Abs(dNdk) > 1e-30 ? k * n : n;
            var solver = new StrainSolver(_section, _calc, ten: _ten, ca: true);
            var plane = solver.Solve(nTarget, k * mx, k * my, seed);
            if (!solver.Converged) return (default, 0.0, false);
            double eps = plane.e0 + plane.ky * yA + plane.kz * xA;
            return (plane, eps, true);
        }

        double anchor = double.IsFinite(k0) && k0 > 1e-9 ? k0 : 1.0;
        var (anchorPlane, anchorEps, anchorOk) = EvalAtK(anchor);
        if (!anchorOk && anchor != 1.0)
        {
            anchor = 1.0;
            (anchorPlane, anchorEps, anchorOk) = EvalAtK(anchor);
        }
        if (!anchorOk)
            return new PinPointResult(default, default, false, UsedFallback: true);

        double lo, hi, loEps, hiEps;
        Kurvature loPlane, hiPlane;
        bool loOk, hiOk;

        // eps(k) убывает с ростом k (больше момента — сильнее сжата pin-точка). Если якорь
        // ещё недосжат (epsAtAnchor > epsPin) — раскрываем верхнюю границу; если уже
        // пересжат — раскрываем нижнюю.
        if (anchorEps > epsPin)
        {
            lo = anchor; loEps = anchorEps; loPlane = anchorPlane; loOk = true;
            hi = anchor; hiEps = anchorEps; hiPlane = anchorPlane; hiOk = true;
            for (int expand = 0; expand < 30 && hiOk && hiEps > epsPin; expand++)
            {
                hi = hi * 1.4 + 0.05;
                (hiPlane, hiEps, hiOk) = EvalAtK(hi);
            }
        }
        else
        {
            hi = anchor; hiEps = anchorEps; hiPlane = anchorPlane; hiOk = true;
            lo = anchor; loEps = anchorEps; loPlane = anchorPlane; loOk = true;
            for (int expand = 0; expand < 30 && loOk && loEps <= epsPin && lo > 1e-9; expand++)
            {
                lo *= 0.65;
                (loPlane, loEps, loOk) = EvalAtK(lo);
            }
        }
        if (!loOk || !hiOk)
            return new PinPointResult(default, default, false, UsedFallback: true);

        Kurvature bestPlane = loPlane;
        for (int i = 0; i < 40; i++)
        {
            double mid = 0.5 * (lo + hi);
            var (midPlane, midEps, midOk) = EvalAtK(mid);
            if (!midOk) { hi = mid; continue; }
            bestPlane = midPlane;
            if (midEps > epsPin) lo = mid; else hi = mid;
        }

        var load = _section.Integral(bestPlane, _calc, ten: _ten, ca: true);
        return new PinPointResult(bestPlane, load, true, UsedFallback: true);
    }

    static bool IsConcreteArea(MaterialArea area) =>
        area.Material?.Type == MatType.Concrete ||
        (area.Material?.Type == MatType.Custom && area.Material.BaseType == MatType.Concrete);
}
