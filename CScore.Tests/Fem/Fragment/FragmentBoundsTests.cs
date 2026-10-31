using CScore.Fem.Fragment;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem.Fragment;

public sealed class FragmentBoundsTests
{
    static PlanarVector3 V(double x, double y, double z) => new(x, y, z);

    [Fact]
    public void NoPrism_BandOnly()
    {
        var b = new FragmentBounds(1.5, 4.5, null, 1e-6);
        Assert.Equal(2, b.Planes.Count);
        Assert.True(b.InBox(V(100, -50, 3)));
        Assert.False(b.InBox(V(0, 0, 4.6)));
        var (lo, hi) = b.ClipRange(V(0, 0, 0), V(0, 0, 1), 0, 6);
        Assert.Equal((1.5, 4.5), (lo, hi));
    }

    [Fact]
    public void Prism_ClipRangeAndPolygon()
    {
        var b = new FragmentBounds(1.5, 4.5, FragmentPrism.Create(3, 3, 1, 0, 1.5, 1.5), 1e-6);
        Assert.Equal(6, b.Planes.Count);
        Assert.True(b.InBox(V(4.5, 1.5, 3)));
        Assert.False(b.InBox(V(4.6, 3, 3)));
        // Балка вдоль X на z = 3 от 0 до 6 — внутри 1,5…4,5.
        var (lo, hi) = b.ClipRange(V(0, 3, 3), V(1, 0, 0), 0, 6);
        Assert.Equal(1.5, lo, 9);
        Assert.Equal(4.5, hi, 9);
        // Луч вдоль грани снаружи — пусто.
        var (lo2, hi2) = b.ClipRange(V(0, 5, 3), V(1, 0, 0), 0, 6);
        Assert.True(hi2 <= lo2);
        // Плита 6×6 на z = 3 → квадрат 3×3 с центром (3, 3).
        var poly = b.ClipPolygon([V(0, 0, 3), V(6, 0, 3), V(6, 6, 3), V(0, 6, 3)]);
        var (area, c) = FragmentBounds.AreaCentroid(poly);
        Assert.Equal(9, area, 9);
        Assert.Equal(3, c.X, 9);
        Assert.Equal(3, c.Y, 9);
        Assert.Empty(b.ClipPolygon([V(5, 5, 3), V(6, 5, 3), V(6, 6, 3)]));
    }

    [Fact]
    public void RotatedPrism_PolygonArea()
    {
        // Квадрат 2×2 под 45° внутри большого — площадь 4.
        var b = new FragmentBounds(0, 1, FragmentPrism.Create(0, 0, 1, 1, 1, 1), 1e-6);
        var poly = b.ClipPolygon([V(-5, -5, 0.5), V(5, -5, 0.5), V(5, 5, 0.5), V(-5, 5, 0.5)]);
        Assert.Equal(4, FragmentBounds.AreaCentroid(poly).Area, 9);
    }

    [Fact]
    public void SideLabels()
    {
        var cut = new FragmentCut("S1", FragmentCutKind.Edge, FragmentCutSide.Bottom, 1.5, null, []);
        Assert.Equal("низ", FragmentCutLabel.Side(cut));
        Assert.Equal("верх", FragmentCutLabel.Side(cut with { Side = FragmentCutSide.Top }));
        Assert.Equal("бок, грань +X", FragmentCutLabel.Side(cut with { Side = FragmentCutSide.Lateral, Normal = V(1, 0, 0) }));
        Assert.Equal("бок, грань −Y", FragmentCutLabel.Side(cut with { Side = FragmentCutSide.Lateral, Normal = V(0, -1, 0) }));
        double s = Math.Sqrt(0.5);
        Assert.StartsWith("бок, ∠45", FragmentCutLabel.Side(cut with { Side = FragmentCutSide.Lateral, Normal = V(s, s, 0) }));
    }
}
