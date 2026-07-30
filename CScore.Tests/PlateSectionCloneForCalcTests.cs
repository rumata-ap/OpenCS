using CScore;
using Xunit;

namespace CScore.Tests;

public class PlateSectionCloneForCalcTests
{
    [Fact]
    public void CloneForCalc_RebarLayers_AreIndependentCopies()
    {
        var source = new PlateSection
        {
            RebarLayers = [new PlateRebarLayer { Name = "L1", Asx = 0.001 }]
        };

        var clone = source.CloneForCalc();
        clone.RebarLayers[0].Asx = 0.999;

        Assert.Equal(0.001, source.RebarLayers[0].Asx);
        Assert.NotSame(source.RebarLayers[0], clone.RebarLayers[0]);
    }
}
