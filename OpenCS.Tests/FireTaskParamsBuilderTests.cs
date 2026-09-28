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
            snapshotTimeMin: 60.0,
            method: "fiber");

        var parsed = FireTaskParamsBuilder.Parse("fire_r_check", json);

        Assert.Equal(12, parsed.FireSectionId);
        Assert.Equal(60.0, parsed.SnapshotTimeMin);
        Assert.Equal(-1, parsed.SnapshotIndex);
        Assert.Equal("fiber", parsed.Method);
        Assert.DoesNotContain("thermal_result_id", json);
    }

    [Fact]
    public void Build_EndOfFire_OmitsTime()
    {
        string json = FireTaskParamsBuilder.Build("fire_r_check", 12, null, "fiber");

        Assert.DoesNotContain("snapshot_time_min", json);
        Assert.Null(FireTaskParamsBuilder.Parse("fire_r_check", json).SnapshotTimeMin);
    }

    [Fact]
    public void Parse_LegacyJsonWithThermalResultId_IsAccepted()
    {
        var parsed = FireTaskParamsBuilder.Parse("fire_r_check",
            "{\"fire_section_id\":4,\"thermal_result_id\":47,\"method\":\"fiber\",\"snapshot_index\":3}");

        Assert.Equal(4, parsed.FireSectionId);
        Assert.Equal(3, parsed.SnapshotIndex);
        Assert.Null(parsed.SnapshotTimeMin);
    }

    [Fact]
    public void Parse_EmptyJson_ReturnsDefaultsWithoutThrowing()
    {
        var parsed = FireTaskParamsBuilder.Parse("fire_r_check", "{}");

        Assert.Equal(0, parsed.FireSectionId);
        Assert.Null(parsed.SnapshotTimeMin);
        Assert.Equal(-1, parsed.SnapshotIndex);
        Assert.Equal("fiber", parsed.Method);
    }

    [Fact]
    public void Build_ForRTime_ForcesEndOfFire()
    {
        string json = FireTaskParamsBuilder.Build(
            kind: "fire_r_time",
            fireSectionId: 3,
            snapshotTimeMin: 30.0,
            method: "fiber");

        var parsed = FireTaskParamsBuilder.Parse("fire_r_time", json);

        Assert.Null(parsed.SnapshotTimeMin);
        Assert.Equal(-1, parsed.SnapshotIndex);
    }
}
