using CScore.Sp63;
using Xunit;

namespace CScore.Tests;

public class CrackWidthEtaTests
{
    [Theory]
    [InlineData(70, 100, 0.7)]
    [InlineData(-70, -100, 0.7)]
    [InlineData(0, 100, 0.0)]
    [InlineData(100, 100, 1.0)]
    public void AutoPsi_RatioClamped(double ml, double mt, double expected)
        => Assert.Equal(expected, CrackWidthEta.AutoPsi(ml, mt), 9);

    [Fact]
    public void AutoPsi_ZeroTotal_ReturnsOne()
        => Assert.Equal(1.0, CrackWidthEta.AutoPsi(0, 0));

    [Fact]
    public void ScaleLongTotal_PreservesShare()
    {
        double mxLong = -70, mxTotal = -100, myLong = 35, myTotal = 50;
        double mxEff = -130, myEff = 65; // ηx=1.3, ηy=1.3
        var s = CrackWidthEta.ScaleLongTotal(mxLong, mxTotal, myLong, myTotal, mxEff, myEff);
        Assert.Equal(mxEff, s.MxTotalEff, 9);
        Assert.Equal(myEff, s.MyTotalEff, 9);
        Assert.Equal(0.7, s.MxLongEff / s.MxTotalEff, 9);
        Assert.Equal(0.7, s.MyLongEff / s.MyTotalEff, 9);
    }

    [Fact]
    public void ScaleLongTotal_ZeroTotalAxis_LeavesLongUnchanged()
    {
        var s = CrackWidthEta.ScaleLongTotal(0, 0, 10, 20, 0, 26);
        Assert.Equal(0, s.MxLongEff, 9);
        Assert.Equal(0, s.MxTotalEff, 9);
        Assert.Equal(13, s.MyLongEff, 9); // 10 * (26/20)
        Assert.Equal(26, s.MyTotalEff, 9);
    }
}
