using CScore.Fem;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Линии стыка областей по геометрии (CSfea 4г, срез 4).</summary>
public class FemPlanarMeshPlanTests
{
    static readonly FemPlanarMaterializationResult Empty = new();

    /// <summary>Плита 6×4 на отметке 3 м; стена в плоскости y = 2 высотой 0…6 м проходит сквозь неё.</summary>
    static (PlanarRegion Slab, PlanarRegion Wall) SlabAndWallThrough()
    {
        var slab = PlanarRegion.CreateFromContour(new Contour { X = [0, 6, 6, 0], Y = [0, 0, 4, 4] },
            frame: new Frame3D(new PlanarVector3(0, 0, 3), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1)));
        slab.Id = 1;
        var wall = PlanarRegion.CreateFromContour(new Contour { X = [0, 6, 6, 0], Y = [0, 0, 6, 6] },
            frame: new Frame3D(new PlanarVector3(0, 2, 0), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 0, 1), new PlanarVector3(0, -1, 0)));
        wall.Id = 2;
        return (slab, wall);
    }

    static FemMember[] Members() =>
    [
        new() { Id = 1, ElemTag = "П", ElemType = "shell", PlanarRegionId = 1, NodeIdsJson = "[]" },
        new() { Id = 2, ElemTag = "С", ElemType = "shell", PlanarRegionId = 2, NodeIdsJson = "[]" },
    ];

    [Fact]
    public void LaterWallThroughSlab_JunctionLineInSlab()
    {
        var (slab, wall) = SlabAndWallThrough();

        var result = FemPlanarMeshPlan.Constraints(1, [], Members(), Empty, slab, laterRegions: [("С", wall)]);

        var curve = Assert.Single(result.Constraints, c => c.Id.StartsWith("region-junction:С:"));
        Assert.Equal(PlanarConstraintGeometryKind.Curve, curve.Geometry.Kind);
        Assert.Equal(1, result.JunctionCount);
        var us = curve.Geometry.Points.Select(p => p.U).OrderBy(u => u).ToArray();
        Assert.Equal([0, 6], us.Select(u => Math.Round(u, 9)));
        Assert.All(curve.Geometry.Points, p => Assert.Equal(2, p.V, 9));
        Assert.False(curve.KeepVertices);
    }

    [Fact]
    public void LaterSlabThroughWall_JunctionLineInWall_ClippedToSlab()
    {
        var (slab, wall) = SlabAndWallThrough();

        var result = FemPlanarMeshPlan.Constraints(1, [], Members(), Empty, wall, laterRegions: [("П", slab)]);

        // Плита пересекает плоскость стены по линии y = 2 на отметке 3 (в осях стены v = 3), x от 0 до 6.
        var curve = Assert.Single(result.Constraints, c => c.Id.StartsWith("region-junction:П:"));
        Assert.All(curve.Geometry.Points, p => Assert.Equal(3, p.V, 9));
        Assert.Equal([0.0, 6.0], curve.Geometry.Points.Select(p => Math.Round(p.U, 9)).OrderBy(u => u));
    }

    [Fact]
    public void LaterRegion_ChangesSourceFingerprint()
    {
        var (slab, wall) = SlabAndWallThrough();
        var alone = FemPlanarMeshPlan.Constraints(1, [], Members(), Empty, slab);
        var withWall = FemPlanarMeshPlan.Constraints(1, [], Members(), Empty, slab, laterRegions: [("С", wall)]);
        Assert.NotEqual(alone.SourceFingerprint, withWall.SourceFingerprint);
    }

    [Fact]
    public void LaterParallelRegion_NoJunction()
    {
        var (slab, _) = SlabAndWallThrough();
        var upper = PlanarRegion.CreateFromContour(new Contour { X = [0, 6, 6, 0], Y = [0, 0, 4, 4] },
            frame: new Frame3D(new PlanarVector3(0, 0, 6), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1)));

        var result = FemPlanarMeshPlan.Constraints(1, [], Members(), Empty, slab, laterRegions: [("П2", upper)]);

        Assert.Equal(0, result.JunctionCount);
    }

    /// <summary>Кривая по сетке ранней области — «сохранить вершины»; по стержню — нет.</summary>
    [Fact]
    public void Deriver_CurveFromForeignRegionShell_KeepsVertices()
    {
        var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] });
        region.Id = 1;
        var nodes = new List<FemNode>
        {
            new() { Id = 1, NodeTag = "1", X = -1, Y = 0 }, new() { Id = 2, NodeTag = "2", X = 0, Y = 0 },
            new() { Id = 3, NodeTag = "3", X = 0, Y = 1 }, new() { Id = 4, NodeTag = "4", X = -1, Y = 1 },
            new() { Id = 5, NodeTag = "5", X = 1, Y = 2 }, new() { Id = 6, NodeTag = "6", X = 3, Y = 2 },
        };
        var members = new List<FemMember>
        {
            new() { Id = 1, ElemTag = "host", ElemType = "shell", PlanarRegionId = 1, NodeIdsJson = "[]" },
            new() { Id = 2, ElemTag = "left", ElemType = "shell", PlanarRegionId = 2, NodeIdsJson = "[]" },
            new() { Id = 3, ElemTag = "beam", ElemType = "beam", NodeIdsJson = "[5,6]" },
        };
        var elements = new List<FemElement>
        {
            new() { Id = 1, ElemTag = "1", ElemType = "shell", SourceMemberTag = "left", NodeIdsJson = "[1,2,4,3]" },
        };

        var result = PlanarConstraintDeriver.Derive(new FemSchemaTopology(1, nodes, members, elements), region);

        var curves = result.Constraints.Where(c => c.Geometry.Kind == PlanarConstraintGeometryKind.Curve).ToList();
        Assert.True(Assert.Single(curves, c => c.Geometry.Points.All(p => Math.Abs(p.U) < 1e-9)).KeepVertices);
        Assert.False(Assert.Single(curves, c => c.Geometry.Points.All(p => Math.Abs(p.V - 2) < 1e-9)).KeepVertices);
    }
}
