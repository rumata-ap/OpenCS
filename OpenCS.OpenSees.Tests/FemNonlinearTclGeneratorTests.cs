using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public class FemNonlinearTclGeneratorTests
{
    static FemNonlinearModel Console()
    {
        var n1 = new FemLinearNode(1, 0, 0, 0, [true, true, true, true, true, true]);
        var n2 = new FemLinearNode(2, 3, 0, 0, new bool[6]);
        var section = new OpenSeesSectionModel
        {
            Materials = [new OpenSeesMaterialDefinition
            {
                Tag = 1,
                PositiveEnvelope = [new EnvelopePoint(0, 0), new EnvelopePoint(0.01, 2_000_000)],
                NegativeEnvelope = [new EnvelopePoint(-0.01, -2_000_000), new EnvelopePoint(0, 0)]
            }],
            Fibers = [new OpenSeesFiber(0.3, 0.2, 0.01, 1), new OpenSeesFiber(-0.3, -0.2, 0.01, 1)],
            GJ = 1e6
        };
        return new FemNonlinearModel
        {
            Nodes = [n1, n2],
            Sections = new Dictionary<int, OpenSeesSectionModel> { [1] = section },
            Elements = [new FemNonlinearElement(1, 1, 2, SectionTag: 1, NumIntegrationPoints: 5, Vecxz: (0, 0, 1))],
            Stages = [new FemNonlinearStage
            {
                Tag = "Стадия 1", Loads = [new FemLinearNodalLoad(2, 0, 0, -1000, 0, 0, 0)],
                LoadFactorStep = 0.25, MaxLoadFactor = 1.0
            }],
            GeomTransfKind = "PDelta",
            Policy = new NonlinearAnalysisPolicy { RefinementDivisions = 10, Tolerance = 1e-6, MaxIterations = 30 }
        };
    }

    [Fact]
    public void Generate_EmitsFiberSectionAndForceBeamColumn()
    {
        string tcl = new FemNonlinearTclGenerator().Generate(Console());
        Assert.Contains("model basic -ndm 3 -ndf 6", tcl);
        Assert.Contains("uniaxialMaterial ElasticMultiLinear 1", tcl);
        Assert.Contains("section Fiber 1 -GJ", tcl);
        Assert.Contains("fiber", tcl);
        Assert.Contains("geomTransf PDelta", tcl);
        Assert.Contains("element forceBeamColumn 1 1 2 5 1", tcl);
        Assert.Contains("test EnergyIncr", tcl);
        Assert.Contains("algorithm NewtonLineSearch", tcl);
        Assert.Contains("integrator LoadControl", tcl);
    }

    [Fact]
    public void Generate_EmitsAllSupportedNativeMaterialCommands()
    {
        var model = Console();
        model = new FemNonlinearModel
        {
            Nodes = model.Nodes,
            Sections = new Dictionary<int, OpenSeesSectionModel>
            {
                [1] = new OpenSeesSectionModel
                {
                    Materials =
                    [
                        new OpenSeesMaterialDefinition
                        {
                            Tag = 1,
                            Native = new Concrete01Spec(-20e6, -0.002, -4e6, -0.0035)
                        },
                        new OpenSeesMaterialDefinition
                        {
                            Tag = 2,
                            Native = new Concrete02Spec(-20e6, -0.002, -4e6, -0.0035, 0.1, 1e6, 5e8)
                        },
                        new OpenSeesMaterialDefinition
                        {
                            Tag = 3,
                            Native = new Concrete04Spec(-20e6, -0.002, -0.0035, 30e9, 1e6, 0.00015, 0.1)
                        },
                        new OpenSeesMaterialDefinition
                        {
                            Tag = 4,
                            Native = new Steel01Spec(500e6, 200e9, 0.01)
                        },
                        new OpenSeesMaterialDefinition
                        {
                            Tag = 5,
                            Native = new Steel02Spec(500e6, 200e9, 0.01, 18, 0.925, 0.15)
                        }
                    ],
                    Fibers =
                    [
                        new OpenSeesFiber(0, 0, 0.01, 1),
                        new OpenSeesFiber(0, 0, 0.01, 2),
                        new OpenSeesFiber(0, 0, 0.01, 3),
                        new OpenSeesFiber(0, 0, 0.01, 4),
                        new OpenSeesFiber(0, 0, 0.01, 5)
                    ],
                    GJ = 1e6
                }
            },
            Elements = model.Elements,
            Stages = model.Stages,
            GeomTransfKind = model.GeomTransfKind,
            Policy = model.Policy
        };

        string tcl = new FemNonlinearTclGenerator().Generate(model);

        Assert.Contains("uniaxialMaterial Concrete01 1", tcl);
        Assert.Contains("uniaxialMaterial Concrete02 2", tcl);
        Assert.Contains("uniaxialMaterial Concrete04 3", tcl);
        Assert.Contains("uniaxialMaterial Steel01 4", tcl);
        Assert.Contains("uniaxialMaterial Steel02 5", tcl);
    }

    [Fact]
    public void Generate_LogsIterationCountAndFinalNormOnEachSuccessfulSubstep()
    {
        string tcl = new FemNonlinearTclGenerator().Generate(Console());
        Assert.Contains("fconfigure stdout -buffering line", tcl);
        Assert.Contains("set iters [testIter]", tcl);
        Assert.Contains("set finalNorm [lindex [testNorm] [expr {$iters - 1}]]", tcl);
        Assert.Contains("puts \"step $stepIndex OK stage=$currentStageIndex lambda=$currentLambda depth=$depth iters=$iters norm=$finalNorm\"", tcl);
        Assert.Contains("puts \"step [expr {$stepIndex + 1}] FAILED stage=$currentStageIndex lambda=$currentLambda\"", tcl);
    }

    [Fact]
    public void Generate_EmitsPlainNewtonAlgorithmWhenSelected()
    {
        var model = Console();
        model = new FemNonlinearModel
        {
            Nodes = model.Nodes, Sections = model.Sections, Elements = model.Elements, Stages = model.Stages,
            GeomTransfKind = model.GeomTransfKind,
            Policy = model.Policy with { Algorithm = "Newton" }
        };
        string tcl = new FemNonlinearTclGenerator().Generate(model);
        Assert.Contains("algorithm Newton", tcl);
        Assert.DoesNotContain("algorithm NewtonLineSearch", tcl);
    }

    [Fact]
    public void Generate_EmitsNativeRecordersWithCloseOnWriteBeforeStepLoopAndOrderFile()
    {
        // Штатные recorder Node/Element с -closeOnWrite заменили ручные Tcl-каналы
        // (open/puts/close) для nonlinear_node_disp/reactions/element_forces — не держат файл
        // открытым весь расчёт, что устранило класс порчи вывода на реальных сценариях (см.
        // комментарий в FemNonlinearTclGenerator.Generate над recorder Node).
        string tcl = new FemNonlinearTclGenerator().Generate(Console());
        Assert.Contains("recorder Node -file nonlinear_node_disp.out -closeOnWrite -time -node 1 2 -dof 1 2 3 4 5 6 disp", tcl);
        Assert.Contains("recorder Node -file nonlinear_node_reactions.out -closeOnWrite -time -node 1 -dof 1 2 3 4 5 6 reaction", tcl);
        Assert.Contains("recorder Element -file nonlinear_element_forces.out -closeOnWrite -time -ele 1 localForce", tcl);
        Assert.Contains("recorder_order.json", tcl);
        Assert.Contains("\"nodeTags\":[1,2]", tcl);
        Assert.Contains("\"restrainedTags\":[1]", tcl);
        Assert.Contains("\"elemTags\":[1]", tcl);

        int recorderIndex = tcl.IndexOf("recorder Node -file nonlinear_node_disp.out", StringComparison.Ordinal);
        int loopIndex = tcl.IndexOf("set currentStageIndex", StringComparison.Ordinal);
        Assert.True(recorderIndex < loopIndex && recorderIndex >= 0 && loopIndex >= 0);
    }

    [Fact]
    public void Generate_SetsIntegratorBeforeStaticAnalysis()
    {
        string tcl = new FemNonlinearTclGenerator().Generate(Console());
        int integratorIndex = tcl.IndexOf("integrator LoadControl 1.0", StringComparison.Ordinal);
        int analysisIndex = tcl.IndexOf("analysis Static", StringComparison.Ordinal);
        Assert.True(integratorIndex >= 0 && analysisIndex > integratorIndex);
    }

    [Fact]
    public void Generate_EmitsStepLoopWithBreakOnFailure()
    {
        string tcl = new FemNonlinearTclGenerator().Generate(Console());
        Assert.Contains("while {$currentLambda <", tcl);
        Assert.Contains("set rc [analyze 1]", tcl);
        Assert.Contains("refinementDivisions", tcl);
        Assert.Contains("step_status.out", tcl);
        Assert.Contains("completed.marker", tcl);
    }

    [Fact]
    public void Generate_EmitsAdaptiveLoadAndFiberStateArtifacts()
    {
        string tcl = new FemNonlinearTclGenerator().Generate(Console());
        Assert.Contains("set loadFactorStep 0.25", tcl);
        Assert.Contains("set maxLoadFactor 1", tcl);
        Assert.Contains("set refinementDivisions 10", tcl);
        Assert.Contains("nonlinear_fiber_states.out", tcl);
        Assert.Contains("nonlinear_section_order.json", tcl);
        Assert.Contains("integrationPoints", tcl);
        Assert.Contains("isRefinement", tcl);
    }

    [Fact]
    public void Generate_EmitsBeamPointEleLoad()
    {
        var baseModel = Console();
        var model = new FemNonlinearModel
        {
            Nodes = baseModel.Nodes, Sections = baseModel.Sections, Elements = baseModel.Elements,
            GeomTransfKind = baseModel.GeomTransfKind,
            Policy = baseModel.Policy,
            Stages = [new FemNonlinearStage
            {
                Tag = "Стадия 1", Loads = baseModel.Stages[0].Loads,
                PointLoads = [new FemLinearPointLoad(1, -1500, 250, 0, 0.5)]
            }]
        };

        string tcl = new FemNonlinearTclGenerator().Generate(model);

        Assert.Contains("eleLoad -ele 1 -type -beamPoint -1500 250 0.5 0", tcl);
    }

    [Fact]
    public void Generate_ThrowsForCorotationalWithPointLoads()
    {
        var baseModel = Console();
        var model = new FemNonlinearModel
        {
            Nodes = baseModel.Nodes, Sections = baseModel.Sections, Elements = baseModel.Elements,
            Policy = baseModel.Policy,
            GeomTransfKind = "Corotational",
            Stages = [new FemNonlinearStage
            {
                Tag = "Стадия 1", Loads = baseModel.Stages[0].Loads,
                PointLoads = [new FemLinearPointLoad(1, -1500, 0, 0, 0.4)]
            }]
        };

        Assert.Throws<InvalidOperationException>(() => new FemNonlinearTclGenerator().Generate(model));
    }

    static FemNonlinearModel WithMaterial(OpenSeesMaterialDefinition material)
    {
        var n1 = new FemLinearNode(1, 0, 0, 0, [true, true, true, true, true, true]);
        var n2 = new FemLinearNode(2, 3, 0, 0, new bool[6]);
        var section = new OpenSeesSectionModel
        {
            Materials = [material],
            Fibers = [new OpenSeesFiber(0.3, 0.2, 0.01, material.Tag)],
            GJ = 1e6
        };
        return new FemNonlinearModel
        {
            Nodes = [n1, n2],
            Sections = new Dictionary<int, OpenSeesSectionModel> { [1] = section },
            Elements = [new FemNonlinearElement(1, 1, 2, SectionTag: 1, NumIntegrationPoints: 5, Vecxz: (0, 0, 1))],
            Stages = [new FemNonlinearStage
            {
                Tag = "Стадия 1", Loads = [new FemLinearNodalLoad(2, 0, 0, -1000, 0, 0, 0)],
                LoadFactorStep = 0.25, MaxLoadFactor = 1.0
            }],
            GeomTransfKind = "Linear",
            Policy = new NonlinearAnalysisPolicy { RefinementDivisions = 10, Tolerance = 1e-6, MaxIterations = 30 }
        };
    }

    [Fact]
    public void Generate_EmitsConcrete04ForNativeSpecWithoutTension()
    {
        var material = new OpenSeesMaterialDefinition
        {
            Tag = 1,
            Native = new Concrete04Spec(Fc: -14_500_000, Ec0: -0.002, Ecu: -0.0035, Ec: 30_000_000_000, Fct: null, Et: null, Beta: null)
        };

        string tcl = new FemNonlinearTclGenerator().Generate(WithMaterial(material));

        Assert.Contains(
            $"uniaxialMaterial Concrete04 1 {TclNumber.Format(-14_500_000)} {TclNumber.Format(-0.002)} {TclNumber.Format(-0.0035)} {TclNumber.Format(30_000_000_000)}",
            tcl);
        Assert.DoesNotContain("ElasticMultiLinear", tcl);
    }

    [Fact]
    public void Generate_EmitsConcrete04ForNativeSpecWithTension()
    {
        var material = new OpenSeesMaterialDefinition
        {
            Tag = 1,
            Native = new Concrete04Spec(
                Fc: -14_500_000, Ec0: -0.002, Ecu: -0.0035, Ec: 30_000_000_000,
                Fct: 1_050_000, Et: 0.00015, Beta: 0.1)
        };

        string tcl = new FemNonlinearTclGenerator().Generate(WithMaterial(material));

        Assert.Contains(
            $"uniaxialMaterial Concrete04 1 {TclNumber.Format(-14_500_000)} {TclNumber.Format(-0.002)} "
            + $"{TclNumber.Format(-0.0035)} {TclNumber.Format(30_000_000_000)} "
            + $"{TclNumber.Format(1_050_000)} {TclNumber.Format(0.00015)} {TclNumber.Format(0.1)}",
            tcl);
    }

    [Fact]
    public void Generate_EmitsSteel02ForNativeSpec()
    {
        var material = new OpenSeesMaterialDefinition
        {
            Tag = 1,
            Native = new Steel02Spec(Fy: 435_000_000, E0: 200_000_000_000, B: 0.01, R0: 18, CR1: 0.925, CR2: 0.15)
        };

        string tcl = new FemNonlinearTclGenerator().Generate(WithMaterial(material));

        Assert.Contains(
            $"uniaxialMaterial Steel02 1 {TclNumber.Format(435_000_000)} {TclNumber.Format(200_000_000_000)} "
            + $"{TclNumber.Format(0.01)} {TclNumber.Format(18)} {TclNumber.Format(0.925)} {TclNumber.Format(0.15)}",
            tcl);
    }

    [Fact]
    public void Generate_EmitsSteel01ForNativeSpec()
    {
        var material = new OpenSeesMaterialDefinition
        {
            Tag = 1,
            Native = new Steel01Spec(Fy: 435_000_000, E0: 200_000_000_000, B: 0.01)
        };

        string tcl = new FemNonlinearTclGenerator().Generate(WithMaterial(material));

        Assert.Contains(
            $"uniaxialMaterial Steel01 1 {TclNumber.Format(435_000_000)} {TclNumber.Format(200_000_000_000)} {TclNumber.Format(0.01)}",
            tcl);
    }

    [Fact]
    public void Generate_StillEmitsElasticMultiLinearWhenNativeIsNull()
    {
        string tcl = new FemNonlinearTclGenerator().Generate(Console());
        Assert.Contains("uniaxialMaterial ElasticMultiLinear 1", tcl);
    }

    [Fact]
    public void Generate_EmitsKinematicConstraintAlongsideForceLoad()
    {
        var baseModel = Console();
        var model = new FemNonlinearModel
        {
            Nodes = baseModel.Nodes, Sections = baseModel.Sections, Elements = baseModel.Elements,
            GeomTransfKind = baseModel.GeomTransfKind,
            Policy = baseModel.Policy,
            Stages = [new FemNonlinearStage
            {
                Tag = "Стадия 1", Loads = baseModel.Stages[0].Loads,
                KinematicLoads = [new FemLinearKinematicLoad(2, 1, 0.015)]
            }]
        };

        string tcl = new FemNonlinearTclGenerator().Generate(model);

        Assert.Contains("load 2 0 0 -1000 0 0 0", tcl);
        Assert.Contains($"sp 2 1 {TclNumber.Format(0.015)}", tcl);
    }

    [Fact]
    public void Generate_TwoStages_EmitsTwoPatternsWithLoadConstBetweenAndDistinctStageIndex()
    {
        var baseModel = Console();
        var model = new FemNonlinearModel
        {
            Nodes = baseModel.Nodes, Sections = baseModel.Sections, Elements = baseModel.Elements,
            GeomTransfKind = baseModel.GeomTransfKind,
            Policy = baseModel.Policy,
            Stages =
            [
                new FemNonlinearStage
                {
                    Tag = "Сжатие", Loads = [new FemLinearNodalLoad(2, -50000, 0, 0, 0, 0, 0)],
                    LoadFactorStep = 0.2, MaxLoadFactor = 2.0
                },
                new FemNonlinearStage
                {
                    Tag = "Изгиб", Loads = [new FemLinearNodalLoad(2, 0, 0, -1000, 0, 0, 0)],
                    LoadFactorStep = 0.05, MaxLoadFactor = 5.0
                }
            ]
        };

        string tcl = new FemNonlinearTclGenerator().Generate(model);

        Assert.Contains("pattern Plain 1 Linear {", tcl);
        Assert.Contains("pattern Plain 2 Linear {", tcl);
        Assert.Contains("loadConst -time 0.0", tcl);
        Assert.Contains("set currentStageIndex 0", tcl);
        Assert.Contains("set currentStageIndex 1", tcl);
        Assert.Contains("# step stageIndex loadFactor converged isRefinement", tcl);

        int pattern1 = tcl.IndexOf("pattern Plain 1 Linear {", StringComparison.Ordinal);
        int loadConst = tcl.IndexOf("loadConst -time 0.0", StringComparison.Ordinal);
        int pattern2 = tcl.IndexOf("pattern Plain 2 Linear {", StringComparison.Ordinal);
        Assert.True(pattern1 >= 0 && pattern1 < loadConst && loadConst < pattern2);

        // Load 2-й стадии обёрнут в защиту "продолжать, только если первая стадия сошлась".
        int guard = tcl.IndexOf("if {!$analysisFailed} {", StringComparison.Ordinal);
        Assert.True(guard >= 0 && guard < loadConst);

        // Каждая стадия задаёт СВОИ Шаг/Предел λ, а не общие на весь расчёт.
        int stage1Index = tcl.IndexOf("set currentStageIndex 0", StringComparison.Ordinal);
        int stage2Index = tcl.IndexOf("set currentStageIndex 1", StringComparison.Ordinal);
        int step1 = tcl.IndexOf("set loadFactorStep 0.2", StringComparison.Ordinal);
        int max1 = tcl.IndexOf("set maxLoadFactor 2", StringComparison.Ordinal);
        int step2 = tcl.IndexOf("set loadFactorStep 0.05", StringComparison.Ordinal);
        int max2 = tcl.IndexOf("set maxLoadFactor 5", StringComparison.Ordinal);
        Assert.True(step1 >= 0 && max1 >= 0 && step2 >= 0 && max2 >= 0);
        Assert.True(stage1Index < step1 && step1 < pattern2);
        Assert.True(stage2Index < step2);
    }

    [Fact]
    public void Generate_AlwaysEmitsAdvanceDisplacementProc()
    {
        string tcl = new FemNonlinearTclGenerator().Generate(Console());
        Assert.Contains("proc advanceDisplacement {nodeTag dof targetDisp initIncr minIncr maxIncr maxSteps}", tcl);
        Assert.Contains("integrator DisplacementControl $nodeTag $dof $incr 4 $dUmin $dUmax", tcl);
        Assert.Contains("set lastPathControlReason", tcl);
    }

    [Fact]
    public void Generate_AdvanceDisplacement_ChecksTargetReachedBeforeMaxSteps()
    {
        // Регрессия на исправленный приоритет target_reached над max_steps_reached: причина
        // должна проверяться ПОСЛЕ выхода из цикла независимо от того, что его завершило.
        string tcl = new FemNonlinearTclGenerator().Generate(Console());
        int procIdx = tcl.IndexOf("proc advanceDisplacement", StringComparison.Ordinal);
        Assert.True(procIdx >= 0);
        string proc = tcl[procIdx..];
        int targetReachedIdx = proc.IndexOf("set targetReached", StringComparison.Ordinal);
        int maxStepsReachedIdx = proc.IndexOf("\"max_steps_reached\"", StringComparison.Ordinal);
        Assert.True(targetReachedIdx >= 0 && targetReachedIdx < maxStepsReachedIdx,
            "targetReached должен вычисляться до присвоения причины max_steps_reached");
    }

    [Fact]
    public void Generate_AdvanceDisplacement_ZeroStepBranchSetsCorrectReason()
    {
        string tcl = new FemNonlinearTclGenerator().Generate(Console());
        Assert.Contains("zero_step_target_already_reached", tcl);
    }

    [Fact]
    public void Generate_AdvanceDisplacement_SignedDUminDUmaxOrdering()
    {
        string tcl = new FemNonlinearTclGenerator().Generate(Console());
        Assert.Contains("set dUmin [expr {$dir > 0 ? $minIncr : -$maxIncr}]", tcl);
        Assert.Contains("set dUmax [expr {$dir > 0 ? $maxIncr : -$minIncr}]", tcl);
    }
}
