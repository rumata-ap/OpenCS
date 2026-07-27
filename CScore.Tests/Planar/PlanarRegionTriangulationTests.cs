using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public class PlanarRegionTriangulationTests
{
    static double TriangleArea((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
        => System.Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) / 2.0;

    [Fact]
    public void Triangulate_SquareWithoutHoles_TotalAreaMatchesHull()
    {
        var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] });

        var (vertices, triangles) = PlanarRegionTriangulation.Triangulate(region);

        double total = 0;
        foreach (var (a, b, c) in triangles)
            total += TriangleArea(vertices[a], vertices[b], vertices[c]);

        Assert.Equal(16.0, total, 6);
    }

    [Fact]
    public void Triangulate_SquareWithHole_TotalAreaExcludesHole()
    {
        var hull = new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] };
        var hole = new Contour { X = [1, 2, 2, 1], Y = [1, 1, 2, 2] };
        var region = PlanarRegion.CreateFromContour(hull, holes: [hole]);

        var (vertices, triangles) = PlanarRegionTriangulation.Triangulate(region);

        double total = 0;
        foreach (var (a, b, c) in triangles)
            total += TriangleArea(vertices[a], vertices[b], vertices[c]);

        Assert.Equal(15.0, total, 6);
    }

    [Fact]
    public void Triangulate_ReturnsOnlyValidVertexIndices()
    {
        var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 3, 3, 0], Y = [0, 0, 3, 3] });

        var (vertices, triangles) = PlanarRegionTriangulation.Triangulate(region);

        Assert.NotEmpty(triangles);
        foreach (var (a, b, c) in triangles)
        {
            Assert.InRange(a, 0, vertices.Length - 1);
            Assert.InRange(b, 0, vertices.Length - 1);
            Assert.InRange(c, 0, vertices.Length - 1);
        }
    }
}
