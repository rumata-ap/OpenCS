using CScore.Fem;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentFixtures;

namespace CScore.Tests.Fem.Fragment;

public sealed class StoreyFragmentMeshTests
{
    static FemMeshNode M(string tag, double x, double y, double z) => new() { NodeTag = tag, X = x, Y = y, Z = z };

    static FemElement B(string tag, string i, string j, string? member = null, int? releaseI = null) => new()
    {
        ElemTag = tag, ElemType = "beam", NodeIdsJson = $"[{i},{j}]", SectionTag = "1", SourceMemberTag = member, ReleaseI = releaseI,
    };

    static readonly FemMeshNode[] ColumnNodes =
        [M("101", 0, 0, 0), M("102", 0, 0, 2), M("103", 0, 0, 4), M("104", 0, 0, 6)];

    [Fact]
    public void UncoveredColumnChain_BuiltAndCut()
    {
        var d = Build(1.5, 4.5, [], [Slab("S1", 1, 3)], ColumnNodes,
            [CoveredElement("S1"), B("1", "101", "102"), B("2", "102", "103"), B("3", "103", "104")]);

        Assert.False(d.HasErrors);
        var bars = d.Members.Where(m => m.Member.ElemType == "beam").ToList();
        Assert.Equal(new[] { "КЭ без КонЭ · низ", "КЭ без КонЭ · верх" }, bars.Select(b => b.Member.ElemTag));
        Assert.All(bars, b => Assert.Equal("КЭ без КонЭ", b.ParentMemberTag));
        Assert.Contains(d.Diagnostics, x => x.Code == "fragment_uncovered_members");
        Assert.Equal(2, d.Cuts.Count);
    }

    [Fact]
    public void CoveredElements_NotRebuilt()
    {
        var d = Build(1.5, 4.5, [N("1", 0, 0, 0), N("2", 0, 0, 6)], [(Bar("C1", "1", "2"), null), Slab("S1", 1, 3)], ColumnNodes,
            [CoveredElement("S1"), B("1", "101", "102", "C1"), B("2", "102", "103", "C1"), B("3", "103", "104", "C1")]);

        Assert.DoesNotContain(d.Diagnostics, x => x.Code == "fragment_uncovered_members");
        Assert.Equal(2, d.Members.Count(m => m.Member.ElemType == "beam"));
    }

    [Fact]
    public void ReleasesAndOtherTypes_ReportedNotTransferred()
    {
        var link = new FemElement { ElemTag = "7", ElemType = "link", NodeIdsJson = "[102,103]", SourceMemberTag = "C1" };
        var d = Build(1.5, 4.5, [N("1", 0, 0, 0), N("2", 0, 0, 6)], [(Bar("C1", "1", "2"), null), Slab("S1", 1, 3)], ColumnNodes,
            [CoveredElement("S1"), B("1", "101", "102", "C1"), B("2", "102", "103", "C1", releaseI: 16), B("3", "103", "104", "C1"), link]);

        Assert.Contains(d.Diagnostics, x => !x.IsError && x.Code == "fragment_releases_lost" && x.SourceKeys!.Contains("2"));
        Assert.Contains(d.Diagnostics, x => !x.IsError && x.Code == "fragment_element_type_lost" && x.SourceKeys!.Contains("7"));
    }
}
