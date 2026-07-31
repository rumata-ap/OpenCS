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
    public void Generate_EmitsPlainLinearLoadPattern()
    {
        var model = Q4Model() with
        {
            Stages = [new() { Tag = "stage-1", Loads = [new(2, 0, 0, -1000, 0, 0, 0)] }]
        };

        var script = new ShellTclGenerator().Generate(model);

        Assert.Contains("pattern Plain 1 Linear {", script);
        Assert.Contains("load 2 0 0 -1000 0 0 0", script);
    }

    [Fact]
    public void Generate_EmitsBeamElementsEqualDofAndRigidLink()
    {
        var baseModel = Q4Model();
        var model = baseModel with
        {
            Nodes = baseModel.Nodes.Concat([
                new(5, 2, 0, 1, new bool[6], null),
                new(6, 2, 0, 0, new bool[6], null)]).ToArray(),
            BeamElements = [new(100, 2, 5, 0.01, 200e9, 77e9, 1e-6, 1e-5, 1e-5, (1, 0, 0))],
            EqualDofConstraints = [new(2, 6, [1, 2, 3, 4, 5, 6])]
        };

        var script = new ShellTclGenerator().Generate(model);

        Assert.Contains("element elasticBeamColumn 100 2 5", script);
        Assert.Contains("equalDOF 2 6 1 2 3 4 5 6", script);
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

    [Fact]
    public void Generate_EmitsUniaxialDependencyBeforeReferencingPlateRebarMaterial()
    {
        // PlateRebar nDMaterial (tag 2) ссылается на uniaxialMaterial (tag 500) — тег
        // зависимости больше тега ссылающегося материала, поэтому сортировка по возрастанию
        // Tag сама по себе НЕ гарантирует порядок объявления; генератор обязан эмитировать
        // зависимость раньше независимо от численного соотношения тегов.
        var model = Q4Model() with
        {
            Materials =
            [
                new(1, "concrete", new ElasticIsotropicShellMaterialSpec(30e9, 0.2)),
                new(2, "rebar", new PlateRebarShellMaterialSpec(500, 0)),
                new(500, "rebar-uniaxial", new ElasticUniaxialShellMaterialSpec(200e9)),
            ]
        };

        var script = new ShellTclGenerator().Generate(model);

        int uniaxialIndex = script.IndexOf("uniaxialMaterial Elastic 500", StringComparison.Ordinal);
        int plateRebarIndex = script.IndexOf("nDMaterial PlateRebar 2 500 0", StringComparison.Ordinal);

        Assert.True(uniaxialIndex >= 0 && plateRebarIndex >= 0);
        Assert.True(uniaxialIndex < plateRebarIndex,
            $"uniaxialMaterial (index {uniaxialIndex}) должен быть объявлен раньше nDMaterial PlateRebar (index {plateRebarIndex})");
    }

    [Fact]
    public void Generate_OrdersConcreteWrapperAfterBaseDamageMaterial()
    {
        // PlateFromPlaneStress (tag 4) ссылается на PlasticDamageConcretePlaneStress (tag 8) —
        // зависимость имеет БОЛЬШИЙ tag, чем зависимый от неё материал, аналогично
        // существующему PlateRebar-кейсу, но для второй, независимой пары обёрток.
        var model = Q4Model() with
        {
            Materials =
            [
                new(1, "fixture", new ElasticIsotropicShellMaterialSpec(30e9, 0.25)),
                new(4, "concrete-wrapped", new PlateFromPlaneStressShellMaterialSpec(8, 1.25e10)),
                new(8, "concrete-base", new PlasticDamageConcretePlaneStressShellMaterialSpec(
                    3.0e10, 0.2, 3.0e6, 3.0e7, 0.6, 0.5, 2.0, 0.14)),
            ]
        };

        var script = new ShellTclGenerator().Generate(model);

        int baseIndex = script.IndexOf("nDMaterial PlasticDamageConcretePlaneStress 8", StringComparison.Ordinal);
        int wrapperIndex = script.IndexOf("nDMaterial PlateFromPlaneStress 4 8", StringComparison.Ordinal);

        Assert.True(baseIndex >= 0 && wrapperIndex >= 0);
        Assert.True(baseIndex < wrapperIndex,
            $"Базовый материал (index {baseIndex}) должен быть объявлен раньше обёртки (index {wrapperIndex})");
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
                ShellFrame.Identity, integration, "fixture")],
            Stages = [new() { Tag = "no-load" }]
        };
    }
}
