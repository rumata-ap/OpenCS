using System.Globalization;
using CScore.Import;
using CScore.Planar;

namespace CScore.Fem.Fragment;

public static partial class StoreyFragmentBuilder
{
    /// <summary>Имя набора КЭ полосы без КонЭ, собранного построителем (части — «КЭ без КонЭ · k»).</summary>
    public const string UncoveredTag = "КЭ без КонЭ";

    /// <summary>
    /// КЭ сетки родителя, заходящие во фрагмент: ни одна плоскость полосы и призмы не оставляет все узлы КЭ снаружи (или
    /// на ней). У рёбер призмы отбор консервативен — лишние КЭ у углов дальше отсекает обрезка КонЭ.
    /// </summary>
    static List<FemElement> BandElements(Ctx c, StoreyFragmentParentSnapshot parent)
    {
        var node = parent.MeshNodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => P(g.First()), StringComparer.Ordinal);
        var result = new List<FemElement>();
        foreach (var e in parent.MeshElements)
        {
            var tags = FemMeshTopology.ReadNodeTags(e)?.ToList();
            if (tags is null || tags.Count == 0 || tags.Any(t => !node.ContainsKey(t))) continue;
            if (c.Planes.All(pl => tags.Min(t => pl.Eval(node[t])) < -c.Tol)) result.Add(e);
        }
        return result;
    }

    /// <summary>Медиана длины рёбер КЭ пластин родителя в полосе, округлённая до 1 см (не меньше 1 см); null — пластин нет.</summary>
    static double? ParentMeshStep(Ctx c, StoreyFragmentParentSnapshot parent)
    {
        var node = parent.MeshNodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var edges = new List<double>();
        foreach (var e in BandElements(c, parent).Where(e => e.ElemType == "shell"))
        {
            var tags = FemMeshTopology.ReadNodeTags(e)?.ToArray();
            if (tags is not { Length: 3 or 4 } || tags.Any(t => !node.ContainsKey(t))) continue;
            // Q4 хранится «1 2 4 3» — обход контура n1→n2→n4→n3.
            var loop = tags.Length == 4 ? new[] { tags[0], tags[1], tags[3], tags[2] } : tags;
            for (int i = 0; i < loop.Length; i++)
            {
                double length = (P(node[loop[(i + 1) % loop.Length]]) - P(node[loop[i]])).Length;
                if (length > c.Tol) edges.Add(length);
            }
        }
        if (edges.Count == 0) return null;
        edges.Sort();
        return Math.Max(0.01, Math.Round(edges[edges.Count / 2], 2));
    }

    /// <summary>КЭ полосы, не покрытые КонЭ родителя, — в КонЭ через <see cref="MeshMemberBuilder"/> (спека, решение 1).</summary>
    static List<SourceMember> Uncovered(Ctx c, StoreyFragmentParentSnapshot parent)
    {
        var memberTags = parent.Members.Select(m => m.ElemTag).ToHashSet(StringComparer.Ordinal);
        var uncovered = BandElements(c, parent)
            .Where(e => e.ElemType is "beam" or "shell")
            .Where(e => e.SourceMemberTag is null || !memberTags.Contains(e.SourceMemberTag))
            .ToList();
        var tags = uncovered.Select(e => e.ElemTag).ToList();
        var result = new List<SourceMember>();
        if (tags.Count == 0) return result;
        // КЭ ссылается на КонЭ, которого в снимке нет: у КонЭ нет узлов схемы (битые ссылки NodeIdsJson) — его сечение и
        // параметры теряются, а КЭ собираются как КЭ без КонЭ.
        var lost = uncovered.Select(e => e.SourceMemberTag).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (lost.Count > 0)
            c.Warn("fragment_member_unresolved", $"КЭ полосы ссылаются на КонЭ родителя без узлов схемы: {Head(lost)} — сечения " +
                $"и параметры этих КонЭ не переносятся, КЭ собраны в КонЭ «{UncoveredTag}». Проверьте узлы КонЭ в родителе.", [.. lost]);

        var build = MeshMemberBuilder.Build(new MeshMemberRequest(UncoveredTag, null, tags, "auto"), parent.MeshNodes, parent.MeshElements);
        foreach (var d in build.Diagnostics) c.Warn(d.Code, d.Message, [.. d.SourceKeys ?? []]);
        var ends = build.Nodes.ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
        var elementByTag = uncovered.ToDictionary(e => e.ElemTag, StringComparer.Ordinal);
        var meshNode = parent.MeshNodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var part in build.Parts)
        {
            var m = part.Member;
            if (m.ElemType == "beam")
            {
                // Сечение — у КЭ цепочки (импорт SCAD/ЛИРЫ): части фрагмента берут его по своему участку (CutBar).
                var bars = new List<(PlanarVector3 A, PlanarVector3 B, int? SectionId)>();
                foreach (var e in part.ElementTags.Select(t => elementByTag.GetValueOrDefault(t)).OfType<FemElement>())
                    if (FemMeshTopology.ReadNodeTags(e)?.ToArray() is { Length: 2 } et &&
                        meshNode.TryGetValue(et[0], out var ea) && meshNode.TryGetValue(et[1], out var eb))
                        bars.Add((P(ea), P(eb), e.CrossSectionId));
                c.ParentBars[m.ElemTag] = bars;
                var ids = NodeTags(m);
                result.Add(new(m, null, P(ends[ids[0].ToString(CultureInfo.InvariantCulture)]),
                    P(ends[ids[^1].ToString(CultureInfo.InvariantCulture)]), 0, 0));
            }
            else if (part.Region is not null)
                result.Add(new(m, part.Region, default, default, 0, 0));
        }
        c.Warn("fragment_uncovered_members",
            $"КЭ полосы без КонЭ ({tags.Count}) собраны в КонЭ «{UncoveredTag}».", [.. tags]);
        return result;
    }

    /// <summary>Что из КЭ полосы не переносится: шарниры концов (у КонЭ нет поля), КЭ без аналога на уровне КонЭ.</summary>
    static void ReportNotTransferred(Ctx c, StoreyFragmentParentSnapshot parent)
    {
        var band = BandElements(c, parent);
        var released = band.Where(e => e.ReleaseI is > 0 || e.ReleaseJ is > 0).Select(e => e.ElemTag).ToList();
        if (released.Count > 0)
            c.Warn("fragment_releases_lost", $"Шарниры концов КЭ не переносятся (у КонЭ нет такого поля): {Head(released)}.", [.. released]);
        var other = band.Where(e => e.ElemType is not ("beam" or "shell")).Select(e => e.ElemTag).ToList();
        if (other.Count > 0)
            c.Warn("fragment_element_type_lost", $"КЭ без аналога на уровне КонЭ не переносятся: {Head(other)}.", [.. other]);
    }

    static string Head(List<string> tags) =>
        tags.Count <= 10 ? string.Join(", ", tags) : $"{string.Join(", ", tags.Take(10))} … ещё {tags.Count - 10}";
}
