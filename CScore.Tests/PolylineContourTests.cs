using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>Открытые полилинии (срезы-стержни) в Contour и WKT.</summary>
public sealed class PolylineContourTests
{
    [Fact]
    public void Polyline_WithTwoVertices_IsCreatedAndNotClosed()
    {
        var c = Contour.Polyline([0.0, 0.0], [-0.2, 0.2], "срез");

        Assert.Equal(2, c.X.Count);
        Assert.False(c.IsClosed);
        Assert.True(c.IsPolyline);
    }

    [Fact]
    public void Polyline_WritesLineStringWkt()
    {
        var c = Contour.Polyline([0.0, 0.0], [-0.2, 0.2], "срез");

        Assert.StartsWith("LINESTRING", c.WKT);
        Assert.True(WktHelper.IsLineString(c.WKT));
    }

    [Fact]
    public void ContourFromWkt_RoundTripsLineString()
    {
        var source = Contour.Polyline([0.01, 0.03], [-0.2, 0.25], "срез");

        var loaded = new Contour(source.WKT, "срез");

        Assert.True(loaded.IsPolyline);
        Assert.Equal(2, loaded.X.Count);
        Assert.Equal(0.01, loaded.X[0], 12);
        Assert.Equal(0.25, loaded.Y[1], 12);
    }

    [Fact]
    public void ContourFromWkt_StillReadsPolygon()
    {
        var rect = new Contour([-0.1, 0.1, 0.1, -0.1, -0.1], [-0.2, -0.2, 0.2, 0.2, -0.2], "хомут");

        var loaded = new Contour(rect.WKT, "хомут");

        Assert.False(loaded.IsPolyline);
        Assert.True(loaded.IsClosed);
    }

    [Fact]
    public void Polyline_WithSingleVertex_Throws()
    {
        Assert.Throws<ArgumentException>(() => Contour.Polyline([0.0], [0.0], "срез"));
    }

    [Fact]
    public void Polyline_CloneForCalc_PreservesTwoVerticesAndLineString()
    {
        var source = Contour.Polyline([0.01, 0.03], [-0.2, 0.25], "срез");

        var clone = source.CloneForCalc();

        Assert.True(clone.IsPolyline);
        Assert.Equal(2, clone.X.Count);
        Assert.Equal(2, clone.Y.Count);
        Assert.Equal(0.01, clone.X[0], 12);
        Assert.Equal(0.25, clone.Y[1], 12);
        Assert.StartsWith("LINESTRING", clone.WKT);
    }

    [Fact]
    public void CloneForCalc_DoesNotShareCoordinateLists()
    {
        var source = Contour.Polyline([0.0, 0.0], [-0.2, 0.2], "срез");

        var clone = source.CloneForCalc();
        clone.X[0] = 0.5;

        Assert.Equal(0.0, source.X[0], 12);
    }

    [Fact]
    public void ClosedContour_CloneForCalc_StillWorks()
    {
        var rect = new Contour([-0.1, 0.1, 0.1, -0.1, -0.1], [-0.2, -0.2, 0.2, 0.2, -0.2], "хомут");

        var clone = rect.CloneForCalc();

        Assert.False(clone.IsPolyline);
        Assert.Equal(5, clone.X.Count);
        Assert.True(clone.IsClosed);
    }
}
