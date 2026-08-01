using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public class PlanarMeshEdgeExtractorTests
{
    [Fact]
    public void ExtractEdges_Triangle_ReturnsThreeEdges()
    {
        var snapshot = new PlanarMeshSnapshot
        {
            Nodes = [new(0, 0, 0, 0, 0, 0), new(1, 1, 0, 1, 0, 0), new(2, 0, 1, 0, 1, 0)],
            Elements = [new(0, PlanarMeshElementKind.Triangle3, [0, 1, 2])]
        };

        var edges = PlanarMeshEdgeExtractor.ExtractEdges(snapshot);

        Assert.Equal(3, edges.Count);
    }

    [Fact]
    public void ExtractEdges_Quadrangle_ReturnsFourEdges()
    {
        var snapshot = new PlanarMeshSnapshot
        {
            Nodes = [new(0, 0, 0, 0, 0, 0), new(1, 1, 0, 1, 0, 0), new(2, 1, 1, 1, 1, 0), new(3, 0, 1, 0, 1, 0)],
            Elements = [new(0, PlanarMeshElementKind.Quadrangle4, [0, 1, 2, 3])]
        };

        var edges = PlanarMeshEdgeExtractor.ExtractEdges(snapshot);

        Assert.Equal(4, edges.Count);
    }

    [Fact]
    public void ExtractEdges_SharedEdgeBetweenTwoTriangles_IsNotDuplicated()
    {
        var snapshot = new PlanarMeshSnapshot
        {
            Nodes = [new(0, 0, 0, 0, 0, 0), new(1, 1, 0, 1, 0, 0), new(2, 1, 1, 1, 1, 0), new(3, 0, 1, 0, 1, 0)],
            Elements =
            [
                new(0, PlanarMeshElementKind.Triangle3, [0, 1, 2]),
                new(1, PlanarMeshElementKind.Triangle3, [0, 2, 3]),
            ]
        };

        var edges = PlanarMeshEdgeExtractor.ExtractEdges(snapshot);

        // 3 + 3 рёбра минус общая диагональ 0-2 (или 2-0), учтённая один раз
        Assert.Equal(5, edges.Count);
    }
}
