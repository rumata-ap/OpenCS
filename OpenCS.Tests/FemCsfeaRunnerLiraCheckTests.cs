using CScore.Fem;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверка входа CSfea для схем ЛИРЫ: нет нагрузок или ГУ — ошибка с подсказкой команды (срез 5 плана 4б ЛИРЫ + 4ж).</summary>
public class FemCsfeaRunnerLiraCheckTests
{
    static FemRcModelInput Input(bool loads = false, bool support = false, bool c1 = false, bool selfWeight = false,
        bool manualNodeSupport = false) => new()
    {
        MeshNodes = [new FemMeshNode { NodeTag = "1" }],
        MeshElements = [new FemElement { ElemTag = "1", ElemType = "shell", FoundationC1 = c1 ? 1e7 : null }],
        Nodes = manualNodeSupport ? [new FemNode { NodeTag = "1", DofMask = 7 }] : [],
        LoadCases = [new FemLoadCase { Id = 1, Tag = "1", SelfWeightFactor = selfWeight ? 1.1 : null }],
        MeshNodeLoads = loads ? [new FemMeshNodeLoad { LoadCaseId = 1, MeshNodeTag = "1", Fz = -1 }] : [],
        Supports = support ? [new FemMeshNodeSupport { NodeTag = "1", Mask = 7 }] : [],
        Stages = [],
    };

    static List<string> Codes(string sourceType, FemRcModelInput input)
    {
        var diag = new List<FemValidationDiagnostic>();
        FemCsfeaRunner.CheckLiraTransfer(new FemSchema { Tag = "S", SourceType = sourceType }, input, diag);
        Assert.All(diag, d => Assert.True(d.IsError));
        return diag.Select(d => d.Code).ToList();
    }

    [Fact]
    public void LiraSchema_WithoutLoadsAndBoundary_TwoErrors() =>
        Assert.Equal(["lira_loads_not_transferred", "lira_boundary_not_transferred"], Codes("lira", Input()));

    [Fact]
    public void LiraSchema_WithAnyLoadsAndBoundary_NoErrors()
    {
        Assert.Empty(Codes("lira", Input(loads: true, support: true)));
        Assert.Empty(Codes("lira", Input(selfWeight: true, c1: true)));
        Assert.Empty(Codes("lira", Input(loads: true, manualNodeSupport: true)));
        Assert.Equal(["lira_boundary_not_transferred"], Codes("lira", Input(loads: true)));
        Assert.Equal(["lira_loads_not_transferred"], Codes("lira", Input(support: true)));
    }

    [Fact]
    public void OtherSchemas_NotChecked()
    {
        Assert.Empty(Codes("scad", Input()));
        Assert.Empty(Codes("manual", Input()));
    }
}
