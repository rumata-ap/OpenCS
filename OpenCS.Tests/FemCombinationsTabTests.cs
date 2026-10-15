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
}
