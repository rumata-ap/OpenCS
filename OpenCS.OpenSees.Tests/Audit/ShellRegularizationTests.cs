using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests.Audit;

public sealed class ShellRegularizationTests
{
    private static NormalizedShellNode[] QuadNodes() =>
    [
        new(1, 0, 0, 0, new bool[6], null),
        new(2, 2, 0, 0, new bool[6], null),
        new(3, 2, 1, 0, new bool[6], null),
        new(4, 0, 1, 0, new bool[6], null)
    ];

    private static NormalizedShellNode[] TriNodes() =>
    [
        new(1, 0, 0, 0, new bool[6], null),
        new(2, 2, 0, 0, new bool[6], null),
        new(3, 0, 1, 0, new bool[6], null)
    ];

    [Fact]
    public void CharacteristicLength_Q4_IsSquareRootOfArea()
    {
        var element = new NormalizedShellElement(10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4],
            20, "s", ShellFrame.Identity, ShellIntegrationPolicy.Full, "q4");

        ShellElementCharacteristicLength length =
            ShellCharacteristicLength.Compute(element, QuadNodes().ToDictionary(node => node.Tag));

        Assert.Equal(2.0, length.Area, 12);
        Assert.Equal(Math.Sqrt(2.0), length.CharacteristicLength, 12);
    }

    [Fact]
    public void CharacteristicLength_T3_IsSquareRootOfArea()
    {
        var element = new NormalizedShellElement(11, ShellElementKind.ASDShellT3, [1, 2, 3],
            20, "s", ShellFrame.Identity, ShellIntegrationPolicy.Reduced, "t3");

        ShellElementCharacteristicLength length =
            ShellCharacteristicLength.Compute(element, TriNodes().ToDictionary(node => node.Tag));

        Assert.Equal(1.0, length.Area, 12);
        Assert.Equal(1.0, length.CharacteristicLength, 12);
    }

    [Fact]
    public void CharacteristicLength_DegenerateElement_Throws()
    {
        var element = new NormalizedShellElement(12, ShellElementKind.ASDShellT3, [1, 1, 1],
            20, "s", ShellFrame.Identity, ShellIntegrationPolicy.Reduced, "degenerate");

        Assert.Throws<ArgumentException>(() =>
            ShellCharacteristicLength.Compute(element, TriNodes().ToDictionary(node => node.Tag)));
    }

    [Fact]
    public void Capability_EmptyRegistry_SupportsNothing()
    {
        var capability = new ShellRegularizationCapability([]);

        Assert.False(capability.CanApply(ShellRegularizationMode.CrackBand));
        Assert.False(capability.CanApply(ShellRegularizationMode.ElementCharacteristicLength));
        Assert.Empty(capability.SupportedModes);
    }

    [Fact]
    public void Capability_FakeAdapter_MatchesModeAndSpec()
    {
        var capability = new ShellRegularizationCapability([new FakeCrackBandAdapter()]);
        var spec = new ElasticIsotropicShellMaterialSpec(30e9, 0.2);

        Assert.True(capability.CanApply(ShellRegularizationMode.CrackBand));
        Assert.False(capability.CanApply(ShellRegularizationMode.FractureEnergy));
        Assert.True(capability.CanApplyTo(ShellRegularizationMode.CrackBand, spec));
        Assert.Equal([ShellRegularizationMode.CrackBand], capability.SupportedModes);
    }

    private sealed class FakeCrackBandAdapter : IShellRegularizedMaterialAdapter
    {
        public ShellRegularizationMode Mode => ShellRegularizationMode.CrackBand;

        public bool CanApply(NativeShellMaterialSpec spec) => true;
    }
}
