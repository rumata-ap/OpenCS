using System.Globalization;
using CScore.Combinations;
using CScore.Fem;
using CScore.Fem.Combinations;
using Xunit;

namespace CScore.Tests;

/// <summary>«Авто»-коэффициенты слагаемых комбинаций (спека 2026-10-14, п. 5.3–5.4) и совместимость JSON формул.</summary>
public sealed class FemCombinationCoefficientsTests
{
    static readonly Sp20GammaDefaults G = Sp20GammaDefaults.Standard;
    static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    static FemLoadCase Case(string type, double? gamma = null, double? share = null) =>
        new() { Id = 1, Tag = "ЗГ", Sp20Type = type, GammaFUnfav = gamma, LongTermShare = share };

    [Theory]
    // Основное, нормативные: ×γf·ψ.
    [InlineData("short_term", Sp20TermRole.Second, "C", Sp20LoadValueKind.Normative, 1.4 * 0.9, "1,4·0,9 = 1,26")]
    [InlineData("short_term", Sp20TermRole.Leading, "C", Sp20LoadValueKind.Normative, 1.4, "1,4")]
    [InlineData("long_term", Sp20TermRole.Other, "C", Sp20LoadValueKind.Normative, 1.2 * 0.95, "1,2·0,95 = 1,14")]
    [InlineData("permanent", Sp20TermRole.Permanent, "C", Sp20LoadValueKind.Normative, 1.1, "1,1")]
    [InlineData("permanent", Sp20TermRole.PermanentReduced, "C", Sp20LoadValueKind.Normative, 0.9, "0,9")]
    // Основное, расчётные: ×ψ, п. 7.3 — ×0,9÷γf.
    [InlineData("short_term", Sp20TermRole.Second, "C", Sp20LoadValueKind.Design, 0.9, "0,9")]
    [InlineData("permanent", Sp20TermRole.PermanentReduced, "C", Sp20LoadValueKind.Design, 0.9 / 1.1, "0,9÷1,1 = 0,818")]
    // ПС2: нормативные ×ψ, расчётные ÷γf·ψ.
    [InlineData("short_term", Sp20TermRole.Second, "N", Sp20LoadValueKind.Normative, 0.9, "0,9")]
    [InlineData("short_term", Sp20TermRole.Second, "N", Sp20LoadValueKind.Design, 0.9 / 1.4, "÷1,4·0,9 = 0,643")]
    [InlineData("permanent", Sp20TermRole.PermanentReduced, "N", Sp20LoadValueKind.Normative, 1.0, "1")]
    // Особое: кратковременные γf 0,5 и ψ 0,8, остальные γf 1.
    [InlineData("short_term", Sp20TermRole.Leading, "A", Sp20LoadValueKind.Normative, 0.4, "0,5·0,8 = 0,4")]
    [InlineData("accidental", Sp20TermRole.Accidental, "A", Sp20LoadValueKind.Normative, 1.0, "1")]
    [InlineData("permanent", Sp20TermRole.Permanent, "A", Sp20LoadValueKind.Design, 1 / 1.1, "÷1,1 = 0,909")]
    // Особая нагрузка вне особого сочетания не входит.
    [InlineData("accidental", Sp20TermRole.Accidental, "C", Sp20LoadValueKind.Normative, 0.0, "0")]
    public void Auto_ByGroupKindAndRole(string type, Sp20TermRole role, string group, Sp20LoadValueKind kind,
        double expected, string text)
    {
        var auto = FemCombinationCoefficients.Auto(Case(type), role, group, kind, G);
        Assert.Equal(expected, auto.Value, 12);
        Assert.Equal(text, auto.Format(Ru));
    }

    [Fact]
    public void Auto_LongTermGroups_ShortTermByShare_AccidentalExcluded()
    {
        var snow = Case("short_term", share: 0.5);
        Assert.Equal(1.4 * 0.9 * 0.5, FemCombinationCoefficients.Auto(snow, Sp20TermRole.Second, "CL",
            Sp20LoadValueKind.Normative, G).Value, 12);
        Assert.Equal(0.5, FemCombinationCoefficients.Auto(snow, Sp20TermRole.Leading, "NL",
            Sp20LoadValueKind.Normative, G).Value, 12);
        // Доля не задана — 0.
        Assert.Equal(0, FemCombinationCoefficients.Auto(Case("short_term"), Sp20TermRole.Leading, "NL",
            Sp20LoadValueKind.Normative, G).Value);
        // Длительные — целиком.
        Assert.Equal(1.2, FemCombinationCoefficients.Auto(Case("long_term"), Sp20TermRole.Leading, "CL",
            Sp20LoadValueKind.Normative, G).Value, 12);
        Assert.Equal(0, FemCombinationCoefficients.Auto(Case("accidental"), Sp20TermRole.Accidental, "NL",
            Sp20LoadValueKind.Normative, G).Value);
    }

    [Fact]
    public void Auto_UsesLoadCaseGammaAndDefaultsFromSettings()
    {
        var defaults = new Sp20GammaDefaults { ShortTermUnfav = 1.3 };
        Assert.Equal(1.3, FemCombinationCoefficients.Auto(Case("short_term"), Sp20TermRole.Leading, "C",
            Sp20LoadValueKind.Normative, defaults).Value, 12);
        Assert.Equal(1.25, FemCombinationCoefficients.Auto(Case("short_term", gamma: 1.25), Sp20TermRole.Leading, "C",
            Sp20LoadValueKind.Normative, defaults).Value, 12);
    }

    [Fact]
    public void DefaultRole_RanksByLoadTypeInCombination()
    {
        var g = new FemLoadCase { Id = 1, Sp20Type = "permanent" };
        var q1 = new FemLoadCase { Id = 2, Sp20Type = "short_term" };
        var q2 = new FemLoadCase { Id = 3, Sp20Type = "short_term" };
        var q3 = new FemLoadCase { Id = 4, Sp20Type = "short_term" };
        var l1 = new FemLoadCase { Id = 5, Sp20Type = "long_term" };
        Assert.Equal(Sp20TermRole.Permanent, FemCombinationCoefficients.DefaultRole(g, []));
        Assert.Equal(Sp20TermRole.Leading, FemCombinationCoefficients.DefaultRole(q1, [g]));
        Assert.Equal(Sp20TermRole.Second, FemCombinationCoefficients.DefaultRole(q2, [g, q1]));
        Assert.Equal(Sp20TermRole.Other, FemCombinationCoefficients.DefaultRole(q3, [g, q1, q2]));
        Assert.Equal(Sp20TermRole.Leading, FemCombinationCoefficients.DefaultRole(l1, [g, q1, q2]));
    }

    [Fact]
    public void Recompute_UpdatesOnlyAutoTerms()
    {
        var cases = new[]
        {
            new FemLoadCase { Id = 1, Sp20Type = "permanent" },
            new FemLoadCase { Id = 2, Sp20Type = "short_term", LongTermShare = 0.35 },
        };
        var expression = new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            Terms =
            [
                new FemLoadTerm { LoadCaseId = 1, Coefficient = 7, Role = FemLoadTermRoles.Permanent, Mode = FemLoadTerm.AutoMode },
                new FemLoadTerm { LoadCaseId = 2, Coefficient = 0.33 },
            ],
        };
        var nl = FemCombinationCoefficients.Recompute(expression, cases, "NL", Sp20LoadValueKind.Normative, G);
        Assert.Equal(1.0, nl.Terms[0].Coefficient);
        Assert.Equal(0.33, nl.Terms[1].Coefficient);
        var c = FemCombinationCoefficients.Recompute(expression, cases, "C", Sp20LoadValueKind.Normative, G);
        Assert.Equal(1.1, c.Terms[0].Coefficient);
    }

    [Fact]
    public void TermJson_OldFormatUnchanged_NewFieldsRoundTrip()
    {
        var old = new FemLoadExpression { Mode = FemLoadExpressionMode.Sum, Terms = [new FemLoadTerm { LoadCaseId = 3, Coefficient = 1.2 }] };
        Assert.DoesNotContain("Role", old.ToJson());
        Assert.DoesNotContain("IsAuto", old.ToJson());
        var parsed = FemLoadExpression.Parse("""{"Mode":1,"Terms":[{"LoadCaseId":3,"Coefficient":1.2}]}""");
        Assert.False(parsed.Terms[0].IsAuto);
        Assert.Null(parsed.Terms[0].Mode);

        var auto = new FemLoadExpression
        {
            Terms = [new FemLoadTerm { LoadCaseId = 3, Coefficient = 1.26, Role = FemLoadTermRoles.Second, Mode = FemLoadTerm.AutoMode }],
        };
        var back = FemLoadExpression.Parse(auto.ToJson()).Terms[0];
        Assert.True(back.IsAuto);
        Assert.Equal(Sp20TermRole.Second, FemLoadTermRoles.Parse(back.Role));
    }

    [Theory]
    [InlineData(null, "C")]
    [InlineData("fundamental", "C")]
    [InlineData("accidental", "A")]
    [InlineData("NL", "NL")]
    public void CombinationType_Normalize(string? stored, string expected) =>
        Assert.Equal(expected, FemCombinationTypes.Normalize(stored));

    [Fact]
    public void CreateSp20_WritesAutoTermsWithRolesAndTypeC()
    {
        var schema = new FemSchema { Id = 5 };
        var cases = new[]
        {
            new FemLoadCase { Id = 10, SchemaId = 5, Tag = "G", Sp20Type = "permanent" },
            new FemLoadCase { Id = 11, SchemaId = 5, Tag = "Q", Sp20Type = "short_term" },
        };
        var loads = new[]
        {
            new FemNodeLoad { SchemaId = 5, LoadCaseId = 10, NodeId = 1, Fz = -10 },
            new FemNodeLoad { SchemaId = 5, LoadCaseId = 11, NodeId = 1, Fz = -5 },
        };
        var definitions = FemLoadDefinitionFactory.CreateSp20(schema, cases, [new FemNode { Id = 1 }], loads,
            "fundamental", gammaDefaults: new Sp20GammaDefaults { ShortTermUnfav = 1.3 });
        Assert.All(definitions, d => Assert.Equal("C", d.CombinationType));
        var full = definitions.Select(d => d.GetExpression()).Single(e => e.Terms.Count == 2);
        Assert.All(full.Terms, t => Assert.True(t.IsAuto));
        Assert.Equal(1.3, full.Terms.Single(t => t.LoadCaseId == 11).Coefficient, 12);

        var design = FemLoadDefinitionFactory.CreateSp20(schema, cases, [new FemNode { Id = 1 }], loads,
            "fundamental", valueKind: Sp20LoadValueKind.Design);
        Assert.Equal(1.0, design.Select(d => d.GetExpression()).Single(e => e.Terms.Count == 2)
            .Terms.Single(t => t.LoadCaseId == 11).Coefficient, 12);
    }
}
