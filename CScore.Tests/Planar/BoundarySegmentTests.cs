using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public class BoundarySegmentTests
{
    [Fact]
    public void Validate_AcceptsSegmentWithinRange()
    {
        var seg = new BoundarySegment { Loop = BoundaryLoop.Outer, StartVertex = 0, EndVertex = 1, Role = BoundaryRole.Support };
        seg.Validate(loopVertexCount: 4);
    }

    [Fact]
    public void Validate_ThrowsWhenStartVertexOutOfRange()
    {
        var seg = new BoundarySegment { StartVertex = 4, EndVertex = 0 };
        Assert.Throws<InvalidOperationException>(() => seg.Validate(loopVertexCount: 4));
    }

    [Fact]
    public void Validate_ThrowsWhenEndVertexOutOfRange()
    {
        var seg = new BoundarySegment { StartVertex = 0, EndVertex = 4 };
        Assert.Throws<InvalidOperationException>(() => seg.Validate(loopVertexCount: 4));
    }

    [Fact]
    public void Validate_ThrowsForZeroLengthSegment()
    {
        var seg = new BoundarySegment { StartVertex = 2, EndVertex = 2 };
        Assert.Throws<InvalidOperationException>(() => seg.Validate(loopVertexCount: 4));
    }

    [Fact]
    public void Validate_ThrowsWhenLoopHasFewerThanThreeVertices()
    {
        var seg = new BoundarySegment { StartVertex = 0, EndVertex = 1 };
        Assert.Throws<InvalidOperationException>(() => seg.Validate(loopVertexCount: 2));
    }

    [Fact]
    public void DefaultRole_IsUnclassified()
    {
        var seg = new BoundarySegment();
        Assert.Equal(BoundaryRole.Unclassified, seg.Role);
    }
}
