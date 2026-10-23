using System.Globalization;
using CScore.Import;

namespace CScore.Fem.Fragment;

public static partial class StoreyFragmentBuilder
{
    /// <summary>Имя набора КЭ полосы без КонЭ, собранного построителем (части — «КЭ без КонЭ · k»).</summary>
    public const string UncoveredTag = "КЭ без КонЭ";

    /// <summary>КЭ сетки родителя, заходящие в полосу (пересекающие её или внутри).</summary>
    static List<FemElement> BandElements(Ctx c, StoreyFragmentParentSnapshot parent)
    {
        var z = parent.MeshNodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First().Z, StringComparer.Ordinal);
        var result = new List<FemElement>();
        foreach (var e in parent.MeshElements)
        {
            var tags = FemMeshTopology.ReadNodeTags(e)?.ToList();
            if (tags is null || tags.Count == 0 || tags.Any(t => !z.ContainsKey(t))) continue;
            double zmin = tags.Min(t => z[t]), zmax = tags.Max(t => z[t]);
            if (zmax > c.Z1 + c.Tol && zmin < c.Z2 - c.Tol) result.Add(e);
        }
        return result;
    }

    /// <summary>КЭ полосы, не покрытые КонЭ родителя, — в КонЭ через <see cref="MeshMemberBuilder"/> (спека, решение 1).</summary>
    static List<SourceMember> Uncovered(Ctx c, StoreyFragmentParentSnapshot parent)
    {
        var memberTags = parent.Members.Select(m => m.ElemTag).ToHashSet(StringComparer.Ordinal);
        var tags = BandElements(c, parent)
            .Where(e => e.ElemType is "beam" or "shell")
            .Where(e => e.SourceMemberTag is null || !memberTags.Contains(e.SourceMemberTag))
            .Select(e => e.ElemTag).ToList();
        var result = new List<SourceMember>();
        if (tags.Count == 0) return result;

        var build = MeshMemberBuilder.Build(new MeshMemberRequest(UncoveredTag, null, tags, "auto"), parent.MeshNodes, parent.MeshElements);
        foreach (var d in build.Diagnostics) c.Warn(d.Code, d.Message, [.. d.SourceKeys ?? []]);
        var ends = build.Nodes.ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
        foreach (var part in build.Parts)
        {
            var m = part.Member;
            if (m.ElemType == "beam")
            {
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
