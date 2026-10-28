using CScore.Planar;
using CScore.Submodel;

namespace CScore.Fem.Fragment.Masks;

/// <summary>
/// Раскладка маски на узлы сетки фрагмента (спека 5.2): перемещения — линейная интерполяция сэмплов по <c>s</c> внутри
/// отрезка; силы кромки — согласованные узловые силы (интеграл погонной силы с кусочно-линейными функциями узлов
/// отрезка). Функции узлов дают разбиение единицы и воспроизводят линейную функцию, поэтому ΣF и ΣM сил узлов равны
/// равнодействующей маски (<see cref="FragmentMaskBalance.Resultant"/>) на любой сетке.
/// </summary>
public static class FragmentMaskDistribution
{
    const double Eps = 1e-9;

    /// <summary>
    /// Сэмплы кромки по отрезкам (как их выдаёт экстрактор: подряд, концы отрезка — свои сэмплы; на стыке отрезков два
    /// сэмпла с одной <c>s</c> — по одному на отрезок).
    /// </summary>
    public static List<List<FragmentMaskSample>> SplitBySegments(IReadOnlyList<(PlanarVector3 P, PlanarVector3 Q)> segments,
        IReadOnlyList<FragmentMaskSample> samples)
    {
        var result = new List<List<FragmentMaskSample>>(segments.Count);
        double offset = 0;
        int k = 0;
        foreach (var (a, b) in segments)
        {
            double len = (b - a).Length;
            var segment = new List<FragmentMaskSample>();
            while (k < samples.Count && samples[k].S <= offset + len + Eps)
            {
                segment.Add(samples[k]);
                k++;
                if (segment.Count > 1 && samples[k - 1].S >= offset + len - Eps) break;
            }
            result.Add(segment);
            offset += len;
        }
        return result;
    }

    /// <summary>Перемещение в точке <paramref name="s"/> отрезка по его сэмплам (вне диапазона — крайний); null — нет данных.</summary>
    public static Dof6? DisplacementAt(IReadOnlyList<FragmentMaskSample> segment, double s)
    {
        if (segment.Count == 0 || segment.Any(x => x.U == null)) return null;
        return Interpolate(segment, s, x => x.U!);
    }

    /// <summary>
    /// Узловые силы кромки по узлам <paramref name="nodes"/> (силы — Н, моменты — Н·м), суммой по отрезкам; null — в
    /// маске нет сил или отрезков маски меньше, чем у узлов.
    /// </summary>
    public static Dictionary<string, Dof6>? EdgeNodalForces(FragmentCut cut, FragmentCutMeshNodes nodes,
        IReadOnlyList<FragmentMaskSample> samples)
    {
        if (samples.Count == 0 || samples.Any(x => x.F == null)) return null;
        var bySegment = SplitBySegments(cut.Segments, samples);
        var result = new Dictionary<string, Dof6>(StringComparer.Ordinal);
        int seg = 0;
        double offset = 0;
        for (int i = 0; i < cut.Segments.Count; i++)
        {
            var (a, b) = cut.Segments[i];
            double len = (b - a).Length;
            var part = bySegment[i];
            // Отрезки нулевой длины узлов не имеют (FragmentCutMeshNodes их пропускает).
            if (seg >= nodes.Segments.Count || len <= Eps) { offset += len; continue; }
            var segmentNodes = nodes.Segments[seg];
            if (Math.Abs(segmentNodes[0].S - offset) > 1e-6) { offset += len; continue; }
            seg++;
            offset += len;
            if (part.Count == 0) return null;
            AddSegment(result, segmentNodes, part);
        }
        return seg == nodes.Segments.Count ? result : null;
    }

    /// <summary>
    /// Согласованные силы одного отрезка: на каждом подынтервале (точки узлов и сэмплов) функция узла и погонная сила
    /// линейны, произведение — квадратичное, Симпсон точен.
    /// </summary>
    static void AddSegment(Dictionary<string, Dof6> result, IReadOnlyList<FragmentCutNode> nodes, IReadOnlyList<FragmentMaskSample> samples)
    {
        double s0 = nodes[0].S, s1 = nodes[^1].S;
        var points = nodes.Select(n => n.S).Concat(samples.Select(x => x.S)).Where(s => s >= s0 - Eps && s <= s1 + Eps)
            .Select(s => Math.Clamp(s, s0, s1)).OrderBy(s => s).ToList();
        var breaks = new List<double>();
        foreach (var s in points)
            if (breaks.Count == 0 || s - breaks[^1] > Eps) breaks.Add(s);

        for (int k = 0; k + 1 < breaks.Count; k++)
        {
            double a = breaks[k], b = breaks[k + 1], h = b - a, m = (a + b) / 2;
            // Узлы, функции которых не нулевые на [a, b]: соседи слева и справа.
            int j = 0;
            while (j + 1 < nodes.Count - 1 && nodes[j + 1].S <= a + Eps) j++;
            var qa = Interpolate(samples, a, x => x.F!);
            var qm = Interpolate(samples, m, x => x.F!);
            var qb = Interpolate(samples, b, x => x.F!);
            for (int i = j; i <= j + 1 && i < nodes.Count; i++)
            {
                double wa = Hat(nodes, i, a), wm = Hat(nodes, i, m), wb = Hat(nodes, i, b);
                var f = (qa.Scale(wa) + qm.Scale(4 * wm) + qb.Scale(wb)).Scale(h / 6);
                result[nodes[i].Tag] = result.TryGetValue(nodes[i].Tag, out var prev) ? prev + f : f;
            }
        }
    }

    /// <summary>Кусочно-линейная функция узла <paramref name="i"/> в точке <paramref name="s"/>.</summary>
    static double Hat(IReadOnlyList<FragmentCutNode> nodes, int i, double s)
    {
        double si = nodes[i].S;
        if (i > 0 && s < si)
        {
            double sl = nodes[i - 1].S;
            return s <= sl ? 0 : (s - sl) / (si - sl);
        }
        if (i + 1 < nodes.Count && s > si)
        {
            double sr = nodes[i + 1].S;
            return s >= sr ? 0 : (sr - s) / (sr - si);
        }
        return Math.Abs(s - si) <= Eps ? 1 : 0;
    }

    static Dof6 Interpolate(IReadOnlyList<FragmentMaskSample> samples, double s, Func<FragmentMaskSample, Dof6> value)
    {
        if (s <= samples[0].S) return value(samples[0]);
        for (int i = 0; i + 1 < samples.Count; i++)
        {
            double a = samples[i].S, b = samples[i + 1].S;
            if (s > b + Eps) continue;
            if (b - a <= Eps) return value(samples[i + 1]);
            double t = Math.Clamp((s - a) / (b - a), 0, 1);
            return value(samples[i]).Scale(1 - t) + value(samples[i + 1]).Scale(t);
        }
        return value(samples[^1]);
    }

    /// <summary>
    /// Линейная комбинация масок одного разреза (Σ kᵢ·маскаᵢ) по сэмплам; сэмплы масок должны совпадать по <c>s</c>
    /// (одна геометрия снятия), иначе null. U (F) суммы — null, если нет хотя бы у одного слагаемого.
    /// </summary>
    public static List<FragmentMaskSample>? Combine(IReadOnlyList<(IReadOnlyList<FragmentMaskSample> Samples, double Factor)> parts)
    {
        if (parts.Count == 0) return null;
        int n = parts[0].Samples.Count;
        if (parts.Any(p => p.Samples.Count != n)) return null;
        var result = new List<FragmentMaskSample>(n);
        for (int i = 0; i < n; i++)
        {
            double s = parts[0].Samples[i].S;
            if (parts.Any(p => Math.Abs(p.Samples[i].S - s) > 1e-6)) return null;
            Dof6? u = Dof6.Zero, f = Dof6.Zero;
            foreach (var (samples, k) in parts)
            {
                var x = samples[i];
                u = u != null && x.U != null ? u + x.U.Scale(k) : null;
                f = f != null && x.F != null ? f + x.F.Scale(k) : null;
            }
            result.Add(new FragmentMaskSample(s, u, f));
        }
        return result;
    }
}
