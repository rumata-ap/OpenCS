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

/// <summary>
/// Момент трещинообразования поперечного сечения (СП 63.13330): бисекция по масштабу
/// момента при фиксированной нормальной силе и направлении (Mx:My = const) до достижения
/// максимальной растягивающей деформацией бетона предельного значения (из ветви растяжения
/// диаграммы бетона <paramref name="calcCrc"/>, обычно <see cref="CalcType.CL"/>).
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
        CalcType calcCrc = CalcType.CL,
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
            if (area.Material?.Type != MatType.Concrete) continue;
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
            if (area.Material?.Type != MatType.Concrete) continue;
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
        bool converged = Math.Abs(bestEps - epsLimit) <= _bisectTol * 10.0;

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
}
