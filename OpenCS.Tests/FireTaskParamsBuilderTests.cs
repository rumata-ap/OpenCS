using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

public sealed class FireTaskParamsBuilderTests
{
    [Theory]
    [InlineData("fire_r_check", true, true)]
    [InlineData("fire_r_check_batch", true, false)]
    [InlineData("fire_r_time", true, true)]
    [InlineData("fire_thermal_curvature", false, false)]
    [InlineData("strain_state", false, false)]
    public void ForceRequirements_MatchContractMatrix(string kind, bool needsSet, bool needsItem)
    {
        Assert.Equal(needsSet, FireTaskParamsBuilder.NeedsForceSet(kind));
        Assert.Equal(needsItem, FireTaskParamsBuilder.NeedsForceItem(kind));
    }

    [Fact]
    public void IsFireKind_RecognizesAllFourKinds()
    {
        Assert.True(FireTaskParamsBuilder.IsFireKind("fire_r_check"));
        Assert.True(FireTaskParamsBuilder.IsFireKind("fire_r_check_batch"));
        Assert.True(FireTaskParamsBuilder.IsFireKind("fire_r_time"));
        Assert.True(FireTaskParamsBuilder.IsFireKind("fire_thermal_curvature"));
        Assert.False(FireTaskParamsBuilder.IsFireKind("cracking"));
    }

    [Fact]
    public void BuildThenParse_PreservesEveryField()
    {
        string json = FireTaskParamsBuilder.Build(
            kind: "fire_r_check",
            fireSectionId: 12,
            thermalResultId: 47,
            snapshotIndex: 5,
            method: "fiber");

        var parsed = FireTaskParamsBuilder.Parse("fire_r_check", json);

        Assert.Equal(12, parsed.FireSectionId);
        Assert.Equal(47, parsed.ThermalResultId);
        Assert.Equal(5, parsed.SnapshotIndex);
        Assert.Equal("fiber", parsed.Method);
    }

    [Fact]
    public void Parse_EmptyJson_ReturnsDefaultsWithoutThrowing()
    {
        var parsed = FireTaskParamsBuilder.Parse("fire_r_check", "{}");

        Assert.Equal(0, parsed.FireSectionId);
        Assert.Equal(0, parsed.ThermalResultId);
        Assert.Equal(-1, parsed.SnapshotIndex);
        Assert.Equal("fiber", parsed.Method);
    }

    [Fact]
    public void Build_ForRTime_ForcesEndOfFireSnapshot()
    {
        string json = FireTaskParamsBuilder.Build(
            kind: "fire_r_time",
            fireSectionId: 3,
            thermalResultId: 9,
            snapshotIndex: 4,
            method: "fiber");

        var parsed = FireTaskParamsBuilder.Parse("fire_r_time", json);

        Assert.Equal(-1, parsed.SnapshotIndex);
    }
}
