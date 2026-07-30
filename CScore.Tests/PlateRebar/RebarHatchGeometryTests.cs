using CScore.PlateRebar;
using Xunit;

namespace CScore.Tests.PlateRebar;

public class RebarHatchGeometryTests
{
    static readonly (double U, double V)[] UnitSquare =
        [(0, 0), (1, 0), (1, 1), (0, 1)];

    [Fact]
    public void BuildDirectionX_Angle0_ReturnsHorizontalLinesSpanningFullWidth()
    {
        var segments = RebarHatchGeometry.BuildDirectionX(UnitSquare, 0);

        Assert.Equal(RebarHatchGeometry.LineCount, segments.Count);
        foreach (var s in segments)
        {
            Assert.Equal(0, s.U1, 9);
            Assert.Equal(1, s.U2, 9);
            Assert.Equal(s.V1, s.V2, 9);
            Assert.InRange(s.V1, 0, 1);
        }
    }

    [Fact]
    public void BuildDirectionY_Angle0_ReturnsVerticalLinesSpanningFullHeight()
    {
        var segments = RebarHatchGeometry.BuildDirectionY(UnitSquare, 0);

        Assert.Equal(RebarHatchGeometry.LineCount, segments.Count);
        foreach (var s in segments)
        {
            Assert.Equal(0, s.V1, 9);
            Assert.Equal(1, s.V2, 9);
            Assert.Equal(s.U1, s.U2, 9);
            Assert.InRange(s.U1, 0, 1);
        }
    }

    [Fact]
    public void BuildDirectionX_Angle45_LinesAreParallelToRotatedAxis()
    {
        var segments = RebarHatchGeometry.BuildDirectionX(UnitSquare, 45);

        Assert.Equal(RebarHatchGeometry.LineCount, segments.Count);
        foreach (var s in segments)
        {
            double dx = s.U2 - s.U1, dy = s.V2 - s.V1;
            double len = System.Math.Sqrt(dx * dx + dy * dy);
            double dot = System.Math.Abs(dx / len * System.Math.Cos(System.Math.PI / 4)
                                        + dy / len * System.Math.Sin(System.Math.PI / 4));
            Assert.True(dot > 0.999, $"dot={dot}");
        }
    }

    [Fact]
    public void BuildDirectionX_DegeneratePolygon_ReturnsEmpty()
    {
        Assert.Empty(RebarHatchGeometry.BuildDirectionX(System.Array.Empty<(double, double)>(), 0));
        Assert.Empty(RebarHatchGeometry.BuildDirectionX([(0, 0)], 0));
    }

    [Fact]
    public void BuildDirectionY_DegeneratePolygon_ReturnsEmpty()
    {
        Assert.Empty(RebarHatchGeometry.BuildDirectionY(System.Array.Empty<(double, double)>(), 0));
        Assert.Empty(RebarHatchGeometry.BuildDirectionY([(0, 0)], 0));
    }

    [Fact]
    public void Centroid_Rectangle_ReturnsCenter()
    {
        var (u, v) = RebarHatchGeometry.Centroid([(0, 0), (2, 0), (2, 4), (0, 4)]);
        Assert.Equal(1, u, 9);
        Assert.Equal(2, v, 9);
    }

    [Fact]
    public void Centroid_EmptyPolygon_ReturnsOrigin()
    {
        var (u, v) = RebarHatchGeometry.Centroid(System.Array.Empty<(double, double)>());
        Assert.Equal(0, u);
        Assert.Equal(0, v);
    }
}
