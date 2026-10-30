using CScore.Fem;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentFixtures;

namespace CScore.Tests.Fem.Fragment;

public sealed class StoreyFragmentMeshTests
{
    static FemMeshNode M(string tag, double x, double y, double z) => new() { NodeTag = tag, X = x, Y = y, Z = z };

    static FemElement B(string tag, string i, string j, string? member = null, int? releaseI = null, int? section = null) => new()
    {
        ElemTag = tag, ElemType = "beam", NodeIdsJson = $"[{i},{j}]", SectionTag = "1", SourceMemberTag = member, ReleaseI = releaseI,
        CrossSectionId = section,
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

    /// <summary>
    /// КЭ ссылаются на КонЭ C1, которого нет в снимке (у КонЭ не нашлось узлов схемы): КЭ собраны без КонЭ, а пропажа КонЭ
    /// названа отдельно — иначе потеря его сечения не видна. КЭ без ссылки на КонЭ в этот список не входят.
    /// </summary>
    [Fact]
    public void ElementsOfMissingMember_ReportedUnresolved()
    {
        var d = Build(1.5, 4.5, [], [Slab("S1", 1, 3)], ColumnNodes,
            [CoveredElement("S1"), B("1", "101", "102", "C1"), B("2", "102", "103", "C1"), B("3", "103", "104")]);
        var clean = Build(1.5, 4.5, [], [Slab("S1", 1, 3)], ColumnNodes,
            [CoveredElement("S1"), B("1", "101", "102"), B("2", "102", "103"), B("3", "103", "104")]);

        var w = Assert.Single(d.Diagnostics, x => x.Code == "fragment_member_unresolved");
        Assert.False(w.IsError);
        Assert.Equal(["C1"], w.SourceKeys!);
        Assert.Contains(d.Diagnostics, x => x.Code == "fragment_uncovered_members");
        Assert.DoesNotContain(clean.Diagnostics, x => x.Code == "fragment_member_unresolved");
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
    /// <summary>Сечение КонЭ-стержня без своего (импорт SCAD/ЛИРЫ) — от КЭ родителя на участке части: низ 1,5…3 —
    /// 0,5 м сечения 7 и 1 м сечения 8 → 8 с предупреждением; верх 3…4,5 — только 8.</summary>
    [Fact]
    public void BarWithoutSection_TakesParentElementSectionOnPart()
    {
        var column = Bar("C1", "1", "2");
        column.CrossSectionId = null;
        var d = Build(1.5, 4.5, [N("1", 0, 0, 0), N("2", 0, 0, 6)], [(column, null), Slab("S1", 1, 3)], ColumnNodes,
            [CoveredElement("S1"), B("1", "101", "102", "C1", section: 7), B("2", "102", "103", "C1", section: 8),
             B("3", "103", "104", "C1", section: 8)]);

        var bars = d.Members.Where(m => m.Member.ElemType == "beam").ToDictionary(m => m.Member.ElemTag, m => m.Member.CrossSectionId);
        Assert.Equal(8, bars["C1 · низ"]);
        Assert.Equal(8, bars["C1 · верх"]);
        var varies = Assert.Single(d.Diagnostics, x => x.Code == "fragment_bar_section_varies");
        Assert.Equal(["C1 · низ"], varies.SourceKeys);
    }

    /// <summary>Своё сечение КонЭ не перебивается сечением КЭ.</summary>
    [Fact]
    public void BarWithSection_KeepsIt()
    {
        var d = Build(1.5, 4.5, [N("1", 0, 0, 0), N("2", 0, 0, 6)], [(Bar("C1", "1", "2"), null), Slab("S1", 1, 3)], ColumnNodes,
            [CoveredElement("S1"), B("1", "101", "102", "C1", section: 7), B("2", "102", "103", "C1", section: 7),
             B("3", "103", "104", "C1", section: 7)]);

        Assert.All(d.Members.Where(m => m.Member.ElemType == "beam"), m => Assert.Equal(1, m.Member.CrossSectionId));
    }

    /// <summary>КЭ без КонЭ: сечение — от своих КЭ по участку части.</summary>
    [Fact]
    public void UncoveredColumn_TakesElementSection()
    {
        var d = Build(1.5, 4.5, [], [Slab("S1", 1, 3)], ColumnNodes,
            [CoveredElement("S1"), B("1", "101", "102", section: 5), B("2", "102", "103", section: 5), B("3", "103", "104", section: 5)]);

        var bars = d.Members.Where(m => m.Member.ElemType == "beam").ToList();
        Assert.Equal(2, bars.Count);
        Assert.All(bars, b => Assert.Equal(5, b.Member.CrossSectionId));
        Assert.DoesNotContain(d.Diagnostics, x => x.Code == "fragment_bar_section_varies");
    }
}
