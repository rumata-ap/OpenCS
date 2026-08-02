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
        Assert.Contains("recorder Node -file shell_node_disp.out", script);
    }

    [Fact]
    public void Generate_T3ReducedEmitsReducedIntegrationAndThreeNodes()
    {
        var script = new ShellTclGenerator().Generate(T3ReducedModel());

        Assert.Contains("element ASDShellT3 11 1 2 3 20 -reducedIntegration", script);
        Assert.DoesNotContain("ASDShellT3 11 1 2 3 4", script);
    }

    [Fact]
    public void Generate_CorotationalEmitsFlagBeforeLocalOnQ4()
    {
        var model = Q4Model() with { ShellCorotational = true };
        var script = new ShellTclGenerator().Generate(model);
        Assert.Contains("element ASDShellQ4 10 1 2 3 4 20 -corotational -local", script);
    }

    [Fact]
    public void Generate_CorotationalEmitsFlagBeforeReducedIntegrationOnT3()
    {
        var model = T3ReducedModel() with { ShellCorotational = true };
        var script = new ShellTclGenerator().Generate(model);
        Assert.Contains("element ASDShellT3 11 1 2 3 20 -corotational -reducedIntegration -local", script);
    }

    [Fact]
    public void Generate_DisabledEnhancedAssumedStrainEmitsNoeasOnQ4OnlyNotT3()
    {
        var model = Q4Model() with
        {
            EnhancedAssumedStrain = false,
            Elements =
            [
                .. Q4Model().Elements,
                new(11, ShellElementKind.ASDShellT3, [1, 2, 3], 20, "section-fingerprint",
                    ShellFrame.Identity, ShellIntegrationPolicy.Full, null)
            ]
        };
        var script = new ShellTclGenerator().Generate(model);
        Assert.Contains("element ASDShellQ4 10 1 2 3 4 20 -noeas -local", script);
        Assert.Contains("element ASDShellT3 11 1 2 3 20 -local", script);
        Assert.DoesNotContain("ASDShellT3 11 1 2 3 20 -noeas", script);
    }

    [Fact]
    public void Generate_DrillingStabilizationEmitsNumericValueOnQ4()
    {
        var model = Q4Model() with
        {
            Drilling = new DrillingPolicy { Mode = ShellDrillingMode.Stabilization, StabilizationValue = 0.001 }
        };
        var script = new ShellTclGenerator().Generate(model);
        Assert.Contains($"element ASDShellQ4 10 1 2 3 4 20 -drillingStab {TclNumber.Format(0.001)} -local", script);
    }

    [Fact]
    public void Generate_NonlinearDrillingEmitsFlagOnBothQ4AndT3()
    {
        var model = Q4Model() with
        {
            Elements =
            [
                .. Q4Model().Elements,
                new(11, ShellElementKind.ASDShellT3, [1, 2, 3], 20, "section-fingerprint",
                    ShellFrame.Identity, ShellIntegrationPolicy.Full, null)
            ],
            Drilling = new DrillingPolicy { Mode = ShellDrillingMode.NonlinearDrilling }
        };
        var script = new ShellTclGenerator().Generate(model);
        Assert.Contains("element ASDShellQ4 10 1 2 3 4 20 -drillingNL -local", script);
        Assert.Contains("element ASDShellT3 11 1 2 3 20 -drillingNL -local", script);
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

    [Fact]
    public void Generate_EmitsNonlinearBeamMaterialSectionAndElement()
    {
        var model = Q4Model() with
        {
            NonlinearBeamSections = new Dictionary<int, OpenSeesSectionModel>
            {
                [30] = new()
                {
                    GJ = 1000,
                    Materials = [new() { Tag = 40, Native = new Steel01Spec(4e8, 2e11, 0.01) }],
                    Fibers = [new(0, 0, 0.001, 40)]
                }
            },
            NonlinearBeamElements = [new(200, 1, 2, 30, 3, (0, 1, 0))]
        };

        var script = new ShellTclGenerator().Generate(model);

        Assert.Contains("uniaxialMaterial Steel01 40", script);
        Assert.Contains("section Fiber 30 -GJ", script);
        Assert.Contains("fiber 0 0 0.001 40", script);
        Assert.Contains("element forceBeamColumn 200 1 2 3 30", script);
    }

    [Fact]
    public void Generate_EmitsStageAwareBeamFiberStateQueriesAndCatalogLocations()
    {
        var baseModel = Q4Model();
        var model = baseModel with
        {
            NonlinearBeamSections = new Dictionary<int, OpenSeesSectionModel>
            {
                [30] = new()
                {
                    GJ = 1000,
                    Materials = [new() { Tag = 40, Native = new Steel01Spec(4e8, 2e11, 0.01) }],
                    Fibers = [new(-0.1, -0.2, 0.001, 40), new(0.1, 0.2, 0.001, 40)]
                }
            },
            NonlinearBeamElements = [new(200, 1, 2, 30, 3, (0, 1, 0))],
            Stages = [new() { Tag = "stage-1", Loads = [new(3, 0, 0, -1000, 0, 0, 0)] }]
        };

        var script = new ShellTclGenerator().Generate(model);

        Assert.Contains("shell_beam_fiber_states.out", script);
        Assert.Contains("section 1 fiber", script);
        Assert.Contains("stressStrain", script);
        Assert.Contains("shell_beam_fiber_states.out [list $stepIndex $currentStageIndex $currentLambda 200", script);
        Assert.Contains("\"beamFiberLocations\":[", script);
        Assert.Contains("\"elementTag\":200", script);
    }

    [Fact]
    public void Generate_EmitsPerIntegrationPointSectionForceRecordersGroupedByPointCount()
    {
        var model = Q4Model() with
        {
            Elements =
            [
                .. Q4Model().Elements,
                new(11, ShellElementKind.ASDShellT3, [1, 2, 3], 20, "section-fingerprint",
                    ShellFrame.Identity, ShellIntegrationPolicy.Full, null)
            ],
            Stages = [new() { Tag = "stage-1", Loads = [new(2, 0, 0, -1000, 0, 0, 0)] }]
        };

        var script = new ShellTclGenerator().Generate(model);

        // Q4 (элемент 10) имеет 4 точки, T3 full (элемент 11) — 3: точки 1..3 общие для
        // обоих элементов (один recorder на точку, без коллизии имён файлов), точка 4 —
        // только у Q4.
        Assert.Contains("recorder Element -file shell_section_forces_ip1.out -closeOnWrite -time -ele 10 11 material 1 force", script);
        Assert.Contains("recorder Element -file shell_section_forces_ip3.out -closeOnWrite -time -ele 10 11 material 3 force", script);
        Assert.Contains("recorder Element -file shell_section_forces_ip4.out -closeOnWrite -time -ele 10 material 4 force", script);
        Assert.Contains("\"sectionForceGroups\"", script);
    }

    [Fact]
    public void Generate_EmitsShellLayerStressAndStrainRecordersWithoutQ4T3Collisions()
    {
        var q4 = Model(
            10,
            ShellElementKind.ASDShellQ4,
            ShellIntegrationPolicy.Full,
            [1, 2, 3, 4],
            layerCount: 4);
        var model = q4 with
        {
            Elements =
            [
                .. q4.Elements,
                new(11, ShellElementKind.ASDShellT3, [1, 2, 3], 20, "section-fingerprint",
                    ShellFrame.Identity, ShellIntegrationPolicy.Full, null)
            ]
        };

        var script = new ShellTclGenerator().Generate(model);

        Assert.Contains(
            "recorder Element -file shell_layer_s20_ip1_layer1_stress.out -closeOnWrite -time -ele 10 11 material 1 fiber 1 stress",
            script);
        Assert.Contains(
            "recorder Element -file shell_layer_s20_ip1_layer1_strain.out -closeOnWrite -time -ele 10 11 material 1 fiber 1 strain",
            script);
        Assert.Contains(
            "recorder Element -file shell_layer_s20_ip4_layer4_stress.out -closeOnWrite -time -ele 10 material 4 fiber 4 stress",
            script);
        Assert.DoesNotContain("shell_layer_s20_ip4_layer4_stress.out -closeOnWrite -time -ele 10 11", script);
        Assert.Contains("\"version\":2", script);
        Assert.Contains("state_order.json", script);
    }

    [Fact]
    public void Generate_TwoSectionsWithSameLayerCount_EmitsSeparateRecorderGroups()
    {
        var baseModel = Q4Model();
        var model = baseModel with
        {
            Sections =
            [
                baseModel.Sections[0],
                new(21, "plate-b", 0.2, ShellFrame.Identity,
                    baseModel.Sections[0].Layers.Select((layer, i) => layer with
                    {
                        MaterialTag = 1,
                        SourceId = $"concrete-b:{i}"
                    }).ToArray(),
                    ShellMappingMode.Exact, [], "section-fingerprint-b")
            ],
            Materials = baseModel.Materials,
            Elements =
            [
                baseModel.Elements[0],
                new(11, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], 21, "section-fingerprint-b",
                    ShellFrame.Identity, ShellIntegrationPolicy.Full, null)
            ]
        };

        var script = new ShellTclGenerator().Generate(model);

        Assert.Contains("recorder Element -file shell_layer_s20_ip1_layer1_stress.out -closeOnWrite -time -ele 10 material 1 fiber 1 stress", script);
        Assert.Contains("recorder Element -file shell_layer_s21_ip1_layer1_stress.out -closeOnWrite -time -ele 11 material 1 fiber 1 stress", script);
        Assert.DoesNotContain("shell_layer_s20_ip1_layer1_stress.out -closeOnWrite -time -ele 10 11", script);
    }

    [Fact]
    public void Generate_StateOrderV2CarriesProvenanceMetadata()
    {
        var script = new ShellTclGenerator().Generate(Q4Model());

        Assert.Contains("\"version\":2", script);
        Assert.Contains("\"sectionTag\":20", script);
        Assert.Contains("\"layerKind\":\"Concrete\"", script);
        Assert.Contains("\"sourceId\":\"concrete:", script);
        Assert.Contains("\"sectionFingerprint\":\"section-fingerprint\"", script);
        Assert.Contains("\"centerZ\":", script);
        Assert.Contains("\"thickness\":", script);
        Assert.Contains("\"unit\":\"Pa\"", script);
    }

    [Fact]
    public void Generate_RequestedUnsupportedOptionalResponse_ThrowsUnsupportedShellResponse()
    {
        var model = Q4Model() with
        {
            MaterialStateRecording = new ShellStateRecordingPolicy
            {
                OptionalResponses = ["tangent"]
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new ShellTclGenerator().Generate(model));

        Assert.Contains("unsupported_shell_response", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_RejectsShellIpOutsideModelRange()
    {
        var model = Q4Model() with
        {
            MaterialStateRecording = new ShellStateRecordingPolicy { ShellIntegrationPoints = [9] }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new ShellTclGenerator().Generate(model));

        Assert.Contains("recording_selection_invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_RejectsBeamFiberSelectionOutsideSectionFibers()
    {
        var model = Q4Model() with
        {
            NonlinearBeamSections = new Dictionary<int, OpenSeesSectionModel>
            {
                [30] = new() { GJ = 1000, Materials = [new() { Tag = 40, Native = new Steel01Spec(4e8, 2e11, 0.01) }],
                    Fibers = [new(0, 0, 0.001, 40)] }
            },
            NonlinearBeamElements = [new(200, 1, 2, 30, 3, (0, 1, 0))],
            MaterialStateRecording = new ShellStateRecordingPolicy { BeamFiberIndices = [1] }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new ShellTclGenerator().Generate(model));

        Assert.Contains("recording_selection_invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_EmitsAdaptiveNewtonLoopAndRecorders()
    {
        var model = Q4Model() with
        {
            Stages = [new() { Tag = "stage-1", LoadFactorStep = 0.2, MaxLoadFactor = 1.0,
                Loads = [new(2, 0, 0, -1000, 0, 0, 0)] }]
        };

        var script = new ShellTclGenerator().Generate(model);

        Assert.Contains("test NormDispIncr", script);
        Assert.Contains("algorithm Newton", script);
        Assert.Contains("proc advanceTo", script);
        Assert.Contains("recorder Node -file shell_node_disp.out -closeOnWrite -time", script);
        Assert.Contains("recorder Element -file shell_element_forces.out -closeOnWrite -time", script);
        Assert.Contains("recorder_order.json", script);
        Assert.Contains("step_status.out", script);
        Assert.DoesNotContain("algorithm Linear", script);
        Assert.DoesNotContain("set ok [analyze 1]", script);
    }

    [Fact]
    public void Generate_MultipleStages_GuardsSubsequentStageOnPriorFailure()
    {
        var model = Q4Model() with
        {
            Stages =
            [
                new() { Tag = "dead-load", Loads = [new(2, 0, 0, -500, 0, 0, 0)] },
                new() { Tag = "live-load", Loads = [new(3, 0, 0, -500, 0, 0, 0)] }
            ]
        };

        var script = new ShellTclGenerator().Generate(model);

        Assert.Contains("if {!$analysisFailed}", script);
        Assert.Contains("loadConst -time 0.0", script);
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
