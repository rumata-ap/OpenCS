using CScore.Fem;
using Xunit;
using CScore.Fem.Fragment;
using CScore.Planar;

namespace CScore.Tests.Fem.Fragment;

public class FragmentStoreCodecTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Points_RoundTrip(int n)
    {
        var points = Enumerable.Range(0, n).Select(i => new PlanarVector3(i + 0.1, -i * 2.5, 3.0 + i / 3.0)).ToList();
        var back = FragmentStoreCodec.DecodePoints(FragmentStoreCodec.EncodePoints(points));
        Assert.Equal(points, back);
    }

    [Fact]
    public void Points_BrokenLength_Throws() =>
        Assert.Throws<ArgumentException>(() => FragmentStoreCodec.DecodePoints(new byte[20]));

    [Fact]
    public void Entries_RoundTrip()
    {
        FragmentFieldEntry[] entries =
        [
            new("101", [-5000.0]),
            new("КЭ·7", [1, 2, 3, 4]),
            new("", []),
        ];
        var back = FragmentStoreCodec.DecodeEntries(FragmentStoreCodec.EncodeEntries(entries));
        Assert.Equal(entries.Length, back.Count);
        for (int i = 0; i < entries.Length; i++)
        {
            Assert.Equal(entries[i].RefTag, back[i].RefTag);
            Assert.Equal(entries[i].Values, back[i].Values);
        }
        Assert.Empty(FragmentStoreCodec.DecodeEntries(FragmentStoreCodec.EncodeEntries([])));
    }

    [Fact]
    public void Entries_Truncated_Throws()
    {
        var bytes = FragmentStoreCodec.EncodeEntries([new("1", [1.0, 2.0])]);
        Assert.Throws<ArgumentException>(() => FragmentStoreCodec.DecodeEntries(bytes[..^4]));
        Assert.Throws<ArgumentException>(() => FragmentStoreCodec.DecodeEntries([.. bytes, 0]));
    }

    [Fact]
    public void BuildReport_RoundTrip()
    {
        FemValidationDiagnostic[] diags =
        [
            new("fragment_no_slab", "В полосе нет плиты", true, ["S1", "S2"]),
            new("fragment_releases_lost", "Шарниры", false),
        ];
        var back = StoreyFragmentBuildReport.FromJson(StoreyFragmentBuildReport.ToJson(diags));
        Assert.Equal(2, back.Count);
        Assert.Equal(diags[0].Code, back[0].Code);
        Assert.Equal(diags[0].Message, back[0].Message);
        Assert.True(back[0].IsError);
        Assert.Equal(["S1", "S2"], back[0].SourceKeys!);
        Assert.False(back[1].IsError);
        Assert.Null(back[1].SourceKeys);
        Assert.Empty(StoreyFragmentBuildReport.FromJson(null));
        Assert.Empty(StoreyFragmentBuildReport.FromJson("{битый"));
    }
}
