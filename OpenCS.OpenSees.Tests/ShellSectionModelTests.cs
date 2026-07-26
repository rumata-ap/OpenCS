using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class ShellSectionModelTests
{
    [Fact]
    public void LayeredSection_RejectsNonPositiveThickness()
    {
        var section = new RCShellLayeredSection(
            20, "source", 0.2, ShellFrame.Identity,
            [new RCShellLayer(0, ShellLayerKind.Concrete, 0, 0, 1, 0, "c")],
            ShellMappingMode.Exact, [], "fingerprint");

        var ex = Assert.Throws<InvalidOperationException>(() => section.Validate());

        Assert.Contains("толщ", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Model_RejectsElementWithUnknownSection()
    {
        var model = new ShellOpenSeesModel
        {
            Nodes = [
                new NormalizedShellNode(1, 0, 0, 0, [true, true, true, true, true, true], null),
                new NormalizedShellNode(2, 1, 0, 0, [false, false, false, false, false, false], null),
                new NormalizedShellNode(3, 1, 1, 0, [false, false, false, false, false, false], null),
                new NormalizedShellNode(4, 0, 1, 0, [false, false, false, false, false, false], null)],
            Sections = [],
            Elements = [new NormalizedShellElement(
                10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], 20, "missing",
                ShellFrame.Identity, ShellIntegrationPolicy.Full, null)]
        };

        var ex = Assert.Throws<InvalidOperationException>(() => model.Validate());

        Assert.Contains("секц", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ElasticMaterial_EmitsDeterministicNdMaterialCommand()
    {
        var spec = new ElasticIsotropicShellMaterialSpec(30e9, 0.25);

        Assert.Equal("nDMaterial ElasticIsotropic 7 30000000000 0.25", spec.ToTcl(7));
        Assert.Equal(spec.Fingerprint, new ElasticIsotropicShellMaterialSpec(30e9, 0.25).Fingerprint);
    }
}
