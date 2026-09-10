using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class NodeClusterBuilderTests
{
    const double Tol = 0.001;

    [Fact]
    public void Build_MergesTouchingEnds_IntoSingleCluster()
    {
        var segments = new List<BeamSegmentInput>
        {
            RigidTransformFixture.Segment("a", 0, 1),
            RigidTransformFixture.Segment("b", 1, 2)
        };

        var set = NodeClusterBuilder.Build(segments, Tol);

        Assert.Equal(3, set.Clusters.Count);
        Assert.Equal(set.EndCluster[0], set.StartCluster[1]);
        Assert.False(set.HasAmbiguous);
    }

    [Fact]
    public void Build_IsInvariantUnderRigidMotion()
    {
        var segments = new List<BeamSegmentInput>
        {
            RigidTransformFixture.Segment("a", 0, 1),
            RigidTransformFixture.Segment("b", 1, 2)
        };

        var direct = NodeClusterBuilder.Build(segments, Tol);
        var rotated = NodeClusterBuilder.Build(RigidTransformFixture.Apply(segments), Tol);

        Assert.Equal(direct.Clusters.Count, rotated.Clusters.Count);
        Assert.Equal(direct.StartCluster, rotated.StartCluster);
        Assert.Equal(direct.EndCluster, rotated.EndCluster);
    }

    [Fact]
    public void Build_KeepsEndsApart_WhenDistanceExceedsTolerance()
    {
        var set = NodeClusterBuilder.Build(
            [RigidTransformFixture.Segment("a", 0, 1), RigidTransformFixture.Segment("b", 1.05, 2)], Tol);

        Assert.Equal(4, set.Clusters.Count);
        Assert.NotEqual(set.EndCluster[0], set.StartCluster[1]);
    }

    [Fact]
    public void Build_ReportsAmbiguousCluster_WhenChainedMergesExceedDoubleTolerance()
    {
        Assert.False(NodeClusterBuilder.Build(ThreeEnds(0.0008), Tol).HasAmbiguous);
        Assert.False(NodeClusterBuilder.Build(ThreeEnds(0.0012), Tol).HasAmbiguous);

        var segments = new List<BeamSegmentInput>();
        for (var i = 0; i < 6; i++)
        {
            var x = i * 0.0008;
            segments.Add(new($"s{i}", new PlanarVector3(x, 0, 0), new PlanarVector3(x, 5, 0),
                0, BetaSource.Member, "m1"));
        }

        var set = NodeClusterBuilder.Build(segments, Tol);

        Assert.True(set.HasAmbiguous);
        Assert.Contains(set.Diagnostics, d => d.Code == "chain_node_cluster_ambiguous" && d.IsError);
    }

    [Fact]
    public void Build_RecordsSourceKeysOfMergedEnds()
    {
        var set = NodeClusterBuilder.Build(
            [RigidTransformFixture.Segment("a", 0, 1), RigidTransformFixture.Segment("b", 1, 2)], Tol);
        var shared = set.Clusters[set.EndCluster[0]];

        Assert.Contains("a", shared.SourceKeys);
        Assert.Contains("b", shared.SourceKeys);
    }

    static List<BeamSegmentInput> ThreeEnds(double step) =>
    [
        new("s0", new PlanarVector3(0, 0, 0), new PlanarVector3(0, 5, 0), 0, BetaSource.Member, "m1"),
        new("s1", new PlanarVector3(step, 0, 0), new PlanarVector3(step, 5, 0), 0, BetaSource.Member, "m1"),
        new("s2", new PlanarVector3(2 * step, 0, 0), new PlanarVector3(2 * step, 5, 0), 0, BetaSource.Member, "m1")
    ];
}
