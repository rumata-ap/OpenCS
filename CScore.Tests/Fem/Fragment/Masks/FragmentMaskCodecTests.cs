using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Fem.Fragment.Masks;

public sealed class FragmentMaskCodecTests
{
    [Fact]
    public void Samples_RoundTrip_WithMissingParts()
    {
        FragmentMaskSample[] samples =
        [
            new(0, new Dof6(1e-3, -2e-3, 3e-3, 1e-4, 2e-4, -3e-4), new Dof6(10, 20, -30, 1, 2, 3)),
            new(0.75, null, new Dof6(-1, 0, 5, 0, 0, 0.5)),
            new(1.5, new Dof6(0, 0, -1e-3, 0, 0, 0), null),
        ];

        var back = FragmentMaskCodec.Decode(FragmentMaskCodec.Encode(samples));

        Assert.Equal(samples, back);
    }

    [Fact]
    public void Decode_UnknownVersion_Throws()
    {
        var bytes = FragmentMaskCodec.Encode([new(0, Dof6.Zero, null)]);
        bytes[0] = 99;
        Assert.Throws<InvalidDataException>(() => FragmentMaskCodec.Decode(bytes));
    }

    [Fact]
    public void Table_ReturnsOnlyWhatWasSet()
    {
        var key = FragmentMaskKey.OfLoadCase(7);
        var t = new FragmentParentResultTable();
        t.AddKey(key);
        t.SetDisplacement(key, "5", new Dof6(1, 2, 3, 0, 0, 0));
        t.AddBeamLoad(key, "e1", FragmentBeamSpanLoad.Point(0.5, new PlanarVector3(0, 0, -1), PlanarVector3.Zero));

        Assert.True(t.TryNodeDisplacement(key, "5", out var u));
        Assert.Equal(3, u.Z);
        Assert.False(t.TryNodeDisplacement(key, "6", out _));
        Assert.False(t.TryNodeDisplacement(FragmentMaskKey.OfLoadCase(8), "5", out _));
        Assert.False(t.TryBeamEndForces(key, "e1", out _));
        Assert.Single(t.BeamLoads(key, "e1"));
        Assert.Empty(t.BeamLoads(key, "e2"));
        Assert.Equal(new[] { key }, t.Keys);
        Assert.Equal(FragmentMaskKey.OfStage("{\"a\":1}"), FragmentMaskKey.OfStage("{\"a\":1}"));
    }
}
