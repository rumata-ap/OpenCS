using CScore.Planar;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class PlanarPointSnapTests
{
    static readonly (double U, double V)[] Square = [(0, 0), (4, 0), (4, 4), (0, 4)];

    [Fact]
    public void NearVertex_SnapsToVertex()
    {
        var r = PlanarPointSnap.Snap(3.95, 0.04, Square, [Square], toleranceM: 0.1);

        Assert.Equal(PlanarSnapKind.Vertex, r.Kind);
        Assert.Equal((4.0, 0.0), (r.U, r.V));
    }

    [Fact]
    public void NearEdgeMidpoint_SnapsToMidpoint()
    {
        var r = PlanarPointSnap.Snap(2.02, 0.03, Square, [Square], toleranceM: 0.1);

        Assert.Equal(PlanarSnapKind.Midpoint, r.Kind);
        Assert.Equal((2.0, 0.0), (r.U, r.V));
    }

    [Fact]
    public void VertexBeatsMidpoint_AndFarPointIsUntouched()
    {
        Assert.Equal(PlanarSnapKind.None, PlanarPointSnap.Snap(1.3, 2.7, Square, [Square], 0.1).Kind);
        // Вершина ближе середины ребра: тот же допуск, но ближайшее — вершина.
        var r = PlanarPointSnap.Snap(0.05, 0.02, Square, [Square], toleranceM: 3.0);
        Assert.Equal(PlanarSnapKind.Vertex, r.Kind);
    }

    [Fact]
    public void Ortho_AlignsWithAnchorWithinAngleTolerance()
    {
        // От якоря (1, 1) почти вдоль U: v приводится к 1.
        var r = PlanarPointSnap.Snap(3.0, 1.05, [], [], toleranceM: 0.01, anchor: (1, 1));

        Assert.Equal(PlanarSnapKind.Ortho, r.Kind);
        Assert.Equal((3.0, 1.0), (r.U, r.V));
    }

    [Fact]
    public void Ortho_NotAppliedAtLargeAngle()
    {
        var r = PlanarPointSnap.Snap(3.0, 2.5, [], [], toleranceM: 0.01, anchor: (1, 1));

        Assert.Equal(PlanarSnapKind.None, r.Kind);
    }

    [Fact]
    public void Grid_RoundsToStep()
    {
        var r = PlanarPointSnap.Snap(1.26, 0.74, [], [], toleranceM: 0.01, gridStepM: 0.25);

        Assert.Equal(PlanarSnapKind.Grid, r.Kind);
        Assert.Equal((1.25, 0.75), (r.U, r.V));
    }
}
