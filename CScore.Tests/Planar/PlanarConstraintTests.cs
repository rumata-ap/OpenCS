using CScore;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public sealed class PlanarConstraintTests
{
    static PlanarRegion Region() => PlanarRegion.CreateFromContour(new Contour
    {
        X = [0, 4, 4, 0],
        Y = [0, 0, 4, 4]
    });

    [Fact]
    public void Validate_AcceptsPointCurveAndRegionLociInsideHost()
    {
        var constraints = new[]
        {
            PlanarConstraintObject.Point(
                "point-1",
                new PlanarPoint2D(1, 1),
                new PlanarStructuralFacet(PlanarStructuralKind.PointMpc,
                    new PlanarMasterReference("test", "master-1")),
                new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint)),
            PlanarConstraintObject.Curve(
                "curve-1",
                [new PlanarPoint2D(1, 1), new PlanarPoint2D(3, 1)],
                new PlanarStructuralFacet(PlanarStructuralKind.Tie),
                new PlanarMeshFacet(PlanarMeshKind.EmbeddedCurve)),
            PlanarConstraintObject.Region(
                "region-1",
                [new PlanarPoint2D(1, 2), new PlanarPoint2D(3, 2), new PlanarPoint2D(3, 3), new PlanarPoint2D(1, 3)],
                new PlanarStructuralFacet(PlanarStructuralKind.RigidBody,
                    new PlanarMasterReference("test", "master-2")),
                new PlanarMeshFacet(PlanarMeshKind.ConformingPartition))
        };

        var diagnostics = PlanarConstraintValidator.Validate(Region(), constraints);

        Assert.DoesNotContain(diagnostics, d => d.IsError);
    }

    [Fact]
    public void Validate_RejectsStructuralFacetWithoutRequiredReference()
    {
        var constraint = PlanarConstraintObject.Point(
            "rigid-1",
            new PlanarPoint2D(0.5, 0.5),
            new PlanarStructuralFacet(PlanarStructuralKind.RigidBody),
            new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint));

        var diagnostics = PlanarConstraintValidator.Validate(Region(), [constraint]);

        Assert.Contains(diagnostics, d => d.Code == "planar_constraint_master_reference_missing");
    }

    [Fact]
    public void Validate_RejectsDuplicateIdsAndLocusInsideHole()
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] },
            [new Contour { X = [1, 3, 3, 1], Y = [1, 1, 3, 3] }]);
        var constraints = new[]
        {
            PlanarConstraintObject.Point("duplicate", new PlanarPoint2D(0.5, 0.5),
                new PlanarStructuralFacet(PlanarStructuralKind.None),
                new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint)),
            PlanarConstraintObject.Point("duplicate", new PlanarPoint2D(0.75, 0.75),
                new PlanarStructuralFacet(PlanarStructuralKind.None),
                new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint)),
            PlanarConstraintObject.Point("hole", new PlanarPoint2D(2, 2),
                new PlanarStructuralFacet(PlanarStructuralKind.None),
                new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint))
        };

        var diagnostics = PlanarConstraintValidator.Validate(region, constraints);

        Assert.Contains(diagnostics, d => d.Code == "planar_constraint_id_duplicate");
        Assert.Contains(diagnostics, d => d.Code == "planar_constraint_inside_hole");
    }

    [Fact]
    public void Validate_RejectsIncompatibleGeometryAndMeshFacet()
    {
        var constraint = PlanarConstraintObject.Point(
            "point-1",
            new PlanarPoint2D(1, 1),
            new PlanarStructuralFacet(PlanarStructuralKind.None),
            new PlanarMeshFacet(PlanarMeshKind.EmbeddedCurve));

        var diagnostics = PlanarConstraintValidator.Validate(Region(), [constraint]);

        Assert.Contains(diagnostics, d => d.Code == "planar_constraint_mesh_facet_incompatible");
    }

    [Fact]
    public void RegionFingerprintChangesWhenConstraintChanges()
    {
        var region = Region();
        region.ConstraintObjects.Add(PlanarConstraintObject.Point(
            "point-1",
            new PlanarPoint2D(1, 1),
            new PlanarStructuralFacet(PlanarStructuralKind.None),
            new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint)));
        region.RecalcFingerprint();
        var first = region.GeometryFingerprint;

        region.ConstraintObjects[0].Geometry =
            new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Point, [new PlanarPoint2D(2, 2)]);
        region.RecalcFingerprint();

        Assert.NotEqual(first, region.GeometryFingerprint);
    }

    /// <summary>Линия на кромке отверстия (стена вдоль края проёма плиты) — не в отверстии; внутри — в отверстии.</summary>
    [Fact]
    public void Validate_CurveOnHoleEdge_NotInsideHole()
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] },
            [new Contour { X = [1, 3, 3, 1], Y = [1, 1, 3, 3] }]);
        PlanarConstraintObject Curve(string id, double v, double u0 = 1, double u1 = 3) => PlanarConstraintObject.Curve(id,
            [new PlanarPoint2D(u0, v), new PlanarPoint2D(u1, v)],
            new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.ConformingPartition));

        Assert.DoesNotContain(PlanarConstraintValidator.Validate(region, [Curve("edge", 3)]), d => d.Code == "planar_constraint_inside_hole");
        Assert.DoesNotContain(PlanarConstraintValidator.Validate(region, [Curve("edge", 1)]), d => d.Code == "planar_constraint_inside_hole");
        Assert.Contains(PlanarConstraintValidator.Validate(region, [Curve("inside", 2, 1.5, 2.5)]), d => d.Code == "planar_constraint_inside_hole");
    }

    /// <summary>Области, примыкающие по стороне (куски следа АЖТ, разрезанные отверстием), — не перекрытие; заход
    /// одной в другую и вложение — перекрытие.</summary>
    [Fact]
    public void Validate_RegionsTouchingAlongEdge_NotOverlap()
    {
        var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 10, 10, 0], Y = [0, 0, 10, 10] });
        PlanarConstraintObject Box(string id, double u0, double v0, double u1, double v1) => PlanarConstraintObject.Region(id,
            [new PlanarPoint2D(u0, v0), new PlanarPoint2D(u1, v0), new PlanarPoint2D(u1, v1), new PlanarPoint2D(u0, v1)],
            new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.ConformingPartition));
        bool Overlap(params PlanarConstraintObject[] c) =>
            PlanarConstraintValidator.Validate(region, c).Any(d => d.Code == "planar_constraint_overlap");

        // Как след колонны 14 «Музея»: верхний кусок шире нижнего, общая сторона с Т-стыком.
        Assert.False(Overlap(Box("a", 1, 2, 3, 3), Box("b", 1, 1.5, 2.5, 2)));
        Assert.False(Overlap(Box("a", 1, 1, 2, 2), Box("b", 2, 2, 3, 3)));
        Assert.True(Overlap(Box("a", 0, 0, 2, 1), Box("b", 1, 0, 3, 1)));
        Assert.True(Overlap(Box("a", 0, 0, 2, 1), Box("b", 0, 0, 1, 1)));
        Assert.True(Overlap(Box("a", 0, 0, 3, 3), Box("b", 1, 1, 2, 2)));
    }
}
