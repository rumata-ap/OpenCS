using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentFixtures;

namespace CScore.Tests.Fem.Fragment;

/// <summary>Построитель с призмой: общие помощники тестов срезов призмы.</summary>
static class StoreyFragmentPrismFixtures
{
    /// <summary>Сечение 0,6 × 0,4 (Y × Z осей стержня) у Id 1.</summary>
    public static readonly Func<int, IReadOnlyList<(double Y, double Z)>?> Outline =
        id => id == 1 ? [(-0.3, -0.2), (0.3, -0.2), (0.3, 0.2), (-0.3, 0.2)] : null;

    /// <summary>Квадратная призма 2a × 2a с центром в начале координат.</summary>
    public static FragmentPrism Square(double a) => FragmentPrism.Create(0, 0, 1, 0, a, a);

    public static StoreyFragmentDraft BuildPrism(
        FragmentPrism prism, FemNode[] nodes, (FemMember Member, PlanarRegion? Region)[] members,
        FemMeshNode[]? meshNodes = null, FemElement[]? meshElements = null) =>
        StoreyFragmentBuilder.Build(new StoreyFragmentParentSnapshot
        {
            Nodes = nodes,
            Members = members.Select(m => m.Member).ToList(),
            Regions = members.Where(m => m.Region is not null).Select(m => m.Region!).ToList(),
            MeshNodes = meshNodes ?? [],
            MeshElements = meshElements ?? [CoveredElement("S1")],
            SectionOutline = Outline,
        }, new StoreyFragmentRequest(1.5, 4.5, "Фрагмент", Prism: prism));
}

public sealed class StoreyFragmentPrismBarTests
{
    static FemNode NodeOf(StoreyFragmentDraft d, string? tag) => d.Nodes.Single(n => n.NodeTag == tag);

    [Fact]
    public void BeamThroughFaces_CutAtFaces_LateralEndCuts()
    {
        var d = StoreyFragmentPrismFixtures.BuildPrism(StoreyFragmentPrismFixtures.Square(1),
            [N("1", -2.5, 0, 3), N("2", 2.5, 0, 3)], [(Bar("B1", "1", "2"), null), Slab("S1", 1, 3)]);

        Assert.False(d.HasErrors);
        var beam = Assert.Single(d.Members, m => m.Member.ElemType == "beam");
        Assert.Equal("B1", beam.Member.ElemTag);
        var cuts = d.Cuts.Where(c => c.MemberTag == "B1").OrderBy(c => NodeOf(d, c.NodeTag).X).ToList();
        Assert.Equal(2, cuts.Count);
        Assert.All(cuts, c => Assert.Equal((FragmentCutKind.End, FragmentCutSide.Lateral), (c.Kind, c.Side)));
        Assert.Equal(-1, NodeOf(d, cuts[0].NodeTag).X, 9);
        Assert.Equal(1, NodeOf(d, cuts[1].NodeTag).X, 9);
        Assert.Equal(new PlanarVector3(-1, 0, 0), cuts[0].Outward);
        Assert.Equal(new PlanarVector3(1, 0, 0), cuts[1].Outward);
    }

    [Fact]
    public void ColumnInPrism_HorizontalEndCuts()
    {
        var d = StoreyFragmentPrismFixtures.BuildPrism(StoreyFragmentPrismFixtures.Square(1),
            [N("1", 0, 0, 0), N("2", 0, 0, 6)], [(Bar("C1", "1", "2"), null), Slab("S1", 1, 3)]);

        Assert.Equal(new[] { "C1 · низ", "C1 · верх" }, d.Members.Where(m => m.Member.ElemType == "beam").Select(m => m.Member.ElemTag));
        var cuts = d.Cuts.Where(c => c.Kind == FragmentCutKind.End).OrderBy(c => c.Z).ToList();
        Assert.Equal((FragmentCutSide.Bottom, FragmentCutSide.Top), (cuts[0].Side, cuts[1].Side));
        Assert.Null(cuts[0].Normal);
        Assert.Equal(new PlanarVector3(0, 0, -1), cuts[0].Outward);
    }

    [Fact]
    public void NeighbourColumn_AxisOutside_SectionTouches_WarnedNotIncluded()
    {
        // Сечение 0,6 вдоль X: колонна в x = 1,1 задевает грань x = 1, колонна в x = 1,5 — нет.
        var d = StoreyFragmentPrismFixtures.BuildPrism(StoreyFragmentPrismFixtures.Square(1),
            [N("1", 1.1, 0, 0), N("2", 1.1, 0, 6), N("3", 1.5, 0.5, 0), N("4", 1.5, 0.5, 6)],
            [(Bar("C2", "1", "2"), null), (Bar("C3", "3", "4"), null), Slab("S1", 1, 3)]);

        Assert.DoesNotContain(d.Members, m => m.Member.ElemType == "beam");
        var w = Assert.Single(d.Diagnostics, x => x.Code == "fragment_column_outside_prism");
        Assert.False(w.IsError);
        Assert.Contains("C2", w.SourceKeys!);
    }

    [Fact]
    public void BeamInFacePlane_Error_OutsideFace_NoError()
    {
        var on = StoreyFragmentPrismFixtures.BuildPrism(StoreyFragmentPrismFixtures.Square(1),
            [N("1", -2, 1, 3), N("2", 2, 1, 3)], [(Bar("B1", "1", "2"), null), Slab("S1", 1, 3)]);
        Assert.Contains(on.Diagnostics, x => x.IsError && x.Code == "fragment_member_on_prism_face");

        var away = StoreyFragmentPrismFixtures.BuildPrism(StoreyFragmentPrismFixtures.Square(1),
            [N("1", 5, 1, 3), N("2", 8, 1, 3)], [(Bar("B1", "1", "2"), null), Slab("S1", 1, 3)]);
        Assert.False(away.HasErrors);
        Assert.DoesNotContain(away.Members, m => m.Member.ElemType == "beam");
    }
}
