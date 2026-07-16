using CScore.Fem;
using CScore.Fem.Combinations;
using Xunit;

namespace CScore.Tests;

public sealed class FemSp20CombinationAdapterTests
{
    [Fact]
    public void ToLoadings_UsesExplicitSp20MetadataAndOverrides()
    {
        var loadCase = new FemLoadCase
        {
            Id = 1,
            Tag = "Полезная нагрузка",
            Sp20Type = "short_term",
            Sp20Group = "live",
            GammaFUnfav = 1.25
        };
        var node = new FemNode { Id = 10 };
        var nodeLoad = new FemNodeLoad { LoadCaseId = 1, NodeId = 10, Fz = 8 };

        var result = FemSp20CombinationAdapter.ToLoadings(
            [loadCase], [node], [nodeLoad], [10]);

        Assert.Empty(result.Warnings);
        Assert.Single(result.Loadings);
        Assert.Equal(1.25, result.Loadings[0].GammaFUnfav);
        Assert.Equal("Fx10", result.Loadings[0].ComponentNames[0]);
        Assert.Equal("live", result.Loadings[0].Group);
    }

    [Fact]
    public void BuildUnitSum_UsesCoefficientOneForEverySelectedCase()
    {
        var first = new FemLoadCase { Id = 1, Sp20Type = "permanent", Tag = "G" };
        var second = new FemLoadCase { Id = 2, Sp20Type = "short_term", Tag = "Q" };
        var node = new FemNode { Id = 10 };
        var loads = new[]
        {
            new FemNodeLoad { LoadCaseId = 1, NodeId = 10, Fz = 2 },
            new FemNodeLoad { LoadCaseId = 2, NodeId = 10, Fz = 3 }
        };

        var result = FemSp20CombinationAdapter.BuildUnitSum(
            [first, second], [node], loads, [10]);

        Assert.Equal(5, result.Vector[2]);
        Assert.Equal(1, result.Coefficients[1]);
        Assert.Equal(1, result.Coefficients[2]);
    }

    [Fact]
    public void ToLoadings_RejectsUnknownSp20TypeWithWarning()
    {
        var result = FemSp20CombinationAdapter.ToLoadings(
            [new FemLoadCase { Id = 1, Tag = "bad", Sp20Type = "unknown" }],
            [new FemNode { Id = 10 }],
            [],
            [10]);

        Assert.Empty(result.Loadings);
        Assert.Contains(result.Warnings, warning => warning.Contains("unknown", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildSp20_DeduplicatesActiveFormulasAndRebuildsVectors()
    {
        var loadCases = new[]
        {
            new FemLoadCase { Id = 1, Tag = "G", Sp20Type = "permanent" },
            new FemLoadCase { Id = 2, Tag = "Q", Sp20Type = "short_term" }
        };
        var combinations = FemSp20CombinationAdapter.BuildSp20(
            loadCases,
            [new FemNode { Id = 10 }],
            [
                new FemNodeLoad { LoadCaseId = 1, NodeId = 10, Fz = 2 },
                new FemNodeLoad { LoadCaseId = 2, NodeId = 10, Fz = 3 }
            ],
            [10]);

        Assert.NotEmpty(combinations);
        Assert.All(combinations, combination => Assert.Equal(6, combination.Vector.Length));
        Assert.Equal(
            combinations.Count,
            combinations.Select(combination => string.Join("|", combination.Active.OrderBy(pair => pair.Key)))
                .Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(combinations, combination => combination.Vector[2] > 0);
    }
}
