using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CScore.Fem.Loads;

namespace CScore.Fem.Fields;

/// <summary>Сетка схемы для вычисления полей.</summary>
public sealed record FemFieldMeshContext(
    IReadOnlyList<FemMeshNode> Nodes, IReadOnlyList<FemElement> Elements,
    IReadOnlyList<FemMemberGroup> Groups, IReadOnlyList<FemMember> Members, IReadOnlyList<FemSpring> Springs);

/// <summary>Цели поля на текущей сетке и теги целей, которых в ней нет.</summary>
public sealed record FemFieldTargetSet(IReadOnlyList<FieldShell> Shells, IReadOnlyList<FieldNode> Nodes, IReadOnlyList<string> Missing);

/// <summary>Результат вычисления поля: перенос, значения в СИ, подпись целей для кэша.</summary>
public sealed record FemFieldComputed(FieldEvaluation Evaluation, IReadOnlyList<FemFieldValue> Values, string Signature);

/// <summary>Вычисление поля на сетке схемы: цели, совмещение, слияние дублей, перенос, пересчёт в СИ.</summary>
public static class FemFieldEvaluator
{
    public static FemFieldTargetSet ResolveTargets(FemField field, FemFieldMeshContext ctx)
    {
        var quantity = FemFieldQuantities.Get(field.Quantity);
        var t = field.Definition.Targets;
        var nodeByTag = ctx.Nodes.GroupBy(n => n.NodeTag, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var elementTags = ctx.Elements.Select(e => e.ElemTag).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();

        IEnumerable<FemElement> chosen = ctx.Elements;
        if (!t.IsEmpty)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tag in t.ElemTags)
                if (elementTags.Contains(tag)) set.Add(tag); else missing.Add(tag);
            var members = t.MemberTags.ToHashSet(StringComparer.Ordinal);
            foreach (var e in ctx.Elements)
                if (e.SourceMemberTag != null && members.Contains(e.SourceMemberTag)) set.Add(e.ElemTag);
            foreach (int gid in t.GroupIds)
            {
                var g = ctx.Groups.FirstOrDefault(x => x.Id == gid);
                if (g == null) { missing.Add($"группа {gid}"); continue; }
                foreach (var se in FemCheckScope.ForGroup(g, ctx.Members, ctx.Elements).Elements) set.Add(se.Element.ElemTag);
            }
            chosen = ctx.Elements.Where(e => set.Contains(e.ElemTag));
        }

        if (quantity.Target == FemFieldTargetKind.Shell)
        {
            var shells = new List<FieldShell>();
            foreach (var e in chosen.Where(e => e.ElemType == "shell"))
            {
                // Четырёхугольник хранится «1 2 4 3» (как в ЛИРЕ/SCAD) — пластина цели получает узлы по контуру.
                var g = FemElementGeometry.Of(e, nodeByTag);
                if (g == null || !g.IsShell) { missing.Add(e.ElemTag); continue; }
                shells.Add(new FieldShell(e.ElemTag, g.Contour.Select(i => g.NodeTags[i]).ToList(),
                    g.Contour.Select(i => new FieldVec3(g.Points[i].X, g.Points[i].Y, g.Points[i].Z)).ToList()));
            }
            return new FemFieldTargetSet(shells, [], missing);
        }

        var derived = new HashSet<string>(StringComparer.Ordinal);
        if (t.IsEmpty) foreach (var n in ctx.Nodes) derived.Add(n.NodeTag);
        else foreach (var e in chosen) foreach (var x in NodeTags(e)) derived.Add(x);
        if (t.OnlyNodesWithSprings)
            derived.IntersectWith(ctx.Springs.Where(s => s.TargetKind == FemSpringTargetKinds.MeshNode).Select(s => s.NodeTag));
        foreach (var tag in t.NodeTags)
            if (nodeByTag.ContainsKey(tag)) derived.Add(tag); else missing.Add(tag);
        var nodes = derived.Where(nodeByTag.ContainsKey).OrderBy(x => x, StringComparer.Ordinal)
            .Select(x => new FieldNode(x, P(nodeByTag[x]))).ToList();
        return new FemFieldTargetSet([], nodes, missing);
    }

    public static string Signature(FemField field, FemFieldMeshContext ctx) => Signature(ResolveTargets(field, ctx));

    public static FemFieldComputed Compute(FemField field, FemFieldMeshContext ctx)
    {
        var quantity = FemFieldQuantities.Get(field.Quantity);
        var set = ResolveTargets(field, ctx);
        var def = field.Definition;
        var aligned = field.Points.Select(p => new FieldPoint(def.Alignment.Apply(p.P), p.Values)).ToList();
        var (points, merged) = FieldPoints.MergeDuplicates(aligned);
        var eval = quantity.Target == FemFieldTargetKind.Shell
            ? ShellFieldInterpolator.Evaluate(points, field.HasZ, set.Shells, def.Settings)
            : NodeFieldMatcher.Match(points, field.HasZ, set.Nodes, def.Settings.MatchTolerance);

        var diags = new List<FieldDiagnostic>(eval.Diagnostics);
        if (set.Missing.Count > 0) diags.Add(new("field_target_missing", "Цели поля, которых нет в сетке", set.Missing));
        if (merged > 0) diags.Add(new("field_duplicates", $"Совпадающих точек таблицы слито: {merged}", []));

        var values = new List<FemFieldValue>(eval.Targets.Count * def.Columns.Count);
        foreach (var target in eval.Targets)
            for (int k = 0; k < def.Columns.Count; k++)
            {
                var col = def.Columns[k];
                double? v = target.Values is { } vals && k < vals.Length ? vals[k] * col.UnitFactor : null;
                values.Add(new FemFieldValue(field.Id, quantity.Target, target.Tag, col.Component, v, target.How, target.Distance));
            }
        return new FemFieldComputed(eval with { Diagnostics = diags }, values, Signature(set));
    }

    static string Signature(FemFieldTargetSet set)
    {
        var sb = new StringBuilder();
        foreach (var s in set.Shells)
        {
            sb.Append('s').Append(s.Tag);
            foreach (var p in s.Nodes) Append(sb, p);
            sb.Append('\n');
        }
        foreach (var n in set.Nodes) { sb.Append('n').Append(n.Tag); Append(sb, n.P); sb.Append('\n'); }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    static void Append(StringBuilder sb, FieldVec3 p) =>
        sb.Append(';').Append(R(p.X)).Append(',').Append(R(p.Y)).Append(',').Append(R(p.Z));

    // + 0.0 сводит −0 к 0: иначе «−0» и «0» дали бы разные подписи одной сетки.
    static string R(double v) => (Math.Round(v, 6) + 0.0).ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Теги узлов КЭ: в <see cref="FemElement.NodeIdsJson"/> — номера-теги, не Id.</summary>
    static string[] NodeTags(FemElement e) =>
        (JsonSerializer.Deserialize<int[]>(e.NodeIdsJson) ?? []).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();

    static FieldVec3 P(FemMeshNode n) => new(n.X, n.Y, n.Z);
}
