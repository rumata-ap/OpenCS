using CScore.Fem.Fragment;
using Xunit;

namespace CScore.Tests.Fem.Fragment;

public sealed class FragmentPrismCodecTests
{
    [Fact]
    public void RoundTrip_AndEmpty()
    {
        var info = new FragmentPrismInfo(FragmentPrism.Create(3, 2, 0, 2, 1.7, 1.2), 1.5, ["C1", "W2"]);
        var back = FragmentPrismCodec.Decode(FragmentPrismCodec.Encode(info))!;
        Assert.Equal(info.Prism, back.Prism);
        Assert.Equal(1.5, back.OffsetM);
        Assert.Equal(["C1", "W2"], back.MemberTags!);

        var bare = FragmentPrismCodec.Decode(FragmentPrismCodec.Encode(new FragmentPrismInfo(info.Prism)))!;
        Assert.Null(bare.OffsetM);
        Assert.Null(bare.MemberTags);

        Assert.Null(FragmentPrismCodec.Decode(null));
        Assert.Null(FragmentPrismCodec.Decode(" "));
        Assert.Throws<InvalidDataException>(() => FragmentPrismCodec.Decode("{"));
        Assert.Throws<InvalidDataException>(() => FragmentPrismCodec.Decode("{\"cx\":1}"));
    }

    [Fact]
    public void Floors_RoundTrip_AndOldJson()
    {
        var prism = FragmentPrism.Create(0, 0, 1, 0, 1.5, 1.5);
        var range = FragmentPrismCodec.Decode(FragmentPrismCodec.Encode(new FragmentPrismInfo(prism, 1.5, ["C1"], 3, 6)))!;
        Assert.Equal((3.0, 6.0, false), (range.FloorFromZ!.Value, range.FloorToZ!.Value, range.FullHeight));
        var full = FragmentPrismCodec.Decode(FragmentPrismCodec.Encode(new FragmentPrismInfo(prism, FullHeight: true)))!;
        Assert.True(full.FullHeight);
        Assert.Null(full.FloorFromZ);

        // JSON среза 2 — без отметок перекрытий.
        var old = FragmentPrismCodec.Decode("{\"cx\":0,\"cy\":0,\"ax\":1,\"ay\":0,\"a\":1.5,\"b\":1.5,\"d\":1.5,\"members\":[\"C1\"]}")!;
        Assert.Equal((null, null, false), (old.FloorFromZ, old.FloorToZ, old.FullHeight));
        Assert.Equal(["C1"], old.MemberTags!);
    }
}
