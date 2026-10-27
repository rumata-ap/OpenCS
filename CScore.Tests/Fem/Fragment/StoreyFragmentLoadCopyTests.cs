using CScore.Fem;
using CScore.Fem.Fragment;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentLoadFixtures;

namespace CScore.Tests.Fem.Fragment;

public sealed class StoreyFragmentLoadCopyTests
{
    [Fact]
    public void LoadCasesAndDefinitions_CopiedWithParentIds()
    {
        var lc = new FemLoadCase
        {
            Id = 7, Tag = "Полезная", Sp20Type = "long_term", LongTermShare = 0.35, SelfWeightFactor = 1.1, Psi1 = 0.9,
            Origin = "import:lira", SourceLoadNum = 3,
        };
        var def = new FemLoadDefinition { Id = 4, Tag = "1.1·П", SourceKind = "manual" };
        def.SetExpression(new FemLoadExpression { Mode = FemLoadExpressionMode.Sum, Terms = [new FemLoadTerm { LoadCaseId = 7, Coefficient = 1.1 }] });

        var d = Build(Snapshot(loadCases: [lc], definitions: [def]));

        Assert.False(d.HasErrors);
        var copy = Assert.Single(d.LoadCases);
        Assert.NotSame(lc, copy);
        Assert.Equal((7, "Полезная", "long_term", 0.35, 1.1, 0.9, "import:lira", 3),
            (copy.Id, copy.Tag, copy.Sp20Type, copy.LongTermShare, copy.SelfWeightFactor, copy.Psi1, copy.Origin, copy.SourceLoadNum));
        var defCopy = Assert.Single(d.LoadDefinitions);
        Assert.NotSame(def, defCopy);
        Assert.Equal(def.ExpressionJson, defCopy.ExpressionJson);
    }

    [Fact]
    public void RemapLoadCases_ReplacesIdsInListAndTerms()
    {
        var def = new FemLoadDefinition { Id = 4, Tag = "Сочетание" };
        def.SetExpression(new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sp20, LoadCaseIds = [7, 8],
            Terms = [new FemLoadTerm { LoadCaseId = 7, Coefficient = 1.1, Role = "permanent", Mode = "auto" }],
        });

        var e = FragmentLoadCopy.RemapLoadCases(def, new Dictionary<int, int> { [7] = 70 }).GetExpression();

        Assert.Equal(new[] { 70, 8 }, e.LoadCaseIds);
        var t = Assert.Single(e.Terms);
        Assert.Equal((70, 1.1, "permanent", "auto"), (t.LoadCaseId, t.Coefficient, t.Role, t.Mode));
        Assert.Equal(FemLoadExpressionMode.Sp20, e.Mode);
    }

    [Fact]
    public void RefElements_AreBandElements()
    {
        var d = Build(Snapshot());
        // Плита 4×4 на z = 3 целиком в полосе; колонна: КЭ 1,5…3 и 3…4,5 внутри, 0…1,5 и 4,5…6 касаются границы — не в полосе.
        Assert.Equal(16, d.RefElements.Count(r => r.IsShell));
        Assert.Equal(new[] { "502", "503" }, d.RefElements.Where(r => r.IsBar).Select(r => r.Tag).OrderBy(t => t));
    }
}
