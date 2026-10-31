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
}
