using CScore;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Построение оффсетной линии контура для центровой линии хомута.</summary>
public sealed class ContourOffsetTests
{
    [Fact]
    public void Rectangle_WithUniformOffset_ShrinksByOffset()
    {
        var hull = new Contour([-0.15, 0.15, 0.15, -0.15, -0.15], [-0.25, -0.25, 0.25, 0.25, -0.25], "hull");

        Assert.True(ContourOffset.TryOffset(hull, 0.03, out var pts, out var error));

        Assert.Null(error);
        Assert.Equal(4, pts.Count);
        Assert.Equal(-0.12, pts.Min(p => p.X), 9);
        Assert.Equal(0.12, pts.Max(p => p.X), 9);
        Assert.Equal(-0.22, pts.Min(p => p.Y), 9);
        Assert.Equal(0.22, pts.Max(p => p.Y), 9);
    }

    [Fact]
    public void Rectangle_WithPerEdgeOffsets_UsesEachEdgeValue()
    {
        var hull = new Contour([-0.15, 0.15, 0.15, -0.15, -0.15], [-0.25, -0.25, 0.25, 0.25, -0.25], "hull");
        var edges = ContourOffset.BuildEdges(hull, 0.03).ToList();
        edges[0] = edges[0] with { Offset = 0.05 };

        var pts = ContourOffset.Offset(edges);

        Assert.Equal(-0.20, pts.Min(p => p.Y), 9);
        Assert.Equal(0.22, pts.Max(p => p.Y), 9);
    }

    [Fact]
    public void ExcessiveOffset_IsRejectedWithMessage()
    {
        var hull = new Contour([-0.15, 0.15, 0.15, -0.15, -0.15], [-0.25, -0.25, 0.25, 0.25, -0.25], "hull");

        Assert.False(ContourOffset.TryOffset(hull, 0.20, out _, out var error));

        Assert.NotNull(error);
    }

    [Fact]
    public void LShapedContour_WithModerateOffset_StaysValid()
    {
        var hull = new Contour(
            [-0.15, 0.15, 0.15, 0.0, 0.0, -0.15, -0.15],
            [-0.25, -0.25, 0.0, 0.0, 0.25, 0.25, -0.25], "hull");

        Assert.True(ContourOffset.TryOffset(hull, 0.02, out var pts, out _));

        Assert.Equal(6, pts.Count);
    }
}
