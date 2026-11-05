using CScore.Fem.Fragment;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem.Fragment;

public sealed class FragmentElementShareTests
{
    static PlanarVector3 V(double x, double y, double z) => new(x, y, z);
    static readonly FragmentBounds Band = new(0, 1, null, 1e-3);

    [Fact]
    public void Bar_InsideCrossingOutsideAndOnPlane()
    {
        var whole = FragmentElementShare.Of(Band, [V(0, 0, 0.2), V(0, 0, 0.8)]);
        Assert.True(whole is { Whole: true, Share: 1 });

        var half = FragmentElementShare.Of(Band, [V(0, 0, -0.5), V(0, 0, 0.5)])!.Value;
        Assert.False(half.Whole);
        Assert.Equal(0.5, half.Share, 12);
        Assert.Equal(0.25, half.Centroid.Z, 12);

        Assert.Null(FragmentElementShare.Of(Band, [V(0, 0, -1), V(0, 0, -0.5)]));
        // Конец на плоскости z₁ снизу — КЭ отброшенной части (как в нагрузках полосы).
        Assert.Null(FragmentElementShare.Of(Band, [V(0, 0, -0.5), V(0, 0, 0)]));
    }

    [Fact]
    public void Shell_WallAcrossBottom_AndSlabAcrossPrismFace()
    {
        var wall = FragmentElementShare.Of(Band, [V(0, 0, -1), V(1, 0, -1), V(1, 0, 1), V(0, 0, 1)])!.Value;
        Assert.Equal(0.5, wall.Share, 12);
        Assert.Equal(0.5, wall.Centroid.Z, 12);

        var prism = new FragmentBounds(0, 1, FragmentPrism.Create(0, 0, 1, 0, 1, 1), 1e-3);
        var slab = FragmentElementShare.Of(prism, [V(0, -0.5, 0.5), V(2, -0.5, 0.5), V(2, 0.5, 0.5), V(0, 0.5, 0.5)])!.Value;
        Assert.Equal(0.5, slab.Share, 12);
        Assert.Equal(0.5, slab.Centroid.X, 12);
    }
}
