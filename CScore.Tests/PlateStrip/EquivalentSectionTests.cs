using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class EquivalentSectionTests
{
    [Fact]
    public void NewFields_HaveExpectedDefaultsAndAreSettable()
    {
        var section = new EquivalentSection();

        Assert.Equal(0.5, section.SpanStationFraction, 12);
        Assert.Equal("", section.SourceRegionFingerprint);

        section.SpanStationFraction = 0.25;
        section.SourceRegionFingerprint = "abc";

        Assert.Equal(0.25, section.SpanStationFraction, 12);
        Assert.Equal("abc", section.SourceRegionFingerprint);
    }
}
