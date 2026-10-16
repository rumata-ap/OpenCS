using CScore.Fem;
using CScore.Fem.Combinations;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Вкладка «Комбинации»: перенос загружений, коэффициенты, имя и тип — командами редактора с отменой.</summary>
[Collection("FEM schema editor VM")]
public sealed class FemCombinationsTabTests(FemSchemaEditorVMFixture fixture)
{
    (FemSchemaEditorVM editor, FemCombinationsTabVM tab, FemLoadCase g, FemLoadCase q, FemLoadCase s) NewTab()
    {
        var editor = new FemSchemaEditorVM(new FemSchema { Id = 1, Tag = "S" }, fixture.App);
        var g = new FemLoadCase { Id = 1, SchemaId = 1, Tag = "G", Sp20Type = "permanent" };
        var q = new FemLoadCase { Id = 2, SchemaId = 1, Tag = "Q", Sp20Type = "short_term" };
        var s = new FemLoadCase { Id = 3, SchemaId = 1, Tag = "S", Sp20Type = "short_term" };
        editor.Session.LoadCases.AddRange([g, q, s]);
        editor.RefreshCollections();
        return (editor, new FemCombinationsTabVM(editor), g, q, s);
    }

    static Dictionary<int, double> Factors(FemLoadDefinition d, IReadOnlyList<FemLoadCase> cases) =>
        FemLoadExpressionResolver.Terms(d.GetExpression(), cases).ToDictionary(t => t.LoadCase.Id, t => t.Factor);

    [Fact]
    public void AddTerms_WithFieldCoefficient_FormulaAndOneUndo()
    {
        var (editor, tab, g, q, _) = NewTab();
        tab.AddCombination();
        var d = Assert.Single(editor.Session.LoadDefinitions);
        Assert.Same(d, tab.SelectedDefinition);
        Assert.Equal("C", d.CombinationType);
        Assert.Equal(3, tab.AvailableCases.Count);

        tab.CoefficientText = FemLoadsFormat.Number(1.35);
        tab.AddTerms(tab.AvailableCases.Where(r => r.LoadCase == g).ToList());
        // Одно слагаемое считается с его коэффициентом (а не как первое загружение с 1,0).
        Assert.Equal(new Dictionary<int, double> { [g.Id] = 1.35 }, Factors(d, editor.Session.LoadCases));

        tab.CoefficientText = FemLoadsFormat.Number(1.5);
        tab.AddTerms(tab.AvailableCases.Where(r => r.LoadCase == q).ToList());
        string Code(int n) => FemCombinationsTabVM.CaseCode(n);
        Assert.Equal($"{FemLoadsFormat.Number(1.35)}·{Code(1)} + {FemLoadsFormat.Number(1.5)}·{Code(2)}", tab.Rows[0].Formula);
        Assert.Equal(2, tab.Terms.Count);
        Assert.Single(tab.AvailableCases);

        editor.UndoCommand.Execute(null);
        Assert.Single(tab.Terms);
        Assert.Equal(2, tab.AvailableCases.Count);
    }

    [Fact]
    public void Coefficients_CellApplyReset_RejectZero()
    {
        var (editor, tab, _, _, _) = NewTab();
        tab.AddCombination();
        tab.AddAllTerms();
        Assert.Equal(3, tab.Terms.Count);
        var firstRow = tab.Terms[0];

        firstRow.Coefficient = FemLoadsFormat.Number(1.1);
        Assert.Same(firstRow, tab.Terms[0]); // правка в ячейке не пересобирает таблицу
        Assert.Equal(1.1, tab.Terms[0].Term.Coefficient);

        firstRow.Coefficient = "0";
        Assert.Equal(1.1, tab.Terms[0].Term.Coefficient);
        Assert.NotNull(tab.Status);

        tab.CoefficientText = FemLoadsFormat.Number(0.9);
        tab.ApplyCoefficient(tab.Terms.Skip(1).ToList());
        Assert.Equal([1.1, 0.9, 0.9], tab.Terms.Select(t => t.Term.Coefficient));
        tab.ResetCoefficient(tab.Terms.Take(1).ToList());
        Assert.Equal([1.0, 0.9, 0.9], tab.Terms.Select(t => t.Term.Coefficient));

        tab.RemoveTerms(tab.Terms.Skip(1).Take(1).ToList());
        Assert.Equal([1, 3], tab.Terms.Select(t => t.Term.LoadCaseId));
        tab.RemoveAllTerms();
        Assert.Empty(tab.Terms);
        Assert.Empty(Factors(tab.SelectedDefinition!, editor.Session.LoadCases));
    }

    [Fact]
    public void NameTypeComment_CopyDelete_Filter()
    {
        var (editor, tab, _, _, _) = NewTab();
        tab.AddCombination();
        tab.AddCombination();
        string first = editor.Session.LoadDefinitions[0].Tag;
        Assert.Equal([first, first + " 2"], editor.Session.LoadDefinitions.Select(d => d.Tag));

        tab.TagText = first;
        Assert.Equal(first + " 2", tab.SelectedDefinition!.Tag);
        Assert.NotNull(tab.Status);
        tab.TagText = "Основное";
        tab.TypeText = "NL";
        tab.CommentText = "проверка";
        var d = tab.SelectedDefinition!;
        Assert.Equal(("Основное", "NL", "проверка"), (d.Tag, d.CombinationType, d.Description));
        Assert.Equal("NL", d.GetExpression().CombinationType);

        tab.CopyCombination();
        Assert.Equal("Основное 2", tab.SelectedDefinition!.Tag);
        Assert.Equal(3, tab.Rows.Count);

        tab.DeleteCombinations(tab.Rows.Take(2).ToList());
        Assert.Single(editor.Session.LoadDefinitions);
        editor.UndoCommand.Execute(null);
        Assert.Equal(3, editor.Session.LoadDefinitions.Count);

        tab.Filter = "short_term";
        Assert.Equal(2, tab.AvailableCases.Count);
        Assert.Equal(["", "permanent", "short_term"], tab.CaseFilters.Select(f => f.Key));
    }

    [Fact]
    public void GenerateSp20_KeepsGeneratorAndEditsAsSum()
    {
        var (editor, tab, g, q, s) = NewTab();
        editor.Session.Nodes.Add(new FemNode { Id = 11, NodeTag = "1" });
        editor.Session.NodeLoads.AddRange([
            new FemNodeLoad { LoadCaseId = g.Id, NodeId = 11, Fz = -10 },
            new FemNodeLoad { LoadCaseId = q.Id, NodeId = 11, Fz = -5 },
            new FemNodeLoad { LoadCaseId = s.Id, NodeId = 11, Fz = -3 },
        ]);
        editor.RefreshCollections();

        tab.GenerateSp20();
        Assert.NotEmpty(tab.Rows);
        // Основное сочетание по Fz−: G·1,1 + ведущая кратковременная Q·1,4·1,0 + вторая S·1,4·0,9 (п. 6.4 СП 20).
        var down = editor.Session.LoadDefinitions.Select(d => Factors(d, editor.Session.LoadCases))
            .Single(f => f.Count == 3 && f[q.Id] > f[s.Id]);
        Assert.Equal(1.1, down[g.Id], 9);
        Assert.Equal(1.4, down[q.Id], 9);
        Assert.Equal(1.4 * 0.9, down[s.Id], 9);
    }

    [Fact]
    public void AutoTerms_DefaultRoles_TypeChange_ManualAndBack()
    {
        var (editor, tab, g, q, s) = NewTab();
        var gamma = editor.Sp20GammaDefaults;
        tab.AddCombination();
        Assert.Equal(FemCombinationsTabVM.AutoText, tab.CoefficientText);
        tab.AddAllTerms();

        // G — постоянная, первое кратковременное — основное, второе — второе (ψ = 0,9 по п. 6.4 СП 20).
        Assert.Equal([FemLoadTermRoles.Permanent, FemLoadTermRoles.Leading, FemLoadTermRoles.Second],
            tab.Terms.Select(t => t.Role));
        Assert.All(tab.Terms, t => Assert.True(t.IsAuto));
        var f = Factors(tab.SelectedDefinition!, editor.Session.LoadCases);
        Assert.Equal(gamma.PermanentUnfav, f[g.Id], 9);
        Assert.Equal(gamma.ShortTermUnfav, f[q.Id], 9);
        Assert.Equal(gamma.ShortTermUnfav * 0.9, f[s.Id], 9);
        Assert.Contains("0", tab.Terms[2].CoefficientDisplay);
        Assert.Contains("=", tab.Terms[2].CoefficientDisplay);

        // ПС2 по нормативным: γf не входит, ψ остаётся; отмена возвращает тип и коэффициенты.
        tab.TypeText = FemCombinationTypes.N;
        f = Factors(tab.SelectedDefinition!, editor.Session.LoadCases);
        Assert.Equal([1.0, 1.0, 0.9], new[] { f[g.Id], f[q.Id], f[s.Id] }.Select(x => Math.Round(x, 9)));
        editor.UndoCommand.Execute(null);
        Assert.Equal(FemCombinationTypes.C, tab.SelectedDefinition!.CombinationType);
        Assert.Equal(gamma.ShortTermUnfav * 0.9, Factors(tab.SelectedDefinition!, editor.Session.LoadCases)[s.Id], 9);

        // Роль «прочие» — ψ 0,7; число в ячейке — «задано» с прежней ролью; «авто» в поле — обратно по роли.
        tab.Terms[2].Role = FemLoadTermRoles.Other;
        Assert.Equal(gamma.ShortTermUnfav * 0.7, tab.Terms[2].Term.Coefficient, 9);
        tab.Terms[2].Coefficient = FemLoadsFormat.Number(1.25);
        Assert.False(tab.Terms[2].IsAuto);
        Assert.Equal((FemLoadTermRoles.Other, 1.25), (tab.Terms[2].Role, tab.Terms[2].Term.Coefficient));
        Assert.Equal(FemLoadsFormat.Number(1.25), tab.Terms[2].CoefficientDisplay);
        tab.CoefficientText = FemCombinationsTabVM.AutoText;
        tab.ApplyCoefficient(tab.Terms.Skip(2).ToList());
        Assert.True(tab.Terms[2].IsAuto);
        Assert.Equal(gamma.ShortTermUnfav * 0.7, tab.Terms[2].Term.Coefficient, 9);

        // Постоянной доступны γf и γf п. 7.3, особой — только «особая».
        Assert.Equal([FemLoadTermRoles.Permanent, FemLoadTermRoles.PermanentReduced], tab.Terms[0].RoleChoices.Select(c => c.Key));
        tab.Terms[0].Role = FemLoadTermRoles.PermanentReduced;
        Assert.Equal(gamma.PermanentFav, tab.Terms[0].Term.Coefficient, 9);
    }

    [Fact]
    public void AutoTerms_FollowLoadCaseEdit_InOneUndo_AndStaleRecompute()
    {
        var (editor, tab, _, q, _) = NewTab();
        tab.AddCombination();
        tab.AddAllTerms();
        var d = tab.SelectedDefinition!;
        double before = Factors(d, editor.Session.LoadCases)[q.Id];

        // γf загружения 1,3 — «авто»-коэффициент следует за ним; одна отмена возвращает и загружение, и формулу.
        var values = new FemLoadCase { Tag = q.Tag, Sp20Type = q.Sp20Type, GammaFUnfav = 1.3 };
        Assert.Null(editor.EditLoadCase(q, values));
        Assert.Equal(1.3, Factors(d, editor.Session.LoadCases)[q.Id], 9);
        editor.UndoCommand.Execute(null);
        Assert.Null(q.GammaFUnfav);
        Assert.Equal(before, Factors(d, editor.Session.LoadCases)[q.Id], 9);

        // Формула с устаревшим «авто» (как после смены настроек) — плашка и пересчёт одной отменой.
        var expression = d.GetExpression();
        d.SetExpression(new FemLoadExpression
        {
            Mode = expression.Mode, CombinationType = expression.CombinationType,
            Terms = expression.Terms.Select(t => new FemLoadTerm
            {
                LoadCaseId = t.LoadCaseId, Role = t.Role, Mode = t.Mode, Coefficient = t.Coefficient + 1,
            }).ToList(),
        });
        tab.Reload();
        Assert.True(tab.HasStale);
        tab.RecomputeAuto();
        Assert.False(tab.HasStale);
        Assert.Equal(before, Factors(d, editor.Session.LoadCases)[q.Id], 9);
        editor.UndoCommand.Execute(null);
        Assert.True(tab.HasStale);
    }

    [Fact]
    public void LegacyNumericTerms_StayManual()
    {
        var (editor, tab, g, _, _) = NewTab();
        tab.AddCombination();
        tab.CoefficientText = FemLoadsFormat.Number(1.35);
        tab.AddTerms(tab.AvailableCases.Where(r => r.LoadCase == g).ToList());
        Assert.False(tab.Terms[0].IsAuto);
        Assert.Equal("", tab.Terms[0].Role);
        Assert.Null(tab.Terms[0].Term.Mode);
        Assert.Equal(0, editor.StaleAutoCombinationCount);
        Assert.Equal(1.35, tab.Terms[0].Term.Coefficient);
    }

    [Fact]
    public void TreeDialogEdit_KeepsAutoForUnchangedCoefficients()
    {
        var (editor, tab, g, q, s) = NewTab();
        tab.AddCombination();
        tab.AddAllTerms();
        var d = tab.SelectedDefinition!;
        // Как диалог «Изменить…» из дерева: числа с 4 знаками, у S — новое значение, тип — N.
        var numbers = d.GetExpression().Terms.Select(t => new FemLoadTerm
        {
            LoadCaseId = t.LoadCaseId, Coefficient = t.LoadCaseId == s.Id ? 2 : Math.Round(t.Coefficient, 4),
        }).ToList();
        Assert.Null(editor.EditLoadDefinition(d, d.Tag, FemCombinationTypes.N, numbers));
        var terms = d.GetExpression().Terms.ToDictionary(t => t.LoadCaseId);
        Assert.True(terms[g.Id].IsAuto);
        Assert.Equal(1.0, terms[g.Id].Coefficient, 9); // «авто» пересчитано под ПС2
        Assert.Equal((FemLoadTerm.ManualMode, FemLoadTermRoles.Second, 2.0), (terms[s.Id].Mode, terms[s.Id].Role, terms[s.Id].Coefficient));
    }

    [Fact]
    public void ImportedLoadCases_WithNormativeSetting_Warn()
    {
        var (editor, tab, g, _, _) = NewTab();
        Assert.False(tab.HasValueKindConflict);
        g.Origin = CScore.Fem.FemLoadOrigin.ImportPrefix + "lira";
        tab.Reload();
        bool normative = editor.Sp20LoadValueKind == CScore.Combinations.Sp20LoadValueKind.Normative;
        Assert.Equal(normative, tab.HasValueKindConflict);
        Assert.False(string.IsNullOrEmpty(tab.ValueKindNote));
    }
}
