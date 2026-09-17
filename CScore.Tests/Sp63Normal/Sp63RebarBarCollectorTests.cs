using CScore;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Контракт сбора стержней, не зависящего от оси изгиба.</summary>
public sealed class Sp63RebarBarCollectorTests
{
    [Fact]
    public void TryCollectBars_ReturnsAllBarsWithZeroCoordinate()
    {
        var section = Sp63NormalFixtures.TwoLayerRectangle(0.3, 0.6, 0.001, 0.0005);

        Assert.True(Sp63RebarBarCollector.TryCollectBars(section, CalcType.C,
            out var bars, out var message));
        Assert.Null(message);
        Assert.Equal(4, bars.Count);
        Assert.All(bars, bar => Assert.Equal(0.0, bar.Coordinate));
        Assert.All(bars, bar => Assert.Equal(435_000.0, bar.Rs));
    }

    [Fact]
    public void TryCollectBars_RejectsMixedResistance()
    {
        var section = Sp63NormalFixtures.TwoLayerRectangle(0.3, 0.6, 0.001, 0.0005,
            useDifferentRebar: true);

        Assert.False(Sp63RebarBarCollector.TryCollectBars(section, CalcType.C,
            out _, out var message));
        Assert.Equal("mixed_rebar_resistance", message!.Code);
    }

    [Fact]
    public void TryCollectBars_RejectsPrestressedRebar()
    {
        var section = Sp63NormalFixtures.TwoLayerRectangle(0.3, 0.6, 0.001, 0.0005,
            sigSp: 100_000.0);

        Assert.False(Sp63RebarBarCollector.TryCollectBars(section, CalcType.C,
            out _, out var message));
        Assert.Equal("prestressed_rebar", message!.Code);
    }

    [Fact]
    public void TryCollect_StillGroupsLayersByAxis()
    {
        var section = Sp63NormalFixtures.TwoLayerRectangle(0.3, 0.6, 0.001, 0.0005);

        Assert.True(Sp63RebarBarCollector.TryCollect(section, Sp63NormalAxis.Mx,
            CalcType.C, out var layers, out _));
        Assert.Equal(2, layers.Count);
        Assert.Equal(-0.25, layers[0].Coordinate, 12);
        Assert.Equal(0.25, layers[1].Coordinate, 12);
    }
}
