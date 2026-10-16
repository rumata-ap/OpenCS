using CScore.Combinations;
using Xunit;

namespace CScore.Tests;

/// <summary>ψ по рангу (п. 6.3–6.5 СП 20.13330.2016) и один проход основного сочетания по формуле (6.1).</summary>
public sealed class CombRulesTests
{
    [Theory]
    [InlineData(NormLoadType.LongTerm, Sp20TermRole.Leading, CombType.Fundamental, 1.0)]
    [InlineData(NormLoadType.LongTerm, Sp20TermRole.Second, CombType.Fundamental, 0.95)]
    [InlineData(NormLoadType.LongTerm, Sp20TermRole.Other, CombType.Accidental, 0.95)]
    [InlineData(NormLoadType.ShortTerm, Sp20TermRole.Leading, CombType.Fundamental, 1.0)]
    [InlineData(NormLoadType.ShortTerm, Sp20TermRole.Second, CombType.Fundamental, 0.9)]
    [InlineData(NormLoadType.ShortTerm, Sp20TermRole.Other, CombType.Fundamental, 0.7)]
    [InlineData(NormLoadType.ShortTerm, Sp20TermRole.Leading, CombType.Accidental, 0.8)]
    [InlineData(NormLoadType.Permanent, Sp20TermRole.Permanent, CombType.Fundamental, 1.0)]
    [InlineData(NormLoadType.Accidental, Sp20TermRole.Accidental, CombType.Accidental, 1.0)]
    public void Psi_ByTypeRankAndCombination(NormLoadType type, Sp20TermRole role, CombType comb, double expected) =>
        Assert.Equal(expected, CombRules.Psi(type, role, comb));

    [Fact]
    public void Fundamental_ShortTermsGetPsi1_EvenWithLongTerms()
    {
        // Одно сечение, одна компонента: G = 10, L = 5, Q1 = 4, Q2 = 3, Q3 = 2.
        var loadings = new List<Loading>
        {
            Loading.Permanent("G", M(10)), Loading.LongTerm("L", M(5)),
            Loading.ShortTerm("Q1", M(4)), Loading.ShortTerm("Q2", M(3)), Loading.ShortTerm("Q3", M(2)),
        };
        var (env, cases) = new Combinator(loadings, CombRules.SP20Fundamental()).FullEnvelopeWithCases();

        // Один проход: по случаю на (сечение, компонента, знак) — 6 компонент × 2 знака.
        Assert.Equal(12, cases.Count);
        var max = cases.Single(c => c.Component == "N" && c.Sign == +1);
        Assert.Equal(1.4 * 1.0, max.Active["Q1"], 12);
        Assert.Equal(1.4 * 0.9, max.Active["Q2"], 12);
        Assert.Equal(1.4 * 0.7, max.Active["Q3"], 12);
        Assert.Equal(1.2 * 1.0, max.Active["L"], 12);
        Assert.Equal(Sp20TermRole.Leading, max.Roles["Q1"]);
        Assert.Equal(Sp20TermRole.Second, max.Roles["Q2"]);
        Assert.Equal(Sp20TermRole.Other, max.Roles["Q3"]);
        Assert.Equal(Sp20TermRole.Permanent, max.Roles["G"]);
        Assert.Equal(1.1 * 10 + 1.2 * 5 + 1.4 * (4 + 0.9 * 3 + 0.7 * 2), env.MaxValues[0, 0], 9);

        // Минимум: вес разгружает — пониженный γf п. 7.3, временные не входят.
        var min = cases.Single(c => c.Component == "N" && c.Sign == -1);
        Assert.Equal(0.9, min.Active["G"], 12);
        Assert.Equal(Sp20TermRole.PermanentReduced, min.Roles["G"]);
        Assert.Single(min.Active);
    }

    [Fact]
    public void Accidental_ShortTermsGammaHalfPsi08()
    {
        var loadings = new List<Loading>
        {
            Loading.Permanent("G", M(10)), Loading.LongTerm("L", M(5)), Loading.ShortTerm("Q", M(4)),
            Loading.Accidental("A", M(7)),
        };
        var (_, cases) = new Combinator(loadings, CombRules.SP20Accidental()).FullEnvelopeWithCases();
        var max = cases.Single(c => c.Component == "N" && c.Sign == +1);
        Assert.Equal(1.0, max.Active["G"]);
        Assert.Equal(1.0, max.Active["L"]);
        Assert.Equal(0.5 * 0.8, max.Active["Q"], 12);
        Assert.Equal(1.0, max.Active["A"]);
        Assert.Equal(Sp20TermRole.Accidental, max.Roles["A"]);
    }

    static double[,] M(double v) => new[,] { { v, 0, 0, 0, 0, 0 } };
}
