using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class SegmentGraphTests
{
    const double Tol = 0.001;

    static IReadOnlyList<SegmentComponent> Build(IReadOnlyList<BeamSegmentInput> segments) =>
        SegmentGraph.Build(segments, NodeClusterBuilder.Build(segments, Tol));

    [Fact]
    public void Build_SingleSegment_IsSimpleChainWithTwoEnds()
    {
        var components = Build([RigidTransformFixture.Segment("a", 0, 1)]);
        Assert.Single(components);
        Assert.True(components[0].IsSimpleChain);
        Assert.Equal(2, components[0].EndClusters.Count);
    }

    [Fact]
    public void Build_StraightChain_IsSimpleChain()
    {
        var components = Build([
            RigidTransformFixture.Segment("a", 0, 1),
            RigidTransformFixture.Segment("b", 1, 2),
            RigidTransformFixture.Segment("c", 2, 3)]);
        Assert.Single(components);
        Assert.True(components[0].IsSimpleChain);
        Assert.Equal(3, components[0].SegmentIndices.Count);
    }

    [Fact]
    public void Build_Branching_IsRejectedWithDiagnostic()
    {
        var segments = new List<BeamSegmentInput>
        {
            RigidTransformFixture.Segment("a", 0, 1),
            RigidTransformFixture.Segment("b", 1, 2),
            new("c", new PlanarVector3(1, 0, 0), new PlanarVector3(1, 1, 0), 0, BetaSource.Member, "m1")
        };
        var component = Build(RigidTransformFixture.Apply(segments)).Single();
        Assert.False(component.IsSimpleChain);
        Assert.Contains(component.Diagnostics, d => d.Code == "chain_branching" && d.IsError);
    }

    [Fact]
    public void Build_Cycle_IsRejectedWithDiagnostic()
    {
        var segments = new List<BeamSegmentInput>
        {
            new("a", new PlanarVector3(0, 0, 0), new PlanarVector3(1, 0, 0), 0, BetaSource.Member, "m1"),
            new("b", new PlanarVector3(1, 0, 0), new PlanarVector3(1, 1, 0), 0, BetaSource.Member, "m1"),
            new("c", new PlanarVector3(1, 1, 0), new PlanarVector3(0, 0, 0), 0, BetaSource.Member, "m1")
        };
        var component = Build(RigidTransformFixture.Apply(segments)).Single();
        Assert.False(component.IsSimpleChain);
        Assert.Contains(component.Diagnostics, d => d.Code == "chain_cycle" && d.IsError);
    }

    [Fact]
    public void Build_DuplicateSegment_IsRejectedWithDiagnostic()
    {
        var component = Build([
            RigidTransformFixture.Segment("a", 0, 1),
            RigidTransformFixture.Segment("a_copy", 0, 1)]).Single();
        Assert.False(component.IsSimpleChain);
        Assert.Contains(component.Diagnostics, d => d.Code == "chain_duplicate_segment" && d.IsError);
    }

    [Fact]
    public void Build_SeparateComponents_AreReturnedIndependently()
    {
        var components = Build([
            RigidTransformFixture.Segment("a", 0, 1),
            RigidTransformFixture.Segment("b", 1, 2),
            RigidTransformFixture.Segment("far1", 50, 51),
            RigidTransformFixture.Segment("far2", 51, 52)]);
        Assert.Equal(2, components.Count);
        Assert.All(components, c => Assert.True(c.IsSimpleChain));
    }
}
