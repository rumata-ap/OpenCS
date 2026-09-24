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

    /// <summary>Кривая constraint, концы которой совпадают с вершинами контура, обязана ссылаться
    /// на эти вершины, а не создавать их копии: для OpenCASCADE копия с теми же координатами —
    /// другая точка, и сетка вырождается (Срез 8a, стена поперёк плиты).</summary>
    [Fact]
    public void Build_ReusesHostVerticesForConstraintEndpoints()
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 6, 12, 12, 6, 0], Y = [-1, -1, -1, 1, 1, 1] }, frame: Frame3D.Identity);
        region.ConstraintObjects =
        [
            PlanarConstraintObject.Curve("wall", [new PlanarPoint2D(6, -1), new PlanarPoint2D(6, 1)],
                new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.EmbeddedCurve)),
            PlanarConstraintObject.Point("corner", new PlanarPoint2D(12, 1),
                new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint))
        ];

        var lines = GmshPlanarGeoBuilder.Build(region, new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Quads))
            .Split(Environment.NewLine);

        // Шесть вершин контура — и ни одной новой точки: концы стены и угловая точка переиспользованы.
        var points = lines.Where(line => line.StartsWith("Point(")).ToList();
        Assert.Equal(6, points.Count);
        var coordinates = points.ToDictionary(
            line => line[6..line.IndexOf(')')],
            line => line[(line.IndexOf('{') + 1)..line.IndexOf(", 0,")]);
        var wallLine = lines.Single(line => line.StartsWith("Line(7)"));
        var ends = wallLine[(wallLine.IndexOf('{') + 1)..wallLine.IndexOf('}')].Split(", ");
        Assert.Equal(["6, -1", "6, 1"], ends.Select(id => coordinates[id]).OrderBy(c => c, StringComparer.Ordinal));
        // Вершина контура уже на границе поверхности — повторно в неё не встраивается.
        Assert.DoesNotContain(lines, line => line.StartsWith("Point {"));
        Assert.Contains(lines, line => line.StartsWith("Physical Point(\"constraint:corner:point\""));
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

    [Fact]
    public void Build_UsesExplicitRequestConstraintsInsteadOfRegionConstraints()
    {
        var region = PlanarRegion.CreateFromContour(new Contour
        {
            X = [0, 4, 4, 0],
            Y = [0, 0, 4, 4]
        });
        var constraint = PlanarConstraintObject.Point(
            "derived-point",
            new(2, 2),
            new PlanarStructuralFacet(PlanarStructuralKind.EmbeddedMember),
            new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint));

        var geo = GmshPlanarGeoBuilder.Build(
            region,
            new PlanarMeshSettings(0.25, 6, PlanarMeshElementMode.Mixed),
            [constraint]);

        Assert.Contains("constraint:derived-point:point", geo);
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
