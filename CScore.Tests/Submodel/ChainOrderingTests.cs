using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class ChainOrderingTests
{
    static StraightBeamChain Order(IReadOnlyList<BeamSegmentInput> segments, PlanarVector3? preferred = null)
    {
        var tolerances = ChainTolerances.Default.Resolve(SegmentInputValidation.CharacteristicSize(segments));
        var clusters = NodeClusterBuilder.Build(segments, tolerances.NodeCoincidenceM);
        var component = SegmentGraph.Build(segments, clusters).Single();
        var straightness = ChainStraightnessCheck.Check(segments, clusters, component, tolerances);
        return ChainOrdering.OrderByTraversal(segments, clusters, component, straightness, preferred, tolerances);
    }

    [Fact]
    public void OrderByTraversal_ReturnsSegmentsFromStartToEnd()
    {
        var chain = Order([RigidTransformFixture.Segment("b", 1, 2), RigidTransformFixture.Segment("c", 2, 3), RigidTransformFixture.Segment("a", 0, 1)]);
        Assert.Equal(["a", "b", "c"], chain.Segments.Select(s => s.Source.SourceKey));
        Assert.Equal(4, chain.Nodes.Count);
        Assert.Equal(3.0, chain.LengthM, 6);
    }

    [Fact]
    public void OrderByTraversal_MarksReversedSegments()
    {
        var chain = Order([RigidTransformFixture.Segment("a", 0, 1), RigidTransformFixture.Segment("b", 2, 1), RigidTransformFixture.Segment("c", 2, 3)]);
        Assert.False(chain.Segments[0].IsReversed);
        Assert.True(chain.Segments[1].IsReversed);
        Assert.False(chain.Segments[2].IsReversed);
    }

    [Fact]
    public void OrderByTraversal_StationsAreMonotonic()
    {
        var chain = Order([RigidTransformFixture.Segment("a", 0, 1), RigidTransformFixture.Segment("b", 1, 2)]);
        Assert.Equal(0.0, chain.Nodes[0].Station, 6);
        Assert.Equal(1.0, chain.Nodes[1].Station, 6);
        Assert.Equal(2.0, chain.Nodes[2].Station, 6);
        Assert.True(chain.Nodes[0].IsEnd);
        Assert.True(chain.Nodes[^1].IsEnd);
        Assert.False(chain.Nodes[1].IsEnd);
    }

    [Fact]
    public void OrderByTraversal_PreferredDirectionWins()
    {
        var segments = new List<BeamSegmentInput> { RigidTransformFixture.Segment("a", 0, 1), RigidTransformFixture.Segment("b", 1, 2) };
        var forward = Order(segments, new PlanarVector3(1, 0, 0));
        var backward = Order(segments, new PlanarVector3(-1, 0, 0));
        Assert.Equal("a", forward.Segments[0].Source.SourceKey);
        Assert.Equal("b", backward.Segments[0].Source.SourceKey);
        Assert.True(backward.Segments[0].IsReversed);
    }

    [Fact]
    public void OrderByTraversal_WithoutPreferredDirection_StartsAtLexicographicallySmallerEnd() =>
        Assert.Equal(0.0, Order([RigidTransformFixture.Segment("a", 0, 1), RigidTransformFixture.Segment("b", 1, 2)]).Nodes[0].Point.X, 6);

    [Fact]
    public void OrderByStation_SortsDisconnectedSegmentsAlongAxis()
    {
        var segments = new List<BeamSegmentInput> { RigidTransformFixture.Segment("far", 1.5, 2.5), RigidTransformFixture.Segment("near", 0, 1) };
        var tolerances = ChainTolerances.Default.Resolve(SegmentInputValidation.CharacteristicSize(segments));
        var clusters = NodeClusterBuilder.Build(segments, tolerances.NodeCoincidenceM);
        var chain = ChainOrdering.OrderByStation(segments, clusters, [0, 1], new PlanarVector3(0, 0, 0), new PlanarVector3(1, 0, 0), tolerances);
        Assert.Equal(["near", "far"], chain.Segments.Select(s => s.Source.SourceKey));
        Assert.Equal(2.5, chain.LengthM, 6);
    }
}
