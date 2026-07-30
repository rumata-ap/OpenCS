using CScore;
using CScore.PlateRebar;
using Xunit;

namespace CScore.Tests.PlateRebar;

public class RebarZonePolygonConverterTests
{
    static Contour MakeContour(double[] xs, double[] ys) =>
        new() { X = [.. xs], Y = [.. ys] };

    [Fact]
    public void FromContour_ClosedContour_DropsDuplicateClosingVertex()
    {
        // квадрат 0..1, последняя вершина дублирует первую (0,0)
        var contour = MakeContour([0, 1, 1, 0, 0], [0, 0, 1, 1, 0]);

        var polygon = RebarZonePolygonConverter.FromContour(contour);

        Assert.Equal(4, polygon.Count);
        Assert.Equal(0, polygon[0].U);
        Assert.Equal(0, polygon[0].V);
        Assert.Equal(0, polygon[3].U);
        Assert.Equal(1, polygon[3].V);
    }

    [Fact]
    public void FromContour_OpenContour_KeepsAllVertices()
    {
        var contour = MakeContour([0, 1, 1, 0], [0, 0, 1, 1]);

        var polygon = RebarZonePolygonConverter.FromContour(contour);

        Assert.Equal(4, polygon.Count);
        Assert.Equal(1, polygon[2].U);
        Assert.Equal(1, polygon[2].V);
    }

    [Fact]
    public void FromContour_ResultIsIndependentOfSourceContour()
    {
        var contour = MakeContour([0, 1, 1, 0], [0, 0, 1, 1]);

        var polygon = RebarZonePolygonConverter.FromContour(contour);
        contour.X[0] = 99;

        Assert.Equal(0, polygon[0].U);
    }
}
