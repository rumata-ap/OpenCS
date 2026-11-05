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

    /// <summary>Центр сечений: четыре боковые кромки квадрата 2×2 вокруг (5, 7) на z = 3 → (5, 7, 3); без сечений — запасная точка.</summary>
    [Fact]
    public void Origin_IsCentreOfCuts()
    {
        PlanarVector3 P(double x, double y) => V(x, y, 3);
        FragmentMaskCutInput Edge((PlanarVector3, PlanarVector3) s) =>
            new(new FragmentCut("s", FragmentCutKind.Edge, FragmentCutSide.Lateral, 3, null, [s]), null, V(0, 0, 1));
        FragmentMaskCutInput[] cuts =
        [
            Edge((P(6, 6), P(6, 8))), Edge((P(6, 8), P(4, 8))), Edge((P(4, 8), P(4, 6))), Edge((P(4, 6), P(6, 6))),
        ];

        var o = FragmentMaskBalance.Origin(cuts, V(100, 100, 100));

        Assert.Equal(5, o.X, 12);
        Assert.Equal(7, o.Y, 12);
        Assert.Equal(3, o.Z, 12);
        Assert.Equal(V(1, 2, 3), FragmentMaskBalance.Origin([], V(1, 2, 3)));
    }

    /// <summary>
    /// Небаланс только по моменту (силы сходятся: +60/+40 кН против −100 кН, момент −40 кН·м): у центра сечений — 20 %,
    /// предупреждение; у далёкой точки (30 м) знаменатель — сила × 30 м, и тот же небаланс тонет (&lt; 1 %).
    /// </summary>
    [Fact]
    public void Check_MomentImbalance_VisibleOnlyAtCutsCentre()
    {
        FragmentMaskCutInput[] cuts = [Col("a", FragmentCutSide.Bottom, 0, 1.5), Col("b", FragmentCutSide.Bottom, 4, 1.5)];
        List<FragmentCutMask> masks =
        [
            new(0, K, [new FragmentMaskSample(0, null, new Dof6(0, 0, 60e3, 0, 0, 0))]),
            new(1, K, [new FragmentMaskSample(0, null, new Dof6(0, 0, 40e3, 0, 0, 0))]),
        ];
        Dictionary<FragmentMaskKey, (PlanarVector3, PlanarVector3)> Loads(PlanarVector3 o) => new()
        {
            [K] = (V(0, 0, -100e3), (V(2, 0, 3) - o).Cross(V(0, 0, -100e3))),
        };
        var centre = FragmentMaskBalance.Origin(cuts, PlanarVector3.Zero);
        var far = V(30, 0, 0);

        var near = FragmentMaskBalance.Check(cuts, masks, Loads(centre), centre);
        var distant = FragmentMaskBalance.Check(cuts, masks, Loads(far), far);

        var d = Assert.Single(near);
        Assert.Contains("по моменту 20", d.Message.Replace(',', '.'));
        Assert.Empty(distant);
    }
}
