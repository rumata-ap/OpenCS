using CScore.Combinations;
using CScore.Fem;
using CScore.Fem.Combinations;
using CScore.Fem.Imperfections;
using Xunit;

namespace CScore.Tests;

/// <summary>Загружение «Несовершенство» (срез 1): параметры, валидатор схемы, СП 20, коэффициенты и выражения.</summary>
public class FemImperfectionModelTests
{
    static FemLoadCase Case(int id, string tag, string type = FemSp20Types.ShortTerm) =>
        new() { Id = id, SchemaId = 1, Tag = tag, Sp20Type = type };

    [Fact]
    public void Params_RoundTrip_AndDefaults()
    {
        var sway = new FemSwayParams
        {
            Scope = FemSwayParams.ScopeList, MemberTags = ["К1", "К2"], ElementTags = ["101"], AngleDeg = 90,
            PhiMode = FemSwayParams.PhiEn1993, HeightM = 12.5, M = 3,
        };
        var s = FemSwayParams.Parse(sway.ToJson());
        Assert.Equal(sway.Scope, s.Scope);
        Assert.Equal(sway.MemberTags, s.MemberTags);
        Assert.Equal(sway.ElementTags, s.ElementTags);
        Assert.Equal((90.0, FemSwayParams.PhiEn1993, 12.5, 3), (s.AngleDeg, s.PhiMode, s.HeightM, s.M));
        Assert.Null(s.PhiInv);
        Assert.DoesNotContain("PhiInv", sway.ToJson());

        var bow = new FemBowParams
        {
            MemberTags = ["К1"], ElementChains = [["1", "2"], ["7"]], Plane = FemBowParams.PlaneZ, E0Mode = FemBowParams.E0Table,
            Curve = "c", Plastic = true, Sign = FemBowParams.SignMinus, Activity = FemBowParams.ActivityAlways,
        };
        var b = FemBowParams.Parse(bow.ToJson());
        Assert.Equal(bow.MemberTags, b.MemberTags);
        Assert.Equal(2, b.ElementChains.Count);
        Assert.Equal(["1", "2"], b.ElementChains[0]);
        Assert.Equal((FemBowParams.PlaneZ, FemBowParams.E0Table, "c", true, FemBowParams.SignMinus, FemBowParams.ActivityAlways),
            (b.Plane, b.E0Mode, b.Curve, b.Plastic, b.Sign, b.Activity));

        // Пустой JSON — значения по умолчанию: крен «вся схема» по +X, m = 1; погибь — y, L/e₀, знак «авто», п. 5.3.2(6).
        var d = FemSwayParams.Parse("");
        Assert.Equal((FemSwayParams.ScopeAll, 0.0, 1, FemSwayParams.PhiManual), (d.Scope, d.AngleDeg, d.M, d.PhiMode));
        var db = FemBowParams.Parse("{}");
        Assert.Equal((FemBowParams.PlaneY, FemBowParams.E0Relative, FemBowParams.SignAuto, FemBowParams.ActivityEn1993),
            (db.Plane, db.E0Mode, db.Sign, db.Activity));
        Assert.Equal("Крен #4", new FemImperfection { Id = 4, Kind = FemImperfectionKinds.Sway }.Label);
    }

    [Fact]
    public void Validator_ImperfectionType_IsKnown_AndLoadsAreErrors()
    {
        var schema = new FemSchema { Id = 1 };
        var imp = Case(2, "Imp", FemSp20Types.Imperfection);
        var cases = new[] { Case(1, "P", FemSp20Types.Permanent), imp };
        var nodes = new[] { new FemNode { Id = 5, SchemaId = 1, NodeTag = "5" }, new FemNode { Id = 6, SchemaId = 1, NodeTag = "6" } };
        var member = new FemMember { Id = 9, SchemaId = 1, ElemTag = "9", NodeIdsJson = "[5, 6]" };
        nodes[1].Z = 3;

        var clean = FemCanonicalValidator.Validate(schema, cases, nodes, [new FemNodeLoad { Id = 1, SchemaId = 1, LoadCaseId = 1, NodeId = 5, Fz = -1 }],
            [member], []);
        Assert.Empty(clean);

        var errors = FemCanonicalValidator.Validate(schema, cases, nodes,
            [new FemNodeLoad { Id = 1, SchemaId = 1, LoadCaseId = 2, NodeId = 5, Fz = -1 }], [member],
            [new FemMemberLoad { Id = 3, SchemaId = 1, LoadCaseId = 2, MemberId = 9, QzStart = -1, QzEnd = -1 }],
            [new FemKinematicLoad { Id = 4, SchemaId = 1, LoadCaseId = 2, NodeId = 6, Dof = 3, Value = 0.01 }]);
        Assert.Equal(3, errors.Count(e => e.Code == FemCanonicalValidator.ImperfectionHasLoadsCode && e.IsError));
        Assert.All(errors, e => Assert.Equal(FemCanonicalValidator.ImperfectionHasLoadsCode, e.Code));

        imp.SelfWeightFactor = 1;
        Assert.Contains(FemCanonicalValidator.Validate(schema, cases, nodes, []),
            e => e.Code == FemCanonicalValidator.ImperfectionHasLoadsCode && e.Message.Contains("собственный вес"));
    }

    [Fact]
    public void Sp20Adapter_SkipsImperfection_WithoutWarning()
    {
        var nodes = new[] { new FemNode { Id = 1, NodeTag = "1" } };
        var conv = FemSp20CombinationAdapter.ToLoadings(
            [Case(1, "P", FemSp20Types.Permanent), Case(2, "Imp", FemSp20Types.Imperfection)], nodes,
            [new FemNodeLoad { LoadCaseId = 1, NodeId = 1, Fz = -10 }], [1]);
        Assert.Empty(conv.Warnings);
        Assert.Single(conv.Loadings);
    }

    [Fact]
    public void AutoCoefficient_ImperfectionIsOne()
    {
        var imp = Case(2, "Imp", FemSp20Types.Imperfection);
        imp.GammaFUnfav = 1.5;
        foreach (var group in FemCombinationTypes.All)
        {
            var a = FemCombinationCoefficients.Auto(imp, Sp20TermRole.Leading, group, Sp20LoadValueKind.Normative, Sp20GammaDefaults.Standard);
            Assert.Equal(1.0, a.Value);
            Assert.Empty(a.Factors);
        }
    }

    [Fact]
    public void Expression_All_ExcludesImperfection_ExplicitTermKeepsFactor()
    {
        var cases = new[] { Case(2, "Imp", FemSp20Types.Imperfection), Case(1, "P"), Case(3, "V") };
        var all = FemLoadExpressionResolver.Terms(new FemLoadExpression { Mode = FemLoadExpressionMode.All }, cases);
        Assert.Equal([1, 3], all.Select(t => t.LoadCase.Id).Order());

        var single = FemLoadExpressionResolver.Terms(new FemLoadExpression { Mode = FemLoadExpressionMode.Single }, cases);
        Assert.Equal(1, Assert.Single(single).LoadCase.Id);

        var sum = FemLoadExpressionResolver.Terms(new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            Terms = [new FemLoadTerm { LoadCaseId = 1, Coefficient = 1.2 }, new FemLoadTerm { LoadCaseId = 2, Coefficient = -1 }],
        }, cases);
        Assert.Equal(-1.0, sum.Single(t => FemSp20Types.IsImperfection(t.LoadCase)).Factor);
    }
}
