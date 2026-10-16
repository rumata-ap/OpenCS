using CScore.Combinations;
using CScore.Fem;
using OpenCS.Tasks;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Загружения линейного CSfea галочками в диалоге постановки (вместо строк-стадий).</summary>
public sealed class FemLinearCasesVMTests
{
    static readonly List<FemLoadCase> Cases =
    [
        new() { Id = 10, Tag = "Вес", Sp20Type = "permanent" },
        new() { Id = 11, Tag = "Оборудование", Sp20Type = "long_term", GammaFUnfav = 1.05 },
        new() { Id = 12, Tag = "Снег", Sp20Type = "short_term", LongTermShare = 0.5 },
        new() { Id = 13, Tag = "Взрыв", Sp20Type = "accidental" },
    ];

    static FemAnalysisStage CaseStage(int id, string tag) =>
        new() { Tag = tag, LoadExpressionJson = FemLinearCasesVM.CaseExpression(id).ToJson(), LoadFactorStep = 1, MaxLoadFactor = 1 };

    [Fact]
    public void NewAnalysis_AllChecked_TypesGammaShare()
    {
        var vm = new FemLinearCasesVM(Cases, Sp20GammaDefaults.Standard, null);
        Assert.All(vm.Rows, r => Assert.True(r.IsChecked));
        Assert.Equal(["G", "L", "Q", "A"], vm.Rows.Select(r => r.TypeLetter));
        Assert.Equal("1 Вес", vm.Rows[0].Label);
        Assert.Equal(FemLoadsFormat.Number(1.1), vm.Rows[0].GammaText);
        Assert.Equal(FemLoadsFormat.Number(1.05), vm.Rows[1].GammaText);
        Assert.Equal(FemLoadsFormat.Number(0.5), vm.Rows[2].ShareText);
        Assert.Equal("—", vm.Rows[0].ShareText);
        Assert.All(vm.Rows, r => Assert.False(r.IsNew));

        var stages = vm.BuildStages();
        Assert.Equal(["Вес", "Оборудование", "Снег", "Взрыв"], stages.Select(s => s.Tag));
        Assert.All(stages, s => Assert.Equal((1.0, 1.0), (s.LoadFactorStep, s.MaxLoadFactor)));
        var e = FemLoadExpression.Parse(stages[2].LoadExpressionJson);
        Assert.Equal((FemLoadExpressionMode.Single, 12), (e.Mode, Assert.Single(e.LoadCaseIds)));
    }

    [Fact]
    public void Edit_ChecksStages_NewCasesUnchecked_LegacyCombinationKept()
    {
        var combination = new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum, LoadCaseIds = [10, 12],
            Terms = [new FemLoadTerm { LoadCaseId = 10, Coefficient = 1.1 }, new FemLoadTerm { LoadCaseId = 12, Coefficient = 1.4 }]
        };
        var stages = new List<FemAnalysisStage>
        {
            CaseStage(12, "Снег"), CaseStage(10, "Вес"),
            new() { Tag = "Сочетание_01", LoadExpressionJson = combination.ToJson(), LoadFactorStep = 1, MaxLoadFactor = 1 },
        };
        var vm = new FemLinearCasesVM(Cases, Sp20GammaDefaults.Standard, stages);

        Assert.Equal(5, vm.Rows.Count);
        Assert.Equal([true, false, true, false, true], vm.Rows.Select(r => r.IsChecked));
        Assert.Equal([false, true, false, true, false], vm.Rows.Select(r => r.IsNew));
        var legacy = vm.Rows[^1];
        Assert.True(legacy.IsLegacy);
        Assert.Equal("Сочетание_01", legacy.Label);

        // Порядок — как в схеме; комбинация прежней версии — в конце с прежним выражением.
        var built = vm.BuildStages();
        Assert.Equal(["Вес", "Снег", "Сочетание_01"], built.Select(s => s.Tag));
        Assert.Equal(combination.ToJson(), built[2].LoadExpressionJson);

        legacy.IsChecked = false;
        Assert.Equal(["Вес", "Снег"], vm.BuildStages().Select(s => s.Tag));
    }

    [Fact]
    public void Filter_CheckNoneAll_OnlyVisibleRows_CountUpdates()
    {
        var vm = new FemLinearCasesVM(Cases, Sp20GammaDefaults.Standard, null);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.FilterText = "снег";
        Assert.Equal("3 Снег", Assert.Single(vm.VisibleRows).Label);
        vm.CheckNone();
        Assert.Equal(3, vm.CheckedCount);
        Assert.False(vm.Rows[2].IsChecked);
        Assert.Contains(nameof(FemLinearCasesVM.CountText), changed);

        vm.FilterText = "";
        Assert.Equal(4, vm.VisibleRows.Count);
        vm.CheckNone();
        Assert.Equal(0, vm.CheckedCount);
        Assert.Empty(vm.BuildStages());
        vm.CheckAll();
        Assert.Equal(4, vm.CheckedCount);
    }
}
