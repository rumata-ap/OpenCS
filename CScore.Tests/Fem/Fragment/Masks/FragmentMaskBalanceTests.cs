using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using CScore.Submodel;
using Xunit;
using static CScore.Tests.Fem.Fragment.Masks.FragmentMaskFixtures;

namespace CScore.Tests.Fem.Fragment.Masks;

public sealed class FragmentMaskBalanceTests
{
    static readonly FragmentMaskKey K = FragmentMaskKey.OfLoadCase(1);

    static FragmentMaskCutInput Col(string tag, FragmentCutSide side, double x, double z) =>
        new(new FragmentCut(tag, FragmentCutKind.End, side, z, tag, []), V(x, 0, z), null);

    /// <summary>
    /// Две колонны (x = 0 и 4), плита с нагрузкой −100 кН в центре x = 2 на z = 3: низы держат по +40 и +40, верхи
    /// пусты, — небаланс 20 кН (20 %) → предупреждение; при +50/+50 — равновесие.
    /// </summary>
    [Fact]
    public void Check_DetectsImbalance()
    {
        FragmentMaskCutInput[] cuts = [Col("a", FragmentCutSide.Bottom, 0, 1.5), Col("b", FragmentCutSide.Bottom, 4, 1.5)];
        var loads = new Dictionary<FragmentMaskKey, (PlanarVector3, PlanarVector3)>
        {
            [K] = (V(0, 0, -100e3), V(2, 0, 3).Cross(V(0, 0, -100e3))),
        };
        List<FragmentCutMask> Masks(double ra, double rb) =>
        [
            new(0, K, [new FragmentMaskSample(0, null, new Dof6(0, 0, ra, 0, 0, 0))]),
            new(1, K, [new FragmentMaskSample(0, null, new Dof6(0, 0, rb, 0, 0, 0))]),
        ];

        var bad = FragmentMaskBalance.Check(cuts, Masks(40e3, 40e3), loads, PlanarVector3.Zero);
        var ok = FragmentMaskBalance.Check(cuts, Masks(50e3, 50e3), loads, PlanarVector3.Zero);

        var d = Assert.Single(bad);
        Assert.Equal("fragment_mask_balance", d.Code);
        Assert.False(d.IsError);
        Assert.Empty(ok);
    }
}
