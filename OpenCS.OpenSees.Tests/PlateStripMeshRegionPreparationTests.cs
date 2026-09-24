using System.Linq;
using CScore;
using CScore.Planar;
using OpenCS.OpenSees.CScore.Fragments;
using Xunit;

namespace OpenCS.OpenSees.Tests;

/// <summary>Срез 8a: копия региона для мешинга с вершинами в концах встроенных кривых, лежащих на
/// рёбрах контура (валидатор constraints считает такое касание пересечением границы).</summary>
public sealed class PlateStripMeshRegionPreparationTests
{
    static PlanarRegion Region()
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 18, 18, 0], Y = [-1, -1, 1, 1] }, frame: Frame3D.Identity, tag: "slab");
        region.Id = 7;
        return region;
    }

    static PlanarConstraintObject Wall(string id, double u) =>
        PlanarConstraintObject.Curve(id, [new PlanarPoint2D(u, -1), new PlanarPoint2D(u, 1)],
            new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.EmbeddedCurve));

    [Fact]
    public void EndpointsOnEdges_BecomeHullVerticesInOrder()
    {
        var region = Region();

        var prepared = PlateStripAnalogyRunner.PrepareMeshRegion(region, [Wall("b", 12), Wall("a", 6)], 1e-6);

        var hull = prepared.RequireHull();
        var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(hull.X, hull.Y);
        Assert.Equal([0.0, 6.0, 12.0, 18.0, 18.0, 12.0, 6.0, 0.0], x);
        Assert.Equal([-1.0, -1.0, -1.0, -1.0, 1.0, 1.0, 1.0, 1.0], y);
        Assert.Equal(7, prepared.Id);
        // Исходный регион не тронут.
        Assert.Equal(4, PlanarRegionTopologyValidator.ToOpenLoop(region.RequireHull().X, region.RequireHull().Y).X.Length);
    }

    [Fact]
    public void BoundarySegmentOfSplitEdge_IsCopiedToEveryPiece()
    {
        var region = Region();
        region.BoundarySegments =
        [
            new BoundarySegment { StartVertex = 0, EndVertex = 1, Role = BoundaryRole.Support, Provenance = "bottom" },
            new BoundarySegment { StartVertex = 3, EndVertex = 0, Role = BoundaryRole.Free, Provenance = "left" }
        ];

        var prepared = PlateStripAnalogyRunner.PrepareMeshRegion(region, [Wall("a", 6), Wall("b", 12)], 1e-6);

        var bottom = prepared.BoundarySegments.Where(s => s.Provenance == "bottom")
            .Select(s => (s.StartVertex, s.EndVertex)).ToList();
        Assert.Equal([(0, 1), (1, 2), (2, 3)], bottom);
        Assert.All(prepared.BoundarySegments.Where(s => s.Provenance == "bottom"),
            s => Assert.Equal(BoundaryRole.Support, s.Role));
        var left = Assert.Single(prepared.BoundarySegments, s => s.Provenance == "left");
        Assert.Equal((7, 0), (left.StartVertex, left.EndVertex));
    }

    [Fact]
    public void NothingOnEdges_ReturnsSameRegion()
    {
        var region = Region();
        var interior = PlanarConstraintObject.Curve("inner", [new PlanarPoint2D(6, -0.5), new PlanarPoint2D(6, 0.5)],
            new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.EmbeddedCurve));

        Assert.Same(region, PlateStripAnalogyRunner.PrepareMeshRegion(region, [interior], 1e-6));
    }
}
