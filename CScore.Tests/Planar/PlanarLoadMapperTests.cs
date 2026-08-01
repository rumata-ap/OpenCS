using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public class PlanarLoadMapperTests
{
    [Fact]
    public void Map_SurfaceLoadOnTriangle_TransformsLocalVectorAndPreservesBalance()
    {
        var frame = new Frame3D(
            new(0, 0, 0),
            new(1, 0, 0),
            new(0, 0, 1),
            new(0, -1, 0));
        var snapshot = new PlanarMeshSnapshot
        {
            Nodes =
            [
                new(0, 0, 0, 0, 0, 0),
                new(1, 1, 0, 1, 0, 0),
                new(2, 0, 1, 0, 0, 1),
            ],
            Elements = [new(0, PlanarMeshElementKind.Triangle3, [0, 1, 2])]
        };

        var result = PlanarLoadMapper.Map(
            frame,
            snapshot,
            [new PlanarLoad
            {
                Tag = "pressure",
                Kind = PlanarLoadKind.Surface,
                CoordinateSystem = PlanarLoadCoordinateSystem.Local,
                Components = new(0, 0, 10)
            }]);

        Assert.True(result.IsCalculable);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(-5, result.MappedForceGlobal.Y, 10);
        Assert.Equal(result.AppliedForceGlobal, result.MappedForceGlobal);
        Assert.Equal(result.AppliedMomentAboutOriginGlobal, result.MappedMomentAboutOriginGlobal);
        Assert.Equal(5.0 / 3.0, result.MappedMomentAboutOriginGlobal.X, 10);
        Assert.Equal(-5.0 / 3.0, result.MappedMomentAboutOriginGlobal.Z, 10);
    }

    [Fact]
    public void Map_SurfaceLoadOnQuadrangle_PreservesFirstMoment()
    {
        var snapshot = new PlanarMeshSnapshot
        {
            Nodes =
            [
                new(0, 0, 0, 0, 0, 0),
                new(1, 2, 0, 2, 0, 0),
                new(2, 2, 1, 2, 1, 0),
                new(3, 0, 1, 0, 1, 0),
            ],
            Elements = [new(0, PlanarMeshElementKind.Quadrangle4, [0, 1, 2, 3])]
        };

        var result = PlanarLoadMapper.Map(
            Frame3D.Identity,
            snapshot,
            [new PlanarLoad
            {
                Kind = PlanarLoadKind.Surface,
                Components = new(0, 0, 2)
            }]);

        Assert.True(result.IsCalculable);
        Assert.Equal(new(0, 0, 4), result.MappedForceGlobal);
        Assert.Equal(new(2, -4, 0), result.MappedMomentAboutOriginGlobal);
    }

    [Fact]
    public void Map_BoundaryLoadOnChain_SumsSharedNodeAndPreservesBalance()
    {
        var key = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 2);
        var snapshot = new PlanarMeshSnapshot
        {
            Nodes =
            [
                new(0, 0, 0, 0, 0, 0),
                new(1, 1, 0, 1, 0, 0),
                new(2, 2, 0, 1, 1, 0),
            ],
            BoundaryMappings = [new() { Key = key, NodeIndices = [0, 1, 2] }]
        };

        var result = PlanarLoadMapper.Map(
            Frame3D.Identity,
            snapshot,
            [new PlanarLoad
            {
                Kind = PlanarLoadKind.Boundary,
                BoundaryKey = key,
                Components = new(0, 0, 10)
            }]);

        Assert.True(result.IsCalculable);
        Assert.Equal(20, result.MappedForceGlobal.Z, 10);
        Assert.Equal(5, result.MappedMomentAboutOriginGlobal.X, 10);
        Assert.Equal(-15, result.MappedMomentAboutOriginGlobal.Y, 10);
        Assert.Equal(10, result.NodalLoads[1].Z, 10);
    }

    [Fact]
    public void Map_PointLoad_RequiresOneExactNode()
    {
        var snapshot = new PlanarMeshSnapshot
        {
            Nodes =
            [
                new(0, 0, 0, 0, 0, 0),
                new(1, 1, 0, 1, 0, 0),
            ]
        };

        var missing = PlanarLoadMapper.Map(
            Frame3D.Identity,
            snapshot,
            [new PlanarLoad
            {
                Kind = PlanarLoadKind.Point,
                PointU = 0.5,
                PointV = 0,
                Components = new(1, 0, 0)
            }]);

        Assert.False(missing.IsCalculable);
        Assert.Contains(missing.Diagnostics, d => d.Code == "planar_load_point_node_missing");

        var exact = PlanarLoadMapper.Map(
            Frame3D.Identity,
            snapshot,
            [new PlanarLoad
            {
                Kind = PlanarLoadKind.Point,
                PointU = 1,
                PointV = 0,
                Components = new(1, 0, 0)
            }]);

        Assert.True(exact.IsCalculable);
        Assert.Equal(1, exact.NodalLoads[1].X);
    }

    [Fact]
    public void Map_PointLoad_RejectsAmbiguousNodeMatch()
    {
        var snapshot = new PlanarMeshSnapshot
        {
            Nodes =
            [
                new(0, 0, 0, 0, 0, 0),
                new(1, 0, 0, 0, 0, 0),
            ]
        };

        var result = PlanarLoadMapper.Map(
            Frame3D.Identity,
            snapshot,
            [new PlanarLoad
            {
                Kind = PlanarLoadKind.Point,
                Components = new(1, 0, 0)
            }]);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "planar_load_point_node_ambiguous");
    }

    [Fact]
    public void BoundaryContractMapper_ReturnsRoleSetsAndRejectsIncompleteMapping()
    {
        var support = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, 0, 1);
        var region = new PlanarRegion
        {
            BoundarySegments =
            [new() { Loop = support.Loop, HoleIndex = support.HoleIndex, StartVertex = 0, EndVertex = 1, Role = BoundaryRole.Support }]
        };
        var snapshot = new PlanarMeshSnapshot
        {
            Nodes = [new(0, 0, 0, 0, 0, 0), new(1, 1, 0, 1, 0, 0)],
            BoundaryMappings = [new() { Key = support, NodeIndices = [0, 1] }]
        };

        var mapped = PlanarBoundaryContractMapper.Map(region, snapshot);

        Assert.True(mapped.IsCalculable);
        var set = Assert.Single(mapped.Sets);
        Assert.Equal(BoundaryRole.Support, set.Role);
        Assert.Equal([0, 1], set.NodeIndices);
        Assert.Equal([(0, 1)], set.Edges);

        var incomplete = PlanarBoundaryContractMapper.Map(
            region,
            new PlanarMeshSnapshot { Nodes = snapshot.Nodes });
        Assert.False(incomplete.IsCalculable);
        Assert.Contains(incomplete.Diagnostics, d => d.Code == "planar_boundary_mapping_missing");
    }
}
