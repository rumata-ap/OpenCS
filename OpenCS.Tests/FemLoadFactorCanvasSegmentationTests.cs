using OpenCS.Views.Helpers;
using Xunit;

namespace OpenCS.Tests;

public class FemLoadFactorCanvasSegmentationTests
{
    [Fact]
    public void GroupBySegment_ConsecutiveSameSegment_OneGroup()
    {
        var points = new[]
        {
            (X: 0.0, Y: 0.1, Converged: true, SegmentId: 0),
            (X: 0.01, Y: 0.2, Converged: true, SegmentId: 0),
            (X: 0.02, Y: 0.3, Converged: true, SegmentId: 0),
        };
        var groups = FemLoadFactorCanvas.GroupBySegment(points);
        Assert.Single(groups);
        Assert.Equal(3, groups[0].Count);
    }

    [Fact]
    public void GroupBySegment_SegmentChange_TwoGroups()
    {
        var points = new[]
        {
            (X: 0.0, Y: 0.1, Converged: true, SegmentId: 0),
            (X: 0.01, Y: 0.2, Converged: true, SegmentId: 0),
            (X: 0.5, Y: 0.1, Converged: true, SegmentId: 1),
            (X: 0.6, Y: 0.2, Converged: true, SegmentId: 1),
        };
        var groups = FemLoadFactorCanvas.GroupBySegment(points);
        Assert.Equal(2, groups.Count);
        Assert.Equal([0, 1], groups[0]);
        Assert.Equal([2, 3], groups[1]);
    }

    [Fact]
    public void GroupBySegment_NaNPointsExcludedFromAllGroups()
    {
        var withNaN = new[] { (X: double.NaN, Y: 0.1, Converged: true, SegmentId: -1), (X: 0.01, Y: 0.2, Converged: true, SegmentId: 0) };
        var groups = FemLoadFactorCanvas.GroupBySegment(withNaN);
        Assert.Single(groups);
        Assert.Equal([1], groups[0]);
    }
}
