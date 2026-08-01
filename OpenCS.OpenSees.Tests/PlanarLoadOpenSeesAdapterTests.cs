using CScore.Planar;
using OpenCS.OpenSees.CScore;

namespace OpenCS.OpenSees.Tests;

public class PlanarLoadOpenSeesAdapterTests
{
    [Fact]
    public void Map_UsesExplicitSnapshotToOpenSeesProvenance()
    {
        var result = new PlanarLoadMappingResult(
            true,
            [],
            new Dictionary<int, PlanarVector3> { [4] = new(1, 2, 3) },
            [],
            new(1, 2, 3),
            PlanarVector3.Zero,
            new(1, 2, 3),
            PlanarVector3.Zero);

        var loads = PlanarLoadOpenSeesAdapter.Map(result, new Dictionary<int, int> { [4] = 17 });

        var load = Assert.Single(loads);
        Assert.Equal(17, load.NodeTag);
        Assert.Equal(1, load.Fx);
        Assert.Equal(2, load.Fy);
        Assert.Equal(3, load.Fz);
        Assert.Equal(0, load.Mx);
        Assert.Equal(0, load.My);
        Assert.Equal(0, load.Mz);
    }

    [Fact]
    public void Map_RejectsUncalculableResult()
    {
        var result = new PlanarLoadMappingResult(
            false,
            [new("load_error", "broken")],
            new Dictionary<int, PlanarVector3>(),
            [],
            PlanarVector3.Zero,
            PlanarVector3.Zero,
            PlanarVector3.Zero,
            PlanarVector3.Zero);

        Assert.Throws<InvalidOperationException>(() =>
            PlanarLoadOpenSeesAdapter.Map(result, new Dictionary<int, int>()));
    }

    [Fact]
    public void MapBoundarySet_MapsNodesAndEdgesWithoutCreatingConstraints()
    {
        var key = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 1);
        var set = new PlanarBoundarySet(BoundaryRole.Support, [key], [4, 7], [(4, 7)]);

        var mapped = PlanarLoadOpenSeesAdapter.MapBoundarySet(
            set,
            new Dictionary<int, int> { [4] = 17, [7] = 19 });

        Assert.Equal(BoundaryRole.Support, mapped.Role);
        Assert.Equal([17, 19], mapped.NodeTags);
        Assert.Equal([(17, 19)], mapped.Edges);
    }
}
