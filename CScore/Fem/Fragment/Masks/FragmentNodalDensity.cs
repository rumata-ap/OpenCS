using CScore.Planar;
using CScore.Submodel;

namespace CScore.Fem.Fragment.Masks;

/// <summary>Слагаемое силы кромки: сила <see cref="F"/> и момент <see cref="M"/> относительно <see cref="At"/>, общие оси.</summary>
public sealed record FragmentNodalTerm(PlanarVector3 At, PlanarVector3 F, PlanarVector3 M);

/// <summary>Слагаемое на отрезке кромки: <see cref="T"/> от начала отрезка, м; момент — относительно точки <see cref="T"/>.</summary>
public readonly record struct FragmentLineTerm(double T, PlanarVector3 F, PlanarVector3 M);

/// <summary>
/// Перенос сосредоточенных узловых сил на линию кромки и кусочно-линейная погонная плотность с точными ΣF и ΣM
/// (спека 2026-11-05, разд. 4): шляпа узла i высотой q_i = 2F_i/(t_{i+1} − t_{i−1}) даёт силу F_i, остаток первого
/// момента ΔM_i = (t_i − (t_{i−1} + t_i + t_{i+1})/3)·axis × F_i добавляется в погонный момент того же узла.
/// </summary>
public static class FragmentNodalDensity
{
    /// <summary>Точки ближе этого (м) сливаются; слагаемое ближе к концу отрезка — на конец.</summary>
    public const double MergeEps = 1e-6;

    /// <summary>
    /// Слагаемые — в ближайшие точки отрезков (с ограничением на отрезок; отрезки короче <see cref="MergeEps"/>
    /// пропускаются): M′ = M + (p − p′) × F. Итог по отрезкам — по возрастанию T, близкие точки слиты (сила — суммой,
    /// моменты — к общей точке).
    /// </summary>
    public static List<List<FragmentLineTerm>> Project(IReadOnlyList<(PlanarVector3 P, PlanarVector3 Q)> segments,
        IEnumerable<FragmentNodalTerm> terms)
    {
        var result = segments.Select(_ => new List<FragmentLineTerm>()).ToList();
        foreach (var term in terms)
        {
            int best = -1;
            double bestD = double.MaxValue, bestT = 0;
            for (int j = 0; j < segments.Count; j++)
            {
                var (a, b) = segments[j];
                double len = (b - a).Length;
                if (len < MergeEps) continue;
                var axis = (b - a) * (1 / len);
                double t = Math.Clamp((term.At - a).Dot(axis), 0, len);
                if (t < MergeEps) t = 0;
                else if (t > len - MergeEps) t = len;
                double d = (term.At - (a + axis * t)).Length;
                if (d < bestD - 1e-12) { best = j; bestD = d; bestT = t; }
            }
            if (best < 0) continue;
            var (pa, pb) = segments[best];
            var at = pa + (pb - pa).Normalize() * bestT;
            result[best].Add(new FragmentLineTerm(bestT, term.F, term.M + (term.At - at).Cross(term.F)));
        }
        for (int j = 0; j < segments.Count; j++)
        {
            var (a, b) = segments[j];
            if ((b - a).Length >= MergeEps) result[j] = Merge(result[j], (b - a).Normalize());
        }
        return result;
    }

    static List<FragmentLineTerm> Merge(List<FragmentLineTerm> terms, PlanarVector3 axis)
    {
        terms.Sort((x, y) => x.T.CompareTo(y.T));
        var merged = new List<FragmentLineTerm>(terms.Count);
        foreach (var t in terms)
        {
            if (merged.Count > 0 && t.T - merged[^1].T <= MergeEps)
            {
                var m = merged[^1];
                merged[^1] = new FragmentLineTerm(m.T, m.F + t.F, m.M + t.M + (axis * (t.T - m.T)).Cross(t.F));
            }
            else merged.Add(t);
        }
        return merged;
    }

    /// <summary>
    /// Узлы кусочно-линейной плотности отрезка длины <paramref name="length"/> (ось <paramref name="axis"/>) по слитым
    /// слагаемым (<see cref="Project"/>): концы отрезка — всегда узлы (без слагаемого — ноль); слагаемое на конце —
    /// полушляпа. Сила — Н/м, момент — Н·м/м (относительно точки на линии).
    /// </summary>
    public static List<(double T, Dof6 F)> Density(double length, PlanarVector3 axis, IReadOnlyList<FragmentLineTerm> terms)
    {
        var t = new List<double>(terms.Count + 2);
        var f = new List<PlanarVector3>(terms.Count + 2);
        var m = new List<PlanarVector3>(terms.Count + 2);
        if (terms.Count == 0 || terms[0].T > 0) { t.Add(0); f.Add(PlanarVector3.Zero); m.Add(PlanarVector3.Zero); }
        foreach (var x in terms) { t.Add(x.T); f.Add(x.F); m.Add(x.M); }
        if (t[^1] < length) { t.Add(length); f.Add(PlanarVector3.Zero); m.Add(PlanarVector3.Zero); }
        var result = new List<(double, Dof6)>(t.Count);
        for (int i = 0; i < t.Count; i++)
        {
            double prev = i > 0 ? t[i - 1] : t[i], next = i + 1 < t.Count ? t[i + 1] : t[i], w = next - prev;
            if (w <= 0) { result.Add((t[i], Dof6.Zero)); continue; }
            double c = (prev + t[i] + next) / 3;
            var dm = (axis * (t[i] - c)).Cross(f[i]);
            result.Add((t[i], Dof6.FromParts(f[i] * (2 / w), (m[i] + dm) * (2 / w))));
        }
        return result;
    }

    /// <summary>Значение плотности в <paramref name="s"/> (линейно между узлами, вне диапазона — крайний узел).</summary>
    public static Dof6 At(IReadOnlyList<(double T, Dof6 F)> density, double s)
    {
        if (density.Count == 0) return Dof6.Zero;
        if (s <= density[0].T) return density[0].F;
        for (int i = 1; i < density.Count; i++)
            if (s <= density[i].T)
            {
                double h = density[i].T - density[i - 1].T, a = h > 0 ? (s - density[i - 1].T) / h : 1;
                return density[i - 1].F.Scale(1 - a) + density[i].F.Scale(a);
            }
        return density[^1].F;
    }
}
