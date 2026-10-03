using CScore.Fem;
using CScore.Import;
using CScore.Sp16;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверка стали по КЭ: параметры СП 16 КЭ из стальных групп SCAD схемы.</summary>
public sealed class FemCheckSteelGroupParamsTests
{
    static FemElement Bar(int num, int n1, int n2) =>
        new() { ElemTag = num.ToString(), ElemType = "beam", NodeIdsJson = $"[{n1},{n2}]" };

    static FemCheckSchemaData Data(params ScadSteelGroup[] groups) => new()
    {
        SourceType = "scad",
        Mesh = [Bar(1, 1, 2), Bar(2, 2, 3), Bar(3, 3, 4)],
        MeshNodes =
        [
            new FemMeshNode { NodeTag = "1", X = 0 }, new FemMeshNode { NodeTag = "2", X = 3 },
            new FemMeshNode { NodeTag = "3", X = 3, Z = 4 }, new FemMeshNode { NodeTag = "4", X = 3, Z = 6 },
        ],
        ScadSteelGroups = new ScadSteelGroupIndex(groups),
    };

    static FemCheckScope Scope(FemCheckSchemaData d) =>
        new([], [.. d.Mesh.Select(e => new FemCheckScopeElement(int.Parse(e.ElemTag), e, null))], RefersToMeshElements: true);

    [Fact]
    public void ElementOfGroup_GetsGroupParams_OthersBase()
    {
        var data = Data(ScadSteelGroupsTests.Group(2, "Балки", [1, 2], gammaC: 0.8, muXoZ: 2, step: 0.5) with { GammaN = 1.1 });
        var (paramsOf, warnings) = FemCheckContext.SteelGroupParams(data, Scope(data));
        string baseJson = new SteelDesignParams { GammaC = 1, LefX = 9, LefY = 9 }.ToJson();
        var scope = Scope(data);

        var p1 = SteelDesignParams.Parse(paramsOf!(scope.Elements[0], baseJson));
        var p2 = SteelDesignParams.Parse(paramsOf(scope.Elements[1], baseJson));

        Assert.Equal(0.8, p1.GammaC);
        Assert.Equal(6, p1.LefX, 9);                                   // μXoZ = 2, l = 3
        Assert.Equal(3, p1.LefY, 9);
        Assert.Equal(0.5, p1.LefB, 9);
        Assert.Equal(180, p1.CompressionLimit!.Base);
        Assert.Equal(8, p2.LefX, 9);                                   // l = 4
        Assert.Null(paramsOf(scope.Elements[2], baseJson));            // КЭ 3 вне групп — параметры проверки
        Assert.Equal(2, warnings.Count);                                // сводка + γn
    }

    [Fact]
    public void MemberGroup_LengthOfWholeChain()
    {
        var data = Data(ScadSteelGroupsTests.Group(1, "Стойка", [2, 3], muXoZ: 1, isMember: true));
        var (paramsOf, _) = FemCheckContext.SteelGroupParams(data, Scope(data));
        var scope = Scope(data);

        var p = SteelDesignParams.Parse(paramsOf!(scope.Elements[1], new SteelDesignParams().ToJson()));

        Assert.Equal(6, p.LefX, 9);                                    // 4 + 2
    }

    [Fact]
    public void NoGroups_NoParams()
    {
        var data = new FemCheckSchemaData { SourceType = "scad" };
        var (paramsOf, warnings) = FemCheckContext.SteelGroupParams(data, Scope(data));
        Assert.Null(paramsOf);
        Assert.Empty(warnings);
    }
}
