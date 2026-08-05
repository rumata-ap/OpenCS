using Xunit;

namespace CScore.Tests;

public sealed class GridSplitTests
{
    [Fact]
    public void SplitWoundPolygon_SingleContiguousResult_ReturnsOnePart()
    {
        var square = new List<(double X, double Y)> { (0, 0), (10, 0), (10, 10), (0, 10) };
        var clipped = GridSplit.ClipByRect(square, 2, 8, 2, 8);
        var spikeless = GridSplit.RemoveSpikes(clipped);

        var parts = GridSplit.SplitWoundPolygon(spikeless, 2, 8, 2, 8);

        var part = Assert.Single(parts);
        Assert.Equal(4, part.Count);
    }

    [Fact]
    public void SplitWoundPolygon_ClipEntirelyOutside_ReturnsNoParts()
    {
        var square = new List<(double X, double Y)> { (0, 0), (10, 0), (10, 10), (0, 10) };
        var clipped = GridSplit.ClipByRect(square, 20, 26, 20, 26);
        var spikeless = GridSplit.RemoveSpikes(clipped);

        var parts = GridSplit.SplitWoundPolygon(spikeless, 20, 26, 20, 26);

        Assert.Empty(parts);
    }

    [Fact]
    public void SplitWoundPolygon_NotchedHullClippedByWideRect_ReturnsTwoParts()
    {
        // Тот же "скобообразный" Hull, что и в PlateStripGeometryBuilderTests: у y=8
        // сечение по x — два непересекающихся интервала [0,4] и [6,10].
        var notched = new List<(double X, double Y)>
        {
            (0, 0), (10, 0), (10, 10), (6, 10), (6, 3), (4, 3), (4, 10), (0, 10)
        };
        var clipped = GridSplit.ClipByRect(notched, 1, 9, 7.5, 8.5);
        var spikeless = GridSplit.RemoveSpikes(clipped);

        var parts = GridSplit.SplitWoundPolygon(spikeless, 1, 9, 7.5, 8.5);

        Assert.Equal(2, parts.Count);
    }
}
