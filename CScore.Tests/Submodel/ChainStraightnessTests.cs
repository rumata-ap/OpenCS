using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class ChainStraightnessTests
{
    static StraightnessResult Check(IReadOnlyList<BeamSegmentInput> segments)
    {
        var rotated = RigidTransformFixture.Apply(segments);
        var tolerances = ChainTolerances.Default.Resolve(SegmentInputValidation.CharacteristicSize(rotated));
        var clusters = NodeClusterBuilder.Build(rotated, tolerances.NodeCoincidenceM);
        return ChainStraightnessCheck.Check(rotated, clusters, SegmentGraph.Build(rotated, clusters).Single(), tolerances);
    }

    [Fact]
    public void Check_StraightChain_IsStraightWithUnitAxis()
    {
        var result = Check([RigidTransformFixture.Segment("a", 0, 1), RigidTransformFixture.Segment("b", 1, 2), RigidTransformFixture.Segment("c", 2, 3)]);
        Assert.True(result.IsStraight);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(1.0, result.AxisDirection.Length, 9);
    }

    [Fact]
    public void Check_ReversedSegment_IsStillStraight()
    {
        var result = Check([RigidTransformFixture.Segment("a", 0, 1), RigidTransformFixture.Segment("b", 2, 1), RigidTransformFixture.Segment("c", 2, 3)]);
        Assert.True(result.IsStraight);
        Assert.Equal(0.0, result.MaxAngleDeg, 6);
    }

    [Fact]
    public void Check_SmallOffsetWithinTolerance_IsStraight()
    {
        var result = Check([
            new("a", new PlanarVector3(0, 0, 0), new PlanarVector3(1, 0.001, 0), 0, BetaSource.Member, "m1"),
            new("b", new PlanarVector3(1, 0.001, 0), new PlanarVector3(2, 0, 0), 0, BetaSource.Member, "m1")]);
        Assert.True(result.IsStraight);
        Assert.InRange(result.MaxNodeOffsetM, 0.0009, 0.0011);
    }

    [Fact]
    public void Check_KinkBeyondTolerance_IsNotStraight()
    {
        var result = Check([
            new("a", new PlanarVector3(0, 0, 0), new PlanarVector3(1, 0.2, 0), 0, BetaSource.Member, "m1"),
            new("b", new PlanarVector3(1, 0.2, 0), new PlanarVector3(2, 0, 0), 0, BetaSource.Member, "m1")]);
        Assert.False(result.IsStraight);
        Assert.Contains(result.Diagnostics, d => d.Code == "chain_not_straight" && d.IsError);
    }

    [Fact]
    public void Check_FineDiscretizationWithMillimeterRounding_IsNotFalselyRejected()
    {
        var segments = new List<BeamSegmentInput>();
        for (var i = 0; i < 20; i++)
        {
            var y0 = Math.Round(i % 2 == 0 ? 0.0008 : -0.0008, 3);
            var y1 = Math.Round(i % 2 == 0 ? -0.0008 : 0.0008, 3);
            segments.Add(new($"s{i}", new PlanarVector3(i * .25, y0, 0), new PlanarVector3((i + 1) * .25, y1, 0), 0, BetaSource.Member, "m1"));
        }
        var result = Check(segments);
        Assert.True(result.IsStraight, string.Join(" | ", result.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Check_OverlappingProjections_ProduceOverlapDiagnostic()
    {
        var result = Check([
            new("ac", new PlanarVector3(1, 0, 0), new PlanarVector3(0, 0, 0), 0, BetaSource.Member, "m1"),
            new("ab", new PlanarVector3(1, 0, 0), new PlanarVector3(.4, 0, 0), 0, BetaSource.Member, "m1")]);
        Assert.Contains(result.Diagnostics, d => d.Code == "chain_overlap" && d.IsError);
        Assert.NotNull(result.MaxOverlapKey);
    }

    [Fact]
    public void Check_MetricsAreInvariantUnderRigidMotion()
    {
        var segments = new List<BeamSegmentInput>
        {
            new("a", new PlanarVector3(0, 0, 0), new PlanarVector3(1, .001, 0), 0, BetaSource.Member, "m1"),
            new("b", new PlanarVector3(1, .001, 0), new PlanarVector3(2, 0, 0), 0, BetaSource.Member, "m1")
        };
        var tolerances = ChainTolerances.Default.Resolve(SegmentInputValidation.CharacteristicSize(segments));
        var clusters = NodeClusterBuilder.Build(segments, tolerances.NodeCoincidenceM);
        var direct = ChainStraightnessCheck.Check(segments, clusters, SegmentGraph.Build(segments, clusters).Single(), tolerances);
        var rotated = Check(segments);
        Assert.Equal(direct.MaxNodeOffsetM, rotated.MaxNodeOffsetM, 9);
        Assert.Equal(direct.MaxAngleDeg, rotated.MaxAngleDeg, 9);
    }
}
