using CScore.Fem;
using CScore.Import;
using Xunit;

namespace CScore.Tests.PlateStrip;

/// <summary>Мост импорта → полоса: сечение пластины группы КЭ переходит в плоский КонЭ.</summary>
public sealed class MeshMemberPlateSectionTests
{
    [Fact]
    public void PlateSectionId_FromRequest_IsSetOnPlanarMember()
    {
        FemMeshNode N(string tag, double x, double y) => new() { NodeTag = tag, X = x, Y = y, Z = 0 };
        var nodes = new[] { N("1", 0, 0), N("2", 1, 0), N("3", 1, 1), N("4", 0, 1) };
        var element = new FemElement { ElemTag = "1", ElemType = "shell", NodeIdsJson = "[1,2,4,3]", ThicknessM = 0.2 };

        var build = MeshMemberBuilder.Build(
            new MeshMemberRequest("Плита", "plate", ["1"], PlateSectionId: 7), nodes, [element]);

        var part = Assert.Single(build.Parts);
        Assert.Equal(7, part.Member.PlateSectionId);
        Assert.NotNull(part.Region);
    }

    [Fact]
    public void PlateSectionId_NotInRequest_StaysEmpty()
    {
        FemMeshNode N(string tag, double x, double y) => new() { NodeTag = tag, X = x, Y = y, Z = 0 };
        var nodes = new[] { N("1", 0, 0), N("2", 1, 0), N("3", 1, 1), N("4", 0, 1) };
        var element = new FemElement { ElemTag = "1", ElemType = "shell", NodeIdsJson = "[1,2,4,3]", ThicknessM = 0.2 };

        var build = MeshMemberBuilder.Build(new MeshMemberRequest("Плита", "plate", ["1"]), nodes, [element]);

        Assert.Null(Assert.Single(build.Parts).Member.PlateSectionId);
    }
}

/// <summary>Плита из КЭ с разными сечениями источника: по умолчанию делится на части, с объединением — одна.</summary>
public sealed class MeshMemberMergeSectionsTests
{
    static (FemMeshNode[] Nodes, FemElement[] Elements) TwoSectionPlate()
    {
        FemMeshNode N(string tag, double x, double y) => new() { NodeTag = tag, X = x, Y = y, Z = 0 };
        var nodes = new[] { N("1", 0, 0), N("2", 1, 0), N("3", 2, 0), N("4", 0, 1), N("5", 1, 1), N("6", 2, 1) };
        FemElement E(string tag, string nodesJson, string section) => new()
        {
            ElemTag = tag, ElemType = "shell", NodeIdsJson = nodesJson, ThicknessM = 0.2, SectionTag = section
        };
        return (nodes, [E("1", "[1,2,4,5]", "A"), E("2", "[2,3,5,6]", "B")]);
    }

    [Fact]
    public void ByDefault_DifferentSectionTags_SplitIntoParts()
    {
        var (nodes, elements) = TwoSectionPlate();

        var build = MeshMemberBuilder.Build(new MeshMemberRequest("П", "plate", ["1", "2"]), nodes, elements);

        Assert.Equal(2, build.Parts.Count);
    }

    [Fact]
    public void MergeSectionTags_GivesSinglePlate()
    {
        var (nodes, elements) = TwoSectionPlate();

        var build = MeshMemberBuilder.Build(
            new MeshMemberRequest("П", "plate", ["1", "2"], MergeSectionTags: true), nodes, elements);

        var part = Assert.Single(build.Parts);
        Assert.Equal(2, part.ElementTags.Count);
        Assert.NotNull(part.Region);
    }
}
