using CScore.ShellSimpl;
using Xunit;
using static CScore.Tests.ShellSimpl.StripNdmFixture;

namespace CScore.Tests.ShellSimpl;

/// <summary>Табулированный контур ПС1 (<see cref="StripNdmUlsTable"/>) против прямого <see cref="StripNdm.UltimateFactor"/>.</summary>
public sealed class StripNdmUlsTableTests
{
    static readonly StripNdmLimits Lim = Limits();

    [Theory]
    [InlineData(0.0, 30.0, 1.0, 1.0)]
    [InlineData(0.0, -45.0, 1.0, 1.0)]
    [InlineData(-800.0, 40.0, 1.0, 0.5)]
    [InlineData(-3000.0, 10.0, 2.0, 2.0)]
    [InlineData(150.0, 10.0, 1.0, 1.0)]
    [InlineData(250.0, -3.0, 0.7, 1.4)]
    [InlineData(60.0, 25.0, 3.0, 0.0)]
    [InlineData(-200.0, 120.0, 4.0, 2.0)]
    [InlineData(0.0, 15.0, 0.0, 1.0)]
    public void Eta_MatchesDirectRay(double n, double m, double kTop, double kBot)
    {
        double asTop = kTop * AsPerMeter, asBot = kBot * AsPerMeter;
        var table = StripNdm.Tabulate(Strip(CalcType.C, asTop: 0.0, asBot: 0.0), Lim);
        double direct = StripNdm.UltimateFactor(Strip(CalcType.C, asTop: asTop, asBot: asBot), n, m, Lim).Eta;
        double fast = table.Eta([asTop, asBot], n, m);
        Assert.Equal(direct, fast, Math.Max(1e-3 * direct, 1e-9));
    }

    [Fact]
    public void ZeroLoad_EtaIsZero()
    {
        var table = StripNdm.Tabulate(Strip(CalcType.C), Lim);
        Assert.Equal(0.0, table.Eta([AsPerMeter, AsPerMeter], 0.0, 0.0));
    }
}
