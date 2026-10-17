using CScore.Combinations;
using CScore.Fem;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Окно выбора нагрузки стадии нелинейного расчёта: источники, формула, поиск, фильтр типов, сохранённые стадии.</summary>
public sealed class FemLoadSourcePickerVMTests
{
    static readonly List<FemLoadCase> Cases = Enumerable.Range(1, 31).Select(i => new FemLoadCase
    {
        Id = 100 + i,
        Tag = i switch { 1 => "Вес", 2 => "Снег", 3 => "Ветер", _ => $"Нагрузка {i}" },
        Sp20Type = i == 1 ? "permanent" : "short_term",
        LongTermShare = i == 2 ? 0.5 : null,
    }).ToList();

    static FemLoadDefinition Def(int id, string tag, string? type, string source, params (int Case, double K)[] terms)
    {
        var d = new FemLoadDefinition { Id = id, Tag = tag, CombinationType = type, SourceKind = source };
        d.SetExpression(new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum, CombinationType = type,
            Terms = terms.Select(t => new FemLoadTerm { LoadCaseId = 100 + t.Case, Coefficient = t.K }).ToList(),
        });
        return d;
    }

    static readonly List<FemLoadDefinition> Definitions =
    [
        Def(1, "Сочетание_01", null, "manual", (1, 1), (2, 1)),
        Def(2, "Огибающая N 001", "N", "envelope", (1, 1), (3, 1)),
        Def(3, "Огибающая C 001", "C", "envelope", (1, 1.35), (2, 1.05), (3, -0.9)),
        Def(4, "Огибающая C 002", "C", "envelope", (1, 1.1), (31, 1.4)),
    ];

    static List<FemLoadSource> Sources() => FemLoadSources.Build(Definitions, Cases, Sp20GammaDefaults.Standard);

    static string Code(int n) => FemCombinationsTabVM.CaseCode(n);

    [Fact]
    public void Build_CombinationsByTypeThenCases_FormulaTermsOrigin()
    {
        var sources = Sources();
        var combos = sources.Where(s => s.Kind == FemLoadSourceKind.Combination).ToList();
        // Без типа — как C (как в дереве), порядок C → N, внутри — порядок вкладки «Комбинации».
        Assert.Equal(["Сочетание_01", "Огибающая C 001", "Огибающая C 002", "Огибающая N 001"], combos.Select(c => c.Tag));
        Assert.Equal(["C", "C", "C", "N"], combos.Select(c => c.TypeBadge));
        Assert.Equal([1, 3, 4, 2], combos.Select(c => c.Number));

        var c001 = combos[1];
        string n135 = FemLoadsFormat.Number(1.35), n105 = FemLoadsFormat.Number(1.05), n09 = FemLoadsFormat.Number(0.9);
        Assert.Equal($"{n135}·{Code(1)} + {n105}·{Code(2)} − {n09}·{Code(3)}", c001.Formula);
        Assert.Equal(["Вес", "Снег", "Ветер"], c001.Terms.Select(t => t.Name));
        Assert.Equal("−" + n09, c001.Terms[2].Coefficient);
        Assert.Equal(FemLoadSources.OriginLabel("envelope"), c001.Origin);
        Assert.Equal(FemLoadSources.OriginLabel("manual"), combos[0].Origin);

        var cases = sources.Where(s => s.Kind == FemLoadSourceKind.LoadCase).ToList();
        Assert.Equal(31, cases.Count);
        Assert.Equal(["G", "Q"], cases.Take(2).Select(c => c.TypeBadge));
        Assert.Equal(Code(2), cases[1].Formula);
        Assert.Equal(FemLoadsFormat.Number(0.5), cases[1].ShareText);
        Assert.Equal("—", cases[2].ShareText);
        Assert.Equal(FemLinearCasesVM.CaseExpression(102).ToJson(), cases[1].ExpressionJson);
    }

    [Fact]
    public void Search_CaseCodeIsExact_NameOfTermCase_AllWordsMustMatch()
    {
        FemLoadSourcePickerVM.ResetSessionMemory();
        var vm = new FemLoadSourcePickerVM(Sources(), null);

        vm.CombinationSearch = Code(3);   // ЗГ3 — не ЗГ31
        Assert.Equal(["Огибающая C 001", "Огибающая N 001"], vm.VisibleCombinations.Select(c => c.Tag));

        vm.CombinationSearch = Code(31).ToLowerInvariant();
        Assert.Equal(["Огибающая C 002"], vm.VisibleCombinations.Select(c => c.Tag));

        vm.CombinationSearch = "снег";    // имя загружения слагаемого
        Assert.Equal(["Сочетание_01", "Огибающая C 001"], vm.VisibleCombinations.Select(c => c.Tag));

        vm.CombinationSearch = "огибающая снег";
        Assert.Equal(["Огибающая C 001"], vm.VisibleCombinations.Select(c => c.Tag));
        Assert.Equal(string.Format(OpenCS.Utilites.Loc.S("FemLoadSourcePickerShown"), 1, 4), vm.CombinationsShownText);

        vm.CaseSearch = "нагрузка 3";   // «3» — не код: подстрока имени
        Assert.Equal(["Нагрузка 13", "Нагрузка 23", "Нагрузка 30", "Нагрузка 31"], vm.VisibleCases.Select(c => c.Tag));
    }

    [Fact]
    public void TypeFilters_CountsAvailability_AndSessionMemory()
    {
        FemLoadSourcePickerVM.ResetSessionMemory();
        var vm = new FemLoadSourcePickerVM(Sources(), null);
        Assert.Equal([3, 0, 1, 0, 0], vm.TypeFilters.Select(f => f.Count));
        Assert.Equal([true, false, true, false, false], vm.TypeFilters.Select(f => f.IsOn));

        vm.TypeFilters[0].IsOn = false;   // C выключен
        Assert.Equal(["Огибающая N 001"], vm.VisibleCombinations.Select(c => c.Tag));

        // Следующее окно помнит выключенный C…
        var next = new FemLoadSourcePickerVM(Sources(), null);
        Assert.False(next.TypeFilters[0].IsOn);
        // …но если текущая нагрузка стадии — комбинация C, её тип включается.
        var sources = Sources();
        var withCurrent = new FemLoadSourcePickerVM(sources, sources.First(s => s.Tag == "Огибающая C 002"));
        Assert.True(withCurrent.TypeFilters[0].IsOn);
        Assert.Equal("Огибающая C 002", withCurrent.Selected?.Tag);

        next.AllTypes();
        Assert.Equal(4, next.VisibleCombinations.Count);
        Assert.True(new FemLoadSourcePickerVM(Sources(), null).TypeFilters[0].IsOn);
    }

    [Fact]
    public void Selection_OpensOnCurrentTab_HiddenRowIsNotAccepted_SingleVisibleSelected()
    {
        FemLoadSourcePickerVM.ResetSessionMemory();
        var sources = Sources();
        var snow = sources.First(s => s.Kind == FemLoadSourceKind.LoadCase && s.Tag == "Снег");
        var vm = new FemLoadSourcePickerVM(sources, snow);
        Assert.Equal(FemLoadSourcePickerVM.CasesTab, vm.SelectedTab);
        Assert.Same(snow, vm.Selected);
        Assert.Equal(["Снег"], vm.SelectedTerms.Select(t => t.Name));

        vm.CaseSearch = "ветер";                 // выбранная строка ушла под фильтр — выбрать нельзя
        Assert.Null(vm.Selected);
        Assert.False(vm.CanAccept);
        vm.SelectedCase = null;                  // сетка сбрасывает выделение — выбор сохраняется
        vm.CaseSearch = "";
        Assert.Same(snow, vm.Selected);

        vm.SelectedTab = FemLoadSourcePickerVM.CombinationsTab;
        Assert.Null(vm.Selected);
        vm.CombinationSearch = Code(31);
        vm.SelectSingleVisible();
        Assert.Equal("Огибающая C 002", vm.Selected?.Tag);
    }

    [Fact]
    public void NoCombinations_OpensOnCasesTab()
    {
        FemLoadSourcePickerVM.ResetSessionMemory();
        var vm = new FemLoadSourcePickerVM(FemLoadSources.Build([], Cases, Sp20GammaDefaults.Standard), null);
        Assert.False(vm.HasCombinations);
        Assert.Equal(FemLoadSourcePickerVM.CasesTab, vm.SelectedTab);
    }

    [Fact]
    public void Resolve_ExactOrSameComposition_OtherwiseKeptAsIs()
    {
        var sources = Sources();
        var c001 = sources.First(s => s.Tag == "Огибающая C 001");
        Assert.Same(c001, FemLoadSources.Resolve(sources, c001.ExpressionJson, Cases));

        // Тот же состав, но слагаемые с ролью «авто» (пересчитанная комбинация) — та же комбинация.
        var withRoles = new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            Terms = FemLoadExpression.Parse(c001.ExpressionJson).Terms
                .Select(t => new FemLoadTerm { LoadCaseId = t.LoadCaseId, Coefficient = t.Coefficient, Role = "other", Mode = FemLoadTerm.AutoMode })
                .ToList(),
        };
        Assert.Same(c001, FemLoadSources.Resolve(sources, withRoles.ToJson(), Cases));

        // Одно загружение — источник-загружение (формат прежнего выпадающего списка).
        Assert.Equal("Ветер", FemLoadSources.Resolve(sources, FemLinearCasesVM.CaseExpression(103).ToJson(), Cases).Tag);

        // Удалённая комбинация — выражение как есть, не первая попавшаяся.
        var gone = new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            Terms = [new FemLoadTerm { LoadCaseId = 101, Coefficient = 2 }, new FemLoadTerm { LoadCaseId = 999, Coefficient = 1 }],
        };
        var unresolved = FemLoadSources.Resolve(sources, gone.ToJson(), Cases);
        Assert.True(unresolved.IsUnresolved);
        Assert.Equal(gone.ToJson(), unresolved.ExpressionJson);
        Assert.Equal($"2·{Code(1)} + 1·#999", unresolved.Formula);
    }

    [Fact]
    public void MultiSelect_ChecksSurviveFilters_VisibleOnly_DisabledNeverChecked()
    {
        FemLoadSourcePickerVM.ResetSessionMemory();
        var sources = Sources();
        var c002 = sources.First(s => s.Tag == "Огибающая C 002");
        c002.DisabledReason = "нет";
        var n001 = sources.First(s => s.Tag == "Огибающая N 001");
        var snow = sources.First(s => s.Tag == "Снег");
        // Прежняя отметка источника из другого окна сбрасывается.
        new FemLoadSourcePickerVM(sources, null, multiSelect: true, [snow]);
        var vm = new FemLoadSourcePickerVM(sources, null, multiSelect: true, [n001, c002]);

        Assert.True(vm.IsMultiSelect);
        Assert.Equal([n001], vm.CheckedSources);
        Assert.False(snow.IsChecked);
        Assert.Equal(FemLoadSourcePickerVM.CombinationsTab, vm.SelectedTab);

        // Отметить видимые под фильтром типа C: недоступная не отмечается, скрытая N остаётся отмеченной.
        vm.TypeFilters.First(f => f.Type == "N").IsOn = false;
        vm.CheckVisible();
        Assert.Equal(["Сочетание_01", "Огибающая C 001", "Огибающая N 001"], vm.CheckedSources.Select(s => s.Tag));
        vm.SetChecked(c002, true);
        Assert.False(c002.IsChecked);

        vm.UncheckVisible();
        Assert.Equal([n001], vm.CheckedSources);

        vm.SelectedTab = FemLoadSourcePickerVM.CasesTab;
        vm.CaseSearch = "Снег";
        vm.CheckVisible();
        vm.Toggle(sources.First(s => s.Tag == "Вес"));
        Assert.Equal(["Огибающая N 001", "Вес", "Снег"], vm.CheckedSources.Select(s => s.Tag));
        Assert.Equal(1, vm.CheckedCombinationCount);
        Assert.Equal(2, vm.CheckedCaseCount);
        Assert.True(vm.CanAccept);
        Assert.Equal(string.Format(OpenCS.Utilites.Loc.S("FemLoadSourcePickerAcceptChecked"), 3), vm.AcceptText);

        foreach (var s in vm.CheckedSources) vm.Toggle(s);
        Assert.False(vm.CanAccept);
    }

    [Fact]
    public void SingleSelect_ClearsStaleChecks_AndIgnoresSetChecked()
    {
        FemLoadSourcePickerVM.ResetSessionMemory();
        var sources = Sources();
        new FemLoadSourcePickerVM(sources, null, multiSelect: true, [sources[0]]);
        var vm = new FemLoadSourcePickerVM(sources, null);
        Assert.False(sources[0].IsChecked);
        vm.SetChecked(sources[1], true);
        Assert.Equal(0, vm.CheckedCount);
    }

    [Fact]
    public void BatchSummary_TypesThenCasesThenUnresolved()
    {
        var sources = Sources();
        var unresolved = FemLoadSources.Resolve(sources, "{\"Mode\":1,\"Terms\":[{\"LoadCaseId\":999,\"Coefficient\":1}]}", Cases);
        var items = new List<FemLoadSource> { sources[3], sources[1], sources[2], sources[4], sources[5], unresolved };
        string text = FemLoadSources.BatchSummary(items);
        Assert.Equal(string.Format(OpenCS.Utilites.Loc.S("FemCsfeaBatchSummary"), 6,
            $"C 2 · N 1 · {OpenCS.Utilites.Loc.S("FemCombinationsCaseCode")} 2 · "
            + string.Format(OpenCS.Utilites.Loc.S("FemCsfeaBatchUnresolvedCount"), 1)), text);
    }
}
