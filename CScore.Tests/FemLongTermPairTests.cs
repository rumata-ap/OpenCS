using CScore.Combinations;
using CScore.Fem;
using CScore.Fem.Combinations;
using Xunit;

namespace CScore.Tests;

/// <summary>Длительная часть комбинации пакета CSfea (спека 2026-10-17, п. 2.1): k_long = k_total · s.</summary>
public sealed class FemLongTermPairTests
{
    static readonly Sp20GammaDefaults G = Sp20GammaDefaults.Standard;

    static readonly FemLoadCase Dead = new() { Id = 1, Tag = "ЗГ1 Вес", Sp20Type = "permanent" };
    static readonly FemLoadCase Partitions = new() { Id = 2, Tag = "ЗГ2 Перегородки", Sp20Type = "long_term" };
    static readonly FemLoadCase Live = new() { Id = 3, Tag = "ЗГ3 Полезная", Sp20Type = "short_term", LongTermShare = 0.5 };
    static readonly FemLoadCase Snow = new() { Id = 4, Tag = "ЗГ4 Снег", Sp20Type = "short_term" };
    static readonly FemLoadCase Blast = new() { Id = 5, Tag = "ЗГ5 Взрыв", Sp20Type = "accidental" };
    static readonly FemLoadCase Full = new() { Id = 6, Tag = "ЗГ6 Склад", Sp20Type = "short_term", LongTermShare = 1 };
    static readonly FemLoadCase[] Cases = [Dead, Partitions, Live, Snow, Blast, Full];

    static FemLoadTerm Manual(int id, double k) => new() { LoadCaseId = id, Coefficient = k, Mode = FemLoadTerm.ManualMode };

    static FemLoadExpression Sum(params FemLoadTerm[] terms) =>
        new() { Mode = FemLoadExpressionMode.Sum, Terms = terms.ToList(), CombinationType = "N" };

    [Fact]
    public void Derived_ShortTermByShare_TypeNL()
    {
        var r = FemLongTermPair.Derive(Sum(Manual(1, 1), Manual(3, 0.9)), Cases, "N");
        Assert.Equal(FemLongTermPairKind.Derived, r.Kind);
        Assert.Equal("NL", r.Long!.CombinationType);
        Assert.Equal([1.0, 0.45], r.Long.Terms.Select(t => t.Coefficient));
        Assert.All(r.Long.Terms, t => Assert.Equal(FemLoadTerm.ManualMode, t.Mode));
        Assert.Empty(r.Notes);
    }

    [Fact]
    public void SameAsTotal_WithoutShortTermOrShareOne()
    {
        var r = FemLongTermPair.Derive(Sum(Manual(1, 1.1), Manual(2, 1.2), Manual(6, 1.4)), Cases, "C");
        Assert.Equal(FemLongTermPairKind.SameAsTotal, r.Kind);
        Assert.Equal("CL", r.Long!.CombinationType);
        Assert.Equal([1.1, 1.2, 1.4], r.Long.Terms.Select(t => t.Coefficient));
    }

    [Fact]
    public void Zero_OnlyShortTermWithoutShare_NotedOnce()
    {
        var r = FemLongTermPair.Derive(Sum(Manual(4, 1), Manual(4, 0.5), Manual(5, 1)), Cases, "N");
        Assert.Equal(FemLongTermPairKind.Zero, r.Kind);
        Assert.Equal(["ЗГ4 Снег: доля длительной части не задана — в NL не входит (0)"], r.Notes);
    }

    [Fact]
    public void Invalid_MissingLoadCase()
    {
        var r = FemLongTermPair.Derive(Sum(Manual(1, 1), Manual(99, 1)), Cases, "N");
        Assert.Equal(FemLongTermPairKind.Invalid, r.Kind);
        Assert.Null(r.Long);
        Assert.Contains("№ 99", Assert.Single(r.Notes));
    }

    [Fact]
    public void LoadCaseIdsWithoutTerms_UnitCoefficients()
    {
        var e = new FemLoadExpression { Mode = FemLoadExpressionMode.Sum, LoadCaseIds = [1, 3] };
        var r = FemLongTermPair.Derive(e, Cases, "N");
        Assert.Equal([1.0, 0.5], r.Long!.Terms.Select(t => t.Coefficient));
        Assert.Equal(FemLongTermPairKind.Derived, r.Kind);
    }

    [Theory]
    [InlineData("N", "NL", Sp20LoadValueKind.Normative)]
    [InlineData("N", "NL", Sp20LoadValueKind.Design)]
    [InlineData("C", "CL", Sp20LoadValueKind.Normative)]
    public void AutoTerms_MatchRecompute(string type, string longType, Sp20LoadValueKind kind)
    {
        FemLoadTerm Auto(int id, Sp20TermRole role) => new()
        {
            LoadCaseId = id, Role = FemLoadTermRoles.ToKey(role), Mode = FemLoadTerm.AutoMode,
            Coefficient = FemCombinationCoefficients.Auto(Cases.First(c => c.Id == id), role, type, kind, G).Value,
        };
        var total = new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sp20, CombinationType = type,
            Terms = [Auto(1, Sp20TermRole.Permanent), Auto(2, Sp20TermRole.Leading), Auto(3, Sp20TermRole.Leading),
                Auto(4, Sp20TermRole.Second), Auto(5, Sp20TermRole.Accidental)],
        };
        var r = FemLongTermPair.Derive(total, Cases, type);
        var expected = FemCombinationCoefficients.Recompute(total, Cases, longType, kind, G);
        Assert.Equal(expected.Terms.Select(t => t.Coefficient), r.Long!.Terms.Select(t => t.Coefficient), new Tol());
        Assert.Equal(FemLoadExpressionMode.Sp20, r.Long.Mode);
    }

    [Theory]
    [InlineData("NL")]
    [InlineData("A")]
    [InlineData(null)]
    public void OnlyNAndC(string? type) =>
        Assert.Throws<ArgumentException>(() => FemLongTermPair.Derive(Sum(Manual(1, 1)), Cases, type!));

    sealed class Tol : IEqualityComparer<double>
    {
        public bool Equals(double a, double b) => Math.Abs(a - b) < 1e-12;
        public int GetHashCode(double v) => 0;
    }
}
