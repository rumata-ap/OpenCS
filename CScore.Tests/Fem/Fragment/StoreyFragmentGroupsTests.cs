using CScore.Fem;
using CScore.Fem.Fragment;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentFixtures;

namespace CScore.Tests.Fem.Fragment;

/// <summary>
/// Группы родителя с сечением пластины → группы КонЭ фрагмента: плита 6 × 6 м на z = 3 сеткой 4 × 4 КЭ (теги 1…16) без КонЭ
/// (как импорт) или КонЭ «S1»; полоса 1,5…4,5.
/// </summary>
public sealed class StoreyFragmentGroupsTests
{
    static StoreyFragmentDraft BuildUncovered(params FemMemberGroup[] groups)
    {
        var (nodes, elements) = StoreyFragmentLoadFixtures.SlabMesh("—", 3, 4);
        foreach (var e in elements) e.SourceMemberTag = null;
        return StoreyFragmentBuilder.Build(new StoreyFragmentParentSnapshot
        {
            Nodes = [], Members = [], Regions = [], MeshNodes = nodes, MeshElements = elements, RigidBodies = [], Groups = groups,
        }, new StoreyFragmentRequest(1.5, 4.5, "Этаж"));
    }

    static FemMemberGroup MeshGroup(IEnumerable<int> tags, int? plateSectionId = 7) =>
        new FemMemberGroup { Tag = "ЖБ: плита", Kind = FemMemberGroup.KindMesh, MemberType = "plate", PlateSectionId = plateSectionId }
            .Also(g => g.SetTags(tags.Select(t => t.ToString())));

    [Fact]
    public void MeshGroupCoveringSlab_BecomesMembersGroup()
    {
        var d = BuildUncovered(MeshGroup(Enumerable.Range(1, 16)));

        Assert.False(d.HasErrors);
        var slab = Assert.Single(d.Members).Member.ElemTag;
        var g = Assert.Single(d.Groups);
        Assert.Equal(FemMemberGroup.KindMembers, g.Kind);
        Assert.Equal(FemMemberGroup.OriginFragment, g.Origin);
        Assert.Equal("ЖБ: плита", g.Tag);
        Assert.Equal(7, g.PlateSectionId);
        Assert.Equal([slab], g.Tags);
        Assert.Contains("«ЖБ: плита» (1 КонЭ)", Assert.Single(d.Diagnostics, x => x.Code == "fragment_groups_transferred").Message);
    }

    [Fact]
    public void MeshGroupCoveringPartOfSlab_NotTransferred_Warning()
    {
        var d = BuildUncovered(MeshGroup(Enumerable.Range(1, 8)));

        Assert.Empty(d.Groups);
        var w = Assert.Single(d.Diagnostics, x => x.Code == "fragment_group_partial");
        Assert.False(w.IsError);
        Assert.Contains("8 из 16 КЭ", w.Message);
    }

    [Fact]
    public void GroupWithoutPlateSection_NotTransferred()
    {
        var d = BuildUncovered(MeshGroup(Enumerable.Range(1, 16), plateSectionId: null));

        Assert.Empty(d.Groups);
        Assert.DoesNotContain(d.Diagnostics, x => x.Code is "fragment_groups_transferred" or "fragment_group_partial");
    }

    /// <summary>Группа КонЭ родителя «S1» — КонЭ фрагмента из «S1» в группе и без сетки родителя.</summary>
    [Fact]
    public void MembersGroup_FollowsParentMember()
    {
        var slab = Slab("S1", 1, 3);
        var group = new FemMemberGroup { Tag = "Плиты", Kind = FemMemberGroup.KindMembers, PlateSectionId = 3 };
        group.SetTags(["S1"]);
        var d = StoreyFragmentBuilder.Build(new StoreyFragmentParentSnapshot
        {
            Nodes = [], Members = [slab.Member], Regions = [slab.Region!], MeshNodes = [], MeshElements = [CoveredElement("S1")],
            RigidBodies = [], Groups = [group],
        }, new StoreyFragmentRequest(1.5, 4.5, "Этаж"));

        Assert.False(d.HasErrors);
        var g = Assert.Single(d.Groups);
        Assert.Equal([Assert.Single(d.Members).Member.ElemTag], g.Tags);
        Assert.Equal(3, g.PlateSectionId);
    }
}

static class GroupTestExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
