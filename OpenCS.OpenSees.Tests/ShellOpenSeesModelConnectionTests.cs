using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class ShellOpenSeesModelConnectionTests
{
    [Fact]
    public void Validate_RejectsLoadOnUnknownNode()
    {
        var model = MinimalQ4Model() with
        {
            Loads = [new ShellNodalLoad(999, 0, 0, -1000, 0, 0, 0)]
        };

        var ex = Assert.Throws<InvalidOperationException>(() => model.Validate());

        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public void Validate_RejectsNonFiniteLoadComponent()
    {
        var model = MinimalQ4Model() with
        {
            Loads = [new ShellNodalLoad(2, 0, 0, double.NaN, 0, 0, 0)]
        };

        var ex = Assert.Throws<InvalidOperationException>(() => model.Validate());

        Assert.Contains("конечны", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    internal static ShellOpenSeesModel MinimalQ4Model()
    {
        const string fingerprint = "conn-fixture-section";
        return new ShellOpenSeesModel
        {
            Nodes = [
                new(1, 0, 0, 0, [true, true, true, true, true, true], null),
                new(2, 1, 0, 0, new bool[6], null),
                new(3, 1, 1, 0, new bool[6], null),
                new(4, 0, 1, 0, [true, true, true, true, true, true], null)],
            Materials = [new(1, "conn", new ElasticIsotropicShellMaterialSpec(30e9, 0.2))],
            Sections = [new(20, "conn", 0.2, ShellFrame.Identity,
                [
                    new(0, ShellLayerKind.Concrete, -0.05, 0.1, 1, 0, "c0"),
                    new(1, ShellLayerKind.Concrete, 0.05, 0.1, 1, 0, "c1")
                ],
                ShellMappingMode.Exact, [], fingerprint)],
            Elements = [new(10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], 20, fingerprint,
                ShellFrame.Identity, ShellIntegrationPolicy.Full, null)]
        };
    }
}
