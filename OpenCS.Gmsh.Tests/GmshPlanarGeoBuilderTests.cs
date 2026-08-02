using CScore;
using CScore.Planar;
using OpenCS.Gmsh.Generation;
using Xunit;

namespace OpenCS.Gmsh.Tests;

public sealed class GmshPlanarGeoBuilderTests
{
    [Fact]
    public void Build_IsDeterministicAndNamesEveryConstraint()
    {
        var region = RegionWithConstraints();
        var settings = new PlanarMeshSettings(0.25, 6, PlanarMeshElementMode.Mixed);

        var first = GmshPlanarGeoBuilder.Build(region, settings);
        var second = GmshPlanarGeoBuilder.Build(region, settings);

        Assert.Equal(first, second);
        Assert.Contains("Physical Point(\"constraint:point-1:point\",", first);
        Assert.Contains("Physical Curve(\"constraint:curve-1:curve\",", first);
        Assert.Contains("Physical Curve(\"constraint:region-1:region\",", first);
        Assert.Contains("Line {", first);
        Assert.Contains("In Surface", first);
    }

    [Fact]
    public void Build_DoesNotEmitConstraintRegionAsHole()
    {
        var region = RegionWithConstraints();

        var geo = GmshPlanarGeoBuilder.Build(region,
            new PlanarMeshSettings(0.25, 6, PlanarMeshElementMode.Mixed));

        Assert.Contains("Plane Surface(1) = {1};", geo);
        Assert.DoesNotContain("Plane Surface(2)", geo);
        Assert.Contains("In Surface {1};", geo);
    }

    static PlanarRegion RegionWithConstraints()
    {
        var region = PlanarRegion.CreateFromContour(new Contour
        {
            X = [0, 4, 4, 0],
            Y = [0, 0, 4, 4]
        });
        region.ConstraintObjects =
        [
            PlanarConstraintObject.Point(
                "point-1", new PlanarPoint2D(1, 1),
                new PlanarStructuralFacet(PlanarStructuralKind.None),
                new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint)),
            PlanarConstraintObject.Curve(
                "curve-1", [new PlanarPoint2D(1, 2), new PlanarPoint2D(3, 2)],
                new PlanarStructuralFacet(PlanarStructuralKind.None),
                new PlanarMeshFacet(PlanarMeshKind.EmbeddedCurve)),
            PlanarConstraintObject.Region(
                "region-1", [new PlanarPoint2D(1, 1), new PlanarPoint2D(3, 1), new PlanarPoint2D(3, 3), new PlanarPoint2D(1, 3)],
                new PlanarStructuralFacet(PlanarStructuralKind.None),
                new PlanarMeshFacet(PlanarMeshKind.ConformingPartition))
        ];
        return region;
    }
}
