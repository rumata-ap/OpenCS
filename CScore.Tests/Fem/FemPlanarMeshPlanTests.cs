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

    /// <summary>Свободный узел в 0,4 мм от кромки плиты и в 0,6 мм от линии стыка со стеной — на них (иначе Gmsh
    /// дробит сетку до долей миллиметра); в 5 мм — как есть. Привязка меняет отпечаток.</summary>
    [Fact]
    public void FreeNodeNearEdgeOrJunction_SnappedWithinMillimetre()
    {
        var (slab, wall) = SlabAndWallThrough();
        FemNode[] nodes =
        [
            new() { NodeTag = "1", X = 3, Y = 0.0004, Z = 3 },
            new() { NodeTag = "2", X = 1, Y = 2.0006, Z = 3 },
            new() { NodeTag = "3", X = 5, Y = 2.005, Z = 3 },
        ];

        var result = FemPlanarMeshPlan.Constraints(1, nodes, Members(), Empty, slab, laterRegions: [("С", wall)]);

        PlanarPoint2D At(string tag) => Assert.Single(result.Constraints, c => c.Id == $"free-node:{tag}").Geometry.Points[0];
        Assert.Equal((3.0, 0.0), (At("1").U, At("1").V));
        Assert.Equal(2, At("2").V, 12);
        Assert.Equal(2.005, At("3").V, 12);
        var plain = FemPlanarMeshPlan.Constraints(1, [nodes[2]], Members(), Empty, slab, laterRegions: [("С", wall)]);
        Assert.NotEqual(plain.SourceFingerprint, result.SourceFingerprint);
    }

    /// <summary>Конец линии стыка, вышедший за кромку плиты на доли микрона (импортные контуры), — на кромке:
    /// проверка ограничений точная.</summary>
    [Fact]
    public void JunctionEndMicronsOutsideHull_SnappedOntoHull()
    {
        var (slab, _) = SlabAndWallThrough();
        var wall = PlanarRegion.CreateFromContour(new Contour { X = [0, 6.00000002, 6.00000002, 0], Y = [0, 0, 6, 6] },
            frame: new Frame3D(new PlanarVector3(0, 2, 0), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 0, 1), new PlanarVector3(0, -1, 0)));
        wall.Id = 2;

        var result = FemPlanarMeshPlan.Constraints(1, [], Members(), Empty, slab, laterRegions: [("С", wall)]);

        Assert.Empty(PlanarConstraintValidator.Validate(slab, result.Constraints));
    }

    /// <summary>Стена стоит на кромке y = 2 ранней плиты, узлы сетки плиты на кромке отходят от плоскости стены на
    /// ±3 мкм (импортные контуры «Музея») — кривая стыка по ним выводится целиком и сохраняет вершины.</summary>
    [Fact]
    public void EarlierSlabMeshMicronsOffWallPlane_CurveAlongWholeJunction()
    {
        var (_, wall) = SlabAndWallThrough();
        var nodes = new List<PlanarMeshNode>();
        for (int j = 0; j <= 2; j++)
            for (int i = 0; i <= 6; i++)
            {
                double y = j == 2 ? 2 + (i % 2 == 0 ? 3e-6 : -3e-6) : j;
                nodes.Add(new PlanarMeshNode(nodes.Count, i, y, i, y, 3));
            }
        var quads = new List<PlanarMeshElement>();
        for (int j = 0; j < 2; j++)
            for (int i = 0; i < 6; i++)
            {
                int n0 = j * 7 + i;
                quads.Add(new PlanarMeshElement(quads.Count, PlanarMeshElementKind.Quadrangle4, [n0, n0 + 1, n0 + 8, n0 + 7]));
            }
        var slabMember = Members()[0];
        var prefix = FemPlanarMeshMaterializer.Materialize(1, [], [], [],
            [new FemPlanarRegionMesh(slabMember, new PlanarRegion { Id = 1 }, new PlanarMeshSnapshot
                { RegionId = 1, IsCalculable = true, Nodes = nodes, Elements = quads }, 0.2)]);

        var result = FemPlanarMeshPlan.Constraints(1, [], Members(), prefix, wall);

        var curve = Assert.Single(result.Constraints, c => c.Geometry.Kind == PlanarConstraintGeometryKind.Curve && c.KeepVertices);
        Assert.Equal(7, curve.Geometry.Points.Count);
        Assert.Equal([0.0, 6.0], new[] { curve.Geometry.Points.Min(p => p.U), curve.Geometry.Points.Max(p => p.U) }.Select(u => Math.Round(u, 9)));
        Assert.Empty(PlanarConstraintValidator.Validate(wall, result.Constraints));
    }

    /// <summary>Свободный узел в 5 мкм за кромкой плиты (на стыке со стеной соседняя область его берёт) — в плите, на кромке.</summary>
    [Fact]
    public void FreeNodeMicronsOutsideEdge_IncludedOnEdge()
    {
        var (slab, _) = SlabAndWallThrough();

        var result = FemPlanarMeshPlan.Constraints(1, [new() { NodeTag = "1", X = 3, Y = -5e-6, Z = 3 }], Members(), Empty, slab);

        var point = Assert.Single(result.Constraints, c => c.Id == "free-node:1").Geometry.Points[0];
        Assert.Equal((3.0, 0.0), (point.U, point.V));
        Assert.Empty(PlanarConstraintValidator.Validate(slab, result.Constraints));
    }

    /// <summary>Стена упирается торцом в кромку x = 6 плиты: её след на плоскости плиты выходит за плиту и касается
    /// её одной точкой — точка стыка (узел там есть у стены).</summary>
    [Fact]
    public void LaterWallTouchingByTraceEnd_JunctionPoint()
    {
        var (slab, _) = SlabAndWallThrough();
        var wall = PlanarRegion.CreateFromContour(new Contour { X = [0, 3, 3, 0], Y = [0, 0, 6, 6] },
            frame: new Frame3D(new PlanarVector3(6, 2, 0), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 0, 1), new PlanarVector3(0, -1, 0)));
        wall.Id = 2;

        var result = FemPlanarMeshPlan.Constraints(1, [], Members(), Empty, slab, laterRegions: [("С", wall)]);

        var point = Assert.Single(result.Constraints, c => c.Id.StartsWith("region-junction:С:")).Geometry;
        Assert.Equal(PlanarConstraintGeometryKind.Point, point.Kind);
        Assert.Equal((6.0, 2.0), (Math.Round(point.Points[0].U, 9), Math.Round(point.Points[0].V, 9)));
    }

    /// <summary>Конец линии стыка в 0,18 мм от кромки плиты (импортные контуры) — на кромке: иначе КЭ со стороной
    /// 0,18 мм вырождается при слиянии узлов (1 мм).</summary>
    [Fact]
    public void JunctionEndFractionOfMillimetreInside_SnappedOntoHull()
    {
        var (slab, _) = SlabAndWallThrough();
        var wall = PlanarRegion.CreateFromContour(new Contour { X = [0, 5.99982, 5.99982, 0], Y = [0, 0, 6, 6] },
            frame: new Frame3D(new PlanarVector3(0, 2, 0), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 0, 1), new PlanarVector3(0, -1, 0)));
        wall.Id = 2;

        var result = FemPlanarMeshPlan.Constraints(1, [], Members(), Empty, slab, laterRegions: [("С", wall)]);

        var curve = Assert.Single(result.Constraints, c => c.Id.StartsWith("region-junction:С:"));
        Assert.Equal(6.0, curve.Geometry.Points.Max(p => p.U), 12);
        Assert.Equal(FemPlanarMeshPlan.MergeToleranceM, curve.ToleranceM);
    }
}
