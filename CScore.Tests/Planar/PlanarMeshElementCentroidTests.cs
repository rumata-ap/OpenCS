using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public class PlanarMeshElementCentroidTests
{
    [Fact]
    public void Centroid_Triangle_IsVertexAverage()
    {
        var nodes = new List<PlanarMeshNode>
        {
            new(0, 0, 0, 0, 0, 0),
            new(1, 3, 0, 3, 0, 0),
            new(2, 0, 3, 0, 3, 0),
        };
        var element = new PlanarMeshElement(0, PlanarMeshElementKind.Triangle3, [0, 1, 2]);

        var (u, v) = element.Centroid(nodes);

        Assert.Equal(1.0, u, 10);
        Assert.Equal(1.0, v, 10);
    }

    [Fact]
    public void Centroid_Quad_IsVertexAverage()
    {
        var nodes = new List<PlanarMeshNode>
        {
            new(0, 0, 0, 0, 0, 0),
            new(1, 2, 0, 2, 0, 0),
            new(2, 2, 4, 2, 4, 0),
            new(3, 0, 4, 0, 4, 0),
        };
        var element = new PlanarMeshElement(0, PlanarMeshElementKind.Quadrangle4, [0, 1, 2, 3]);

        var (u, v) = element.Centroid(nodes);

        Assert.Equal(1.0, u, 10);
        Assert.Equal(2.0, v, 10);
    }
}
