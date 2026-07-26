using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Tests;

public sealed class ShellTclGeneratorTests
{
    [Fact]
    public void Generate_Q4EmitsSixDofModelLayeredShellAndLocalFrame()
    {
        var script = new ShellTclGenerator().Generate(Q4Model());

        Assert.Contains("model Basic -ndm 3 -ndf 6", script);
        Assert.Contains("section LayeredShell 20", script);
        Assert.Contains("element ASDShellQ4 10 1 2 3 4 20", script);
        Assert.Contains("-local 1 0 0", script);
        Assert.Contains("set shell_section_forces", script);
    }

    [Fact]
    public void Generate_T3ReducedEmitsReducedIntegrationAndThreeNodes()
    {
        var script = new ShellTclGenerator().Generate(T3ReducedModel());

        Assert.Contains("element ASDShellT3 11 1 2 3 20 -reducedIntegration", script);
        Assert.DoesNotContain("ASDShellT3 11 1 2 3 4", script);
    }

    [Fact]
    public void Generate_IsDeterministic()
    {
        var generator = new ShellTclGenerator();

        Assert.Equal(generator.Generate(Q4Model()), generator.Generate(Q4Model()));
    }

    [Fact]
    public void Generate_RejectsLayeredShellWithFewerThanThreeLayers()
    {
        var model = Model(
            10,
            ShellElementKind.ASDShellQ4,
            ShellIntegrationPolicy.Full,
            [1, 2, 3, 4],
            layerCount: 2);

        var ex = Assert.Throws<InvalidOperationException>(() => new ShellTclGenerator().Generate(model));

        Assert.Contains("три слоя", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ShellOpenSeesModel Q4Model() => Model(
        10,
        ShellElementKind.ASDShellQ4,
        ShellIntegrationPolicy.Full,
        [1, 2, 3, 4]);

    private static ShellOpenSeesModel T3ReducedModel() => Model(
        11,
        ShellElementKind.ASDShellT3,
        ShellIntegrationPolicy.Reduced,
        [1, 2, 3]);

    private static ShellOpenSeesModel Model(
        int elementTag,
        ShellElementKind kind,
        ShellIntegrationPolicy integration,
        IReadOnlyList<int> connectivity,
        int layerCount = 3)
    {
        const string fingerprint = "section-fingerprint";
        double layerThickness = 0.2 / layerCount;
        return new ShellOpenSeesModel
        {
            Nodes = [
                new(1, 0, 0, 0, [true, true, true, true, true, true], null),
                new(2, 1, 0, 0, [true, true, true, true, true, true], null),
                new(3, 1, 1, 0, [false, false, false, false, false, false], null),
                new(4, 0, 1, 0, [false, false, false, false, false, false], null)],
            Materials = [new(1, "fixture", new ElasticIsotropicShellMaterialSpec(30e9, 0.25))],
            Sections = [new(20, "plate", 0.2, ShellFrame.Identity,
                Enumerable.Range(0, layerCount).Select(index => new RCShellLayer(
                    index,
                    ShellLayerKind.Concrete,
                    -0.1 + (index + 0.5) * layerThickness,
                    layerThickness,
                    1,
                    0,
                    $"concrete:{index}")).ToArray(),
                ShellMappingMode.Exact, [], fingerprint)],
            Elements = [new(elementTag, kind, connectivity, 20, fingerprint,
                ShellFrame.Identity, integration, "fixture")]
        };
    }
}
