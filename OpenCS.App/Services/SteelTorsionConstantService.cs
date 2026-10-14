using System.Collections.Concurrent;
using System.Globalization;
using CScore;
using CScore.ParametricSteel;
using CScore.Sp16;
using CSfea.Torsion;
using CSTriangulation;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Момент инерции при свободном кручении It по МКЭ для проверок СП 16: задача Сен-Венана (функция
/// Прандтля, элементы T6) на трёх сетках h, h/2, h/4 с экстраполяцией Ричардсона; h — наименьшая толщина
/// элементов профиля. Для параметрического сечения контур строится по исходному описанию с мелкими дугами
/// скруглений (погрешность аппроксимации дуг ~0,02 % против ~0,3 % у контура сечения). Результат кэшируется
/// по контуру: в задаче с МКЭ-схемой It считается один раз на сечение.
/// </summary>
public static class SteelTorsionConstantService
{
    /// <summary>Сегментов дуги скругления на четверть окружности для контура параметрического сечения.</summary>
    public const int ArcSegmentsPerQuarter = 32;

    const int MaxCacheEntries = 256;
    static readonly ConcurrentDictionary<string, Lazy<TorsionConstantFem>> Cache = new();
    // Расчёты выполняются по одному: триангуляция Ruppert (Triangle.NET) не гарантирует потокобезопасность.
    static readonly object SolveLock = new();

    /// <summary>
    /// It по МКЭ для области сечения (м⁴). <paramref name="contourKey"/> — ключ геометрии области;
    /// <paramref name="profile"/> — профиль (толщины для шага сетки; null — распознаётся по контуру).
    /// </summary>
    public static TorsionConstantFem Get(CrossSection section, MaterialArea area, string contourKey, SteelProfile? profile)
    {
        var definition = section.TryGetParametricSteelDefinition();
        string key = (definition != null ? "def|" : "area|") + contourKey;
        if (Cache.Count > MaxCacheEntries) Cache.Clear();
        return Cache.GetOrAdd(key, _ => new Lazy<TorsionConstantFem>(() => Compute(area, definition, profile))).Value;
    }

    static TorsionConstantFem Compute(MaterialArea area, ParametricSteelSectionDefinition? definition, SteelProfile? profile)
    {
        TorsionBoundary boundary;
        try
        {
            boundary = (definition != null ? FromDefinition(definition) : null) ?? area.FromMaterialArea();
        }
        catch (Exception ex) { return new(0, ex.Message); }
        profile ??= SteelProfileRecognizer.Recognize(new PolygonSection(
            area.Hull!.Points.Select(pt => (pt.X, pt.Y)), area.Holes.Select(h => h.Points.Select(pt => (pt.X, pt.Y)))));
        double h0 = ElementSize(boundary, profile);
        lock (SolveLock) return Solve(boundary, h0);
    }

    /// <summary>
    /// It по МКЭ для заданного контура: Ruppert, при сбое триангуляции — AdvancingFront.
    /// <paramref name="h0"/> — размер элемента самой грубой сетки (в единицах контура).
    /// </summary>
    public static TorsionConstantFem Solve(TorsionBoundary boundary, double h0)
    {
        string error = "";
        foreach (var triangulation in new[] { TriangulationMethod.Ruppert, TriangulationMethod.AdvancingFront })
        {
            try
            {
                var r = TorsionRichardson.SolveAutoConverge(boundary, TorsionMethod.Fem, triangulation,
                    FemElementOrder.Quadratic, h0, nRuns: 3, parallel: false);
                if (r.It > 0 && double.IsFinite(r.It)) return new(r.It, Details(r));
                error = Loc.S("Sp16ItFemNonPositive");
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { error = ex.Message; }
        }
        return new(0, string.Format(Loc.S("Sp16ItFemFailed"), error));
    }

    static string Details(TorsionAutoConvergeResult r)
    {
        string sizes = string.Join("; ", r.Steps.Select(s => (s.ElementSize * 1000).ToString("0.###", CultureInfo.CurrentCulture)));
        string extrapolation = r.ItExtrapolated && r.ItOrder is { } p
            ? string.Format(Loc.S("Sp16ItFemExtrapolated"), p.ToString("0.0", CultureInfo.CurrentCulture))
            : Loc.S("Sp16ItFemNotExtrapolated");
        return string.Format(Loc.S("Sp16ItFemDetails"), sizes, extrapolation);
    }

    /// <summary>Шаг грубой сетки: наименьшая толщина элементов профиля; для произвольного контура — √A/30.</summary>
    static double ElementSize(TorsionBoundary boundary, SteelProfile profile)
    {
        double t = new[] { profile.Tw, profile.Tf1, profile.Tf2 }.Where(v => v > 0 && double.IsFinite(v))
            .DefaultIfEmpty(0).Min();
        if (t > 0) return t;
        double area = Math.Abs(SignedArea(boundary.OuterX, boundary.OuterY));
        if (boundary.Holes != null)
            foreach (var (hx, hy) in boundary.Holes) area -= Math.Abs(SignedArea(hx, hy));
        return Math.Max(TorsionBoundaryMetrics.MinEdgeLength(boundary), Math.Sqrt(Math.Max(area, 0)) / 30);
    }

    static TorsionBoundary? FromDefinition(ParametricSteelSectionDefinition definition)
    {
        if (ParametricSteelSectionGenerator.BuildCanonicalContour(definition, ArcSegmentsPerQuarter) is not { } c)
            return null;
        var (ox, oy) = Oriented(c.Outer, ccw: true);
        var holes = c.Holes.Select(h => Oriented(h, ccw: false)).ToList();
        return new TorsionBoundary(ox, oy, holes.Count > 0 ? holes : null);
    }

    static (double[] X, double[] Y) Oriented(IReadOnlyList<(double X, double Y)> pts, bool ccw)
    {
        var x = pts.Select(p => p.X).ToArray();
        var y = pts.Select(p => p.Y).ToArray();
        if (SignedArea(x, y) > 0 != ccw) { Array.Reverse(x); Array.Reverse(y); }
        return (x, y);
    }

    static double SignedArea(double[] x, double[] y)
    {
        double s = 0;
        for (int i = 0, n = x.Length; i < n; i++)
        {
            int j = (i + 1) % n;
            s += x[i] * y[j] - x[j] * y[i];
        }
        return 0.5 * s;
    }
}
