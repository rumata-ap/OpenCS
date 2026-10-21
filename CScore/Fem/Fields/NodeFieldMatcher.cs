using System.Globalization;

namespace CScore.Fem.Fields;

/// <summary>Узел-цель поля.</summary>
public sealed record FieldNode(string Tag, FieldVec3 P);

/// <summary>
/// Сопоставление точек таблицы узлам один к одному (сваи ↔ узлы КЭ 56): точка ищет ближайший узел в пределах допуска
/// (без Z — в плане); если к узлу притянулось несколько точек — берётся ближайшая.
/// </summary>
public static class NodeFieldMatcher
{
    public static FieldEvaluation Match(IReadOnlyList<FieldPoint> points, bool hasZ, IReadOnlyList<FieldNode> nodes, double tolerance)
    {
        var tree = new KdTree3(nodes.Select(n => hasZ ? n.P : n.P with { Z = 0 }).ToList());
        var best = new Dictionary<int, (int Point, double D)>();
        var conflicts = new SortedSet<string>(StringComparer.Ordinal);
        var unmatched = new List<string>();
        for (int i = 0; i < points.Count; i++)
        {
            var q = hasZ ? points[i].P : points[i].P with { Z = 0 };
            var (idx, d) = tree.Nearest(q);
            if (idx < 0 || d > tolerance) { unmatched.Add(Describe(i, points[i].P)); continue; }
            if (best.TryGetValue(idx, out var cur))
            {
                conflicts.Add(nodes[idx].Tag);
                if (d < cur.D) best[idx] = (i, d);
            }
            else best[idx] = (i, d);
        }
        var targets = new List<FieldTargetValue>(nodes.Count);
        var orphans = new List<string>();
        for (int n = 0; n < nodes.Count; n++)
        {
            if (best.TryGetValue(n, out var b))
                targets.Add(new FieldTargetValue(nodes[n].Tag, (double[])points[b.Point].Values.Clone(), FieldHow.Matched, b.D));
            else
            {
                targets.Add(new FieldTargetValue(nodes[n].Tag, null, FieldHow.None, double.PositiveInfinity));
                orphans.Add(nodes[n].Tag);
            }
        }
        var diags = new List<FieldDiagnostic>();
        if (conflicts.Count > 0)
            diags.Add(new("field_node_conflict", "К узлу притянулось несколько точек таблицы — взята ближайшая; узлы", conflicts.ToList()));
        if (unmatched.Count > 0)
            diags.Add(new("field_point_unmatched",
                $"Точки таблицы без узла в пределах {tolerance.ToString("0.###", CultureInfo.InvariantCulture)} м; строки", unmatched));
        if (orphans.Count > 0)
            diags.Add(new("field_node_unmatched", "Узлы без точки таблицы; узлы", orphans));
        if (!hasZ)
        {
            // Без Z узлы на одной вертикали неразличимы: точка достанется одному из них произвольно.
            var stacked = nodes.GroupBy(n => ((long)Math.Round(n.P.X * 1000), (long)Math.Round(n.P.Y * 1000)))
                .Where(g => g.Count() > 1).SelectMany(g => g.Select(n => n.Tag)).OrderBy(t => t, StringComparer.Ordinal).ToList();
            if (stacked.Count > 0)
                diags.Add(new("field_nodes_stacked", "Таблица без Z: узлы-цели совпадают в плане — значение получит один из них; узлы", stacked));
        }
        return new FieldEvaluation(targets, diags, [], points.Count, unmatched.Count);
    }

    static string Describe(int i, FieldVec3 p) => string.Create(CultureInfo.InvariantCulture, $"{i + 1} ({p.X:0.###}; {p.Y:0.###})");
}
