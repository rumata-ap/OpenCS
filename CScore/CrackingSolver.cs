using System;
using System.Linq;

namespace CScore;

/// <summary>Результат поиска момента трещинообразования сечения.</summary>
public sealed class CrackingSolverResult
{
    /// <summary>Момент трещинообразования относительно оси X, кН·м.</summary>
    public double Mx { get; set; }
    /// <summary>Момент трещинообразования относительно оси Y, кН·м.</summary>
    public double My { get; set; }
    /// <summary>Нормальная сила (неизменна при поиске), кН.</summary>
    public double N { get; set; }
    public bool Converged { get; set; }
    public int Iterations { get; set; }
    /// <summary>Плоскость деформаций в момент трещинообразования. Null, если не сошлось.</summary>
    public Kurvature? StrainPlane { get; set; }
    /// <summary>Максимальная растягивающая деформация бетона в момент трещинообразования.</summary>
    public double EpsMaxTension { get; set; }
}

/// <summary>Результат поиска момента трещинообразования вдоль луча кривизны (режим Constant).</summary>
public sealed class CrackingCurvatureResult
{
    public double N { get; set; }
    public double Mx { get; set; }
    public double My { get; set; }
    public double Ky { get; set; }
    public double Kz { get; set; }
    public double E0 { get; set; }
    public bool Converged { get; set; }
    public int Iterations { get; set; }
    public double EpsMaxTension { get; set; }
}

/// <summary>Результат поиска момента трещинообразования вдоль пропорционального пути нагрузки (режим Proportional).</summary>
public sealed class CrackingLoadFactorResult
{
    public double Lambda { get; set; }
    public double N { get; set; }
    public double Mx { get; set; }
    public double My { get; set; }
    public Kurvature? StrainPlane { get; set; }
    public bool Converged { get; set; }
    public int Iterations { get; set; }
    public double EpsMaxTension { get; set; }
}

/// <summary>
/// Момент трещинообразования поперечного сечения (СП 63.13330): бисекция по масштабу
/// момента при фиксированной нормальной силе и направлении (Mx:My = const) до достижения
/// максимальной растягивающей деформацией бетона предельного значения (из ветви растяжения
/// диаграммы бетона <paramref name="calcCrc"/>). Расчёт по образованию трещин — часть
/// расчёта по 2-й группе предельных состояний (п. 8.2.14 СП63.13330: "расчётные характеристики
/// материалов принимают для предельных состояний второй группы") — <paramref name="calcCrc"/>
/// обязан быть <see cref="CalcType.N"/> или <see cref="CalcType.NL"/>, использование
/// <see cref="CalcType.C"/>/<see cref="CalcType.CL"/> (1-я группа) здесь недопустимо.
/// Единицы: кН, кН·м, м. Требует, чтобы <see cref="CrossSection.ResolveAndBuildDiagramms"/>
/// уже был вызван вызывающей стороной.
/// </summary>
public sealed class CrackingSolver
{
    readonly CrossSection _section;
    readonly CalcType _calcCrc;
    readonly double? _epsTensionLimitOverride;
    readonly double _solverTol;
    readonly int _solverMaxIter;
    readonly double _solverH;
    readonly double _bisectTol;
    readonly int _bisectMaxIter;

    public CrackingSolver(
        CrossSection section,
        CalcType calcCrc = CalcType.N,
        double? epsTensionLimit = null,
        double solverTol = 0.5,
        int solverMaxIter = 60,
        double solverH = 1e-7,
        double bisectTol = 1e-6,
        int bisectMaxIter = 60)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _calcCrc = calcCrc;
        _epsTensionLimitOverride = epsTensionLimit;
        _solverTol = solverTol;
        _solverMaxIter = solverMaxIter;
        _solverH = solverH;
        _bisectTol = bisectTol;
        _bisectMaxIter = bisectMaxIter;
    }

    /// <summary>Предельная растягивающая деформация бетона (п. Г.1 СП63.13330), из диаграммы <see cref="_calcCrc"/>.</summary>
    public double TensionLimit()
    {
        if (_epsTensionLimitOverride.HasValue) return _epsTensionLimitOverride.Value;

        foreach (var area in _section.Areas)
        {
            if (!IsConcreteArea(area)) continue;
            if (!area.Diagramms.TryGetValue(_calcCrc, out var dgr)) continue;
            if (dgr.It.X.Length == 0) continue;
            return dgr.It.X.Max();
        }

        throw new InvalidOperationException(
            "Не удалось определить предельную растягивающую деформацию бетона: " +
            "в сечении нет бетонной MaterialArea с построенной диаграммой для заданного CalcType.");
    }

    double MaxTensionStrain(Kurvature k)
    {
        double max = 0.0;
        bool found = false;
        foreach (var area in _section.Areas)
        {
            if (!IsConcreteArea(area)) continue;
            if (area.Hull == null) continue;
            var xs = area.Hull.X;
            var ys = area.Hull.Y;
            for (int i = 0; i < xs.Count; i++)
            {
                double eps = k.e0 + k.ky * ys[i] + k.kz * xs[i];
                if (!found) { max = eps; found = true; }
                else if (eps > max) max = eps;
            }
        }
        return max;
    }

    (Kurvature? plane, double epsMax, bool ok) Evaluate(double n, double mx, double my)
    {
        var solver = new StrainSolver(_section, _calcCrc,
            tol: _solverTol, maxIter: _solverMaxIter, h: _solverH);
        var k = solver.Solve(n, mx, my);
        if (!solver.Converged) return (null, 0.0, false);
        return (k, MaxTensionStrain(k), true);
    }

    /// <summary>
    /// Найти момент трещинообразования при заданных N и направлении (Mx, My).
    /// Направление момента фиксировано; масштабируется методом бисекции.
    /// (Mx, My) нормируется к единичному вектору — бисекция начинает поиск верхней границы
    /// с масштаба b=1.0 (то есть буквально с переданных значений), поэтому без нормировки
    /// результат зависел бы от того, какой магнитудой вызывающий код представил направление
    /// (разные вызывающие стороны исторически передавали то "сырой" момент, то единичный
    /// вектор — расхождение до нескольких процентов в Mcrc из-за разной точности вложенного
    /// Ньютон-солвера на разных участках бисекции).
    /// </summary>
    public CrackingSolverResult CrackingMoment(double N, double Mx, double My)
    {
        double mag = Math.Sqrt(Mx * Mx + My * My);
        if (mag > 1e-12) { Mx /= mag; My /= mag; }

        double epsLimit = TensionLimit();

        var (plane0, eps0, ok0) = Evaluate(N, 0.0, 0.0);
        if (!ok0)
            return new CrackingSolverResult { Mx = 0, My = 0, N = N, Converged = false };

        if (eps0 >= epsLimit)
            return new CrackingSolverResult
            {
                Mx = 0, My = 0, N = N, Converged = true,
                StrainPlane = plane0, EpsMaxTension = eps0
            };

        double a = 0.0, b = 1.0;
        bool foundUpper = false;
        while (b < 1e9)
        {
            var (_, epsB, okB) = Evaluate(N, b * Mx, b * My);
            if (!okB || epsB >= epsLimit) { foundUpper = true; break; }
            a = b;
            b *= 2.0;
        }
        if (!foundUpper)
            return new CrackingSolverResult { Mx = b * Mx, My = b * My, N = N, Converged = false };

        Kurvature? bestPlane = plane0;
        double bestEps = eps0;
        int iter = 0;
        for (iter = 1; iter <= _bisectMaxIter; iter++)
        {
            double mid = 0.5 * (a + b);
            var (planeMid, epsMid, okMid) = Evaluate(N, mid * Mx, mid * My);

            if (!okMid)
            {
                b = mid;
            }
            else
            {
                bestPlane = planeMid;
                bestEps = epsMid;
                if (epsMid < epsLimit) a = mid; else b = mid;
            }

            if (Math.Abs(bestEps - epsLimit) <= _bisectTol) break;
        }

        double k = 0.5 * (a + b);
        // Для дискретного волоконного сечения последняя бетонная фибра может
        // перейти через Et2 скачком: между двумя соседними состояниями нет
        // плоскости с epsMax ровно равной epsLimit. Узкий интервал бисекции
        // уже является достаточным свидетельством найденной границы.
        bool converged = Math.Abs(bestEps - epsLimit) <= _bisectTol * 10.0 ||
                         Math.Abs(b - a) <= _bisectTol * 10.0;

        return new CrackingSolverResult
        {
            Mx = k * Mx,
            My = k * My,
            N = N,
            Converged = converged,
            Iterations = iter,
            StrainPlane = bestPlane,
            EpsMaxTension = bestEps
        };
    }

    const double InitialCurvatureBracket = 1e-5;

    /// <summary>
    /// Момент трещинообразования вдоль ЛУЧА КРИВИЗНЫ (не момента) при фиксированном N:
    /// бисекция по величине |κ| вдоль направления (kyDir,kzDir) до достижения максимальной
    /// растягивающей деформацией бетона по контуру предельного значения. В отличие от
    /// <see cref="CrackingMoment"/> (масштабирует момент), результат гарантированно лежит
    /// на луче кривизны — необходимо для согласованного скана
    /// <see cref="BiaxialCurvatureCurveSolver"/> в режиме Constant (см.
    /// docs/superpowers/specs/2026-08-16-biaxial-moment-curvature-design.md).
    /// </summary>
    public CrackingCurvatureResult CrackingCurvature(double N, double kyDir, double kzDir)
    {
        double dirMag = Math.Sqrt(kyDir * kyDir + kzDir * kzDir);
        if (dirMag < 1e-12)
            throw new ArgumentException("Направление кривизны не должно быть нулевым.", nameof(kyDir));
        double uky = kyDir / dirMag, ukz = kzDir / dirMag;

        double epsLimit = TensionLimit();

        var (plane0, eps0, ok0) = EvaluateAtCurvature(N, 0.0, 0.0);
        if (!ok0)
            return new CrackingCurvatureResult { N = N, Converged = false };

        if (eps0 >= epsLimit)
            return new CrackingCurvatureResult
            {
                N = N, Mx = 0.0, My = 0.0, Ky = 0.0, Kz = 0.0, E0 = plane0.e0,
                Converged = true, EpsMaxTension = eps0
            };

        double a = 0.0, b = InitialCurvatureBracket;
        bool foundUpper = false;
        (Kurvature plane, double eps, bool ok) upper = default;
        for (int expand = 0; expand < 40; expand++)
        {
            upper = EvaluateAtCurvature(N, b * uky, b * ukz);
            if (!upper.ok || upper.eps >= epsLimit) { foundUpper = true; break; }
            a = b;
            b *= 2.0;
        }
        if (!foundUpper)
            return new CrackingCurvatureResult { N = N, Ky = b * uky, Kz = b * ukz, Converged = false };

        Kurvature bestPlane = plane0;
        double bestEps = eps0;
        int iter = 0;
        for (iter = 1; iter <= _bisectMaxIter; iter++)
        {
            double mid = 0.5 * (a + b);
            var (planeMid, epsMid, okMid) = EvaluateAtCurvature(N, mid * uky, mid * ukz);
            if (!okMid) { b = mid; }
            else
            {
                bestPlane = planeMid;
                bestEps = epsMid;
                if (epsMid < epsLimit) a = mid; else b = mid;
            }
            if (Math.Abs(bestEps - epsLimit) <= _bisectTol) break;
        }

        double kMag = 0.5 * (a + b);
        var load = _section.Integral(bestPlane, _calcCrc, ten: true, ca: true);
        bool converged = Math.Abs(bestEps - epsLimit) <= _bisectTol * 10.0
            || Math.Abs(b - a) <= _bisectTol * 10.0;

        return new CrackingCurvatureResult
        {
            N = N, Mx = load.Mx, My = load.My,
            Ky = kMag * uky, Kz = kMag * ukz, E0 = bestPlane.e0,
            Converged = converged, Iterations = iter, EpsMaxTension = bestEps
        };
    }

    /// <summary>
    /// Момент трещинообразования вдоль ПРОПОРЦИОНАЛЬНОГО пути нагрузки
    /// (N,Mx,My)=λ·(N0,Mx0,My0): бисекция по общему коэффициенту λ (масштабирует N вместе
    /// с моментом, в отличие от <see cref="CrackingMoment"/>, где N фиксировано). Направление
    /// (N0,Mx0,My0) не нормируется (как и в CrackingMoment — бисекция инвариантна к масштабу
    /// входного вектора).
    /// </summary>
    public CrackingLoadFactorResult CrackingLoadFactor(double N0, double Mx0, double My0)
    {
        double epsLimit = TensionLimit();

        var (plane0, eps0, ok0) = EvaluateAtLoadFactor(0.0, N0, Mx0, My0, null);
        if (!ok0)
            return new CrackingLoadFactorResult { Converged = false };

        if (eps0 >= epsLimit)
            return new CrackingLoadFactorResult
            {
                Lambda = 0.0, N = 0.0, Mx = 0.0, My = 0.0,
                StrainPlane = plane0, Converged = true, EpsMaxTension = eps0
            };

        double a = 0.0, b = 1.0;
        bool foundUpper = false;
        Kurvature? seed = plane0;
        for (int expand = 0; expand < 40; expand++)
        {
            var (planeB, epsB, okB) = EvaluateAtLoadFactor(b, N0, Mx0, My0, seed);
            if (!okB || epsB >= epsLimit) { foundUpper = true; break; }
            seed = planeB;
            a = b;
            b *= 2.0;
        }
        if (!foundUpper)
            return new CrackingLoadFactorResult
            {
                Lambda = b, N = b * N0, Mx = b * Mx0, My = b * My0, Converged = false
            };

        Kurvature? bestPlane = plane0;
        double bestEps = eps0;
        int iter = 0;
        for (iter = 1; iter <= _bisectMaxIter; iter++)
        {
            double mid = 0.5 * (a + b);
            var (planeMid, epsMid, okMid) = EvaluateAtLoadFactor(mid, N0, Mx0, My0, bestPlane);
            if (!okMid) { b = mid; }
            else
            {
                bestPlane = planeMid;
                bestEps = epsMid;
                if (epsMid < epsLimit) a = mid; else b = mid;
            }
            if (Math.Abs(bestEps - epsLimit) <= _bisectTol) break;
        }

        double lambda = 0.5 * (a + b);
        bool converged = Math.Abs(bestEps - epsLimit) <= _bisectTol * 10.0
            || Math.Abs(b - a) <= _bisectTol * 10.0;

        return new CrackingLoadFactorResult
        {
            Lambda = lambda, N = lambda * N0, Mx = lambda * Mx0, My = lambda * My0,
            StrainPlane = bestPlane, Converged = converged, Iterations = iter, EpsMaxTension = bestEps
        };
    }

    (Kurvature plane, double epsMax, bool ok) EvaluateAtCurvature(double n, double ky, double kz)
    {
        CurvatureEquilibriumResult eq;
        try
        {
            eq = CurvatureEquilibrium8232.Solve(
                e0 => _section.Integral(new Kurvature { e0 = e0, ky = ky, kz = kz }, _calcCrc, ten: true, ca: true),
                targetN: n, tolerance: _solverTol, maxIterations: 100);
        }
        catch (InvalidOperationException)
        {
            return (default, 0.0, false);
        }
        if (!eq.Converged) return (default, 0.0, false);
        var plane = new Kurvature { e0 = eq.E0, ky = ky, kz = kz };
        return (plane, MaxTensionStrain(plane), true);
    }

    (Kurvature? plane, double epsMax, bool ok) EvaluateAtLoadFactor(
        double lambda, double N0, double Mx0, double My0, Kurvature? seed)
    {
        var solver = new StrainSolver(_section, _calcCrc, ten: true, ca: true,
            tol: _solverTol, maxIter: _solverMaxIter, h: _solverH);
        var plane = solver.Solve(lambda * N0, lambda * Mx0, lambda * My0, seed);
        if (!solver.Converged) return (null, 0.0, false);
        return (plane, MaxTensionStrain(plane), true);
    }

    static bool IsConcreteArea(MaterialArea area) =>
        area.Material?.Type == MatType.Concrete ||
        (area.Material?.Type == MatType.Custom && area.Material.BaseType == MatType.Concrete);
}
