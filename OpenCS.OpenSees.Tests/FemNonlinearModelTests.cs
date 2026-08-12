using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public class FemNonlinearModelTests
{
    static FemLinearNode Node(int tag, double x) => new(tag, x, 0, 0, new bool[6]);

    static OpenSeesSectionModel Section() => new()
    {
        Materials = [new OpenSeesMaterialDefinition
        {
            Tag = 1,
            PositiveEnvelope = [new EnvelopePoint(0, 0), new EnvelopePoint(0.01, 2_000_000)],
            NegativeEnvelope = [new EnvelopePoint(-0.01, -2_000_000), new EnvelopePoint(0, 0)]
        }],
        Fibers = [new OpenSeesFiber(0, 0, 0.01, 1)],
        GJ = 1e6
    };

    static FemNonlinearModel ValidModel() => new()
    {
        Nodes = [Node(1, 0), Node(2, 1)],
        Sections = new Dictionary<int, OpenSeesSectionModel> { [1] = Section() },
        Elements = [new FemNonlinearElement(1, 1, 2, SectionTag: 1, NumIntegrationPoints: 5, Vecxz: (0, 0, 1))],
        Stages = [new FemNonlinearStage { Tag = "Стадия 1", Loads = [new FemLinearNodalLoad(2, 1000, 0, 0, 0, 0, 0)] }]
    };

    [Fact]
    public void Validate_ValidModel_DoesNotThrow() => ValidModel().Validate();

    [Fact]
    public void Validate_NoStages_Throws()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements, Stages = []
        };
        var ex = Assert.Throws<InvalidOperationException>(model.Validate);
        Assert.Contains("стади", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ElementReferencesMissingSection_Throws()
    {
        var model = ValidModel();
        model = new FemNonlinearModel
        {
            Nodes = model.Nodes, Sections = model.Sections, Stages = model.Stages,
            Elements = [new FemNonlinearElement(1, 1, 2, SectionTag: 99, NumIntegrationPoints: 5, Vecxz: (0, 0, 1))]
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_ElementReferencesMissingNode_Throws()
    {
        var model = ValidModel();
        model = new FemNonlinearModel
        {
            Nodes = [Node(1, 0)], Sections = model.Sections, Stages = [],
            Elements = [new FemNonlinearElement(1, 1, 99, SectionTag: 1, NumIntegrationPoints: 5, Vecxz: (0, 0, 1))]
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveStageLoadFactorStep_Throws(double step)
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements,
            Stages = [new FemNonlinearStage
            {
                Tag = valid.Stages[0].Tag, Loads = valid.Stages[0].Loads, LoadFactorStep = step
            }]
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_MaxLoadFactorBelowStep_Throws()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements,
            Stages = [new FemNonlinearStage
            {
                Tag = valid.Stages[0].Tag, Loads = valid.Stages[0].Loads,
                LoadFactorStep = 0.2, MaxLoadFactor = 0.1
            }]
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_ZeroRefinementDivisions_Throws()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements, Stages = valid.Stages,
            Policy = new NonlinearAnalysisPolicy { RefinementDivisions = 0 }
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_UnknownGeomTransfKind_Throws()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements, Stages = valid.Stages,
            GeomTransfKind = "Nope"
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_UnknownConvergenceTest_Throws()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements, Stages = valid.Stages,
            Policy = new NonlinearAnalysisPolicy { ConvergenceTest = "Nope" }
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_DefaultConvergenceTest_IsEnergyIncr() => Assert.Equal("EnergyIncr", ValidModel().Policy.ConvergenceTest);

    [Fact]
    public void Validate_UnknownAlgorithm_Throws()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements, Stages = valid.Stages,
            Policy = new NonlinearAnalysisPolicy { Algorithm = "Nope" }
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_DefaultAlgorithm_IsNewtonLineSearch() => Assert.Equal("NewtonLineSearch", ValidModel().Policy.Algorithm);

    [Fact]
    public void Validate_ZeroIntegrationPoints_Throws()
    {
        var model = ValidModel();
        model = new FemNonlinearModel
        {
            Nodes = model.Nodes, Sections = model.Sections, Stages = model.Stages,
            Elements = [new FemNonlinearElement(1, 1, 2, SectionTag: 1, NumIntegrationPoints: 0, Vecxz: (0, 0, 1))]
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_CorotationalWithDistributedLoad_Throws()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements,
            Stages = [new FemNonlinearStage
            {
                Tag = "Стадия 1", Loads = valid.Stages[0].Loads,
                DistributedLoads = [new FemLinearDistributedLoad(1, 0, -1000, 0, 0, -1000, 0, 0, 1)]
            }],
            GeomTransfKind = "Corotational"
        };

        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_RejectsPointLoadOnMissingElement()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements,
            Stages = [new FemNonlinearStage
            {
                Tag = "Стадия 1", Loads = valid.Stages[0].Loads,
                PointLoads = [new FemLinearPointLoad(999, 10, 0, 0, 0.5)]
            }]
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_CorotationalWithPointLoad_Throws()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements,
            Stages = [new FemNonlinearStage
            {
                Tag = "Стадия 1", Loads = valid.Stages[0].Loads,
                PointLoads = [new FemLinearPointLoad(1, 10, 0, 0, 0.5)]
            }],
            GeomTransfKind = "Corotational"
        };

        var ex = Assert.Throws<InvalidOperationException>(model.Validate);
        Assert.Contains("Corotational", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_DuplicateKinematicDofAcrossStages_Throws()
    {
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements,
            Stages =
            [
                new FemNonlinearStage { Tag = "Стадия 1", KinematicLoads = [new FemLinearKinematicLoad(2, 3, 0.01)] },
                new FemNonlinearStage { Tag = "Стадия 2", KinematicLoads = [new FemLinearKinematicLoad(2, 3, 0.02)] }
            ]
        };
        var ex = Assert.Throws<InvalidOperationException>(model.Validate);
        Assert.Contains("Дублирующееся", ex.Message);
    }

    static FemNonlinearModel ModelWithPathControl(FemPathControlSettings pc)
    {
        var valid = ValidModel();
        return new FemNonlinearModel
        {
            Nodes = valid.Nodes, Sections = valid.Sections, Elements = valid.Elements,
            Stages = [new FemNonlinearStage
            {
                Tag = valid.Stages[0].Tag, Loads = valid.Stages[0].Loads, PathControl = pc
            }]
        };
    }

    [Fact]
    public void Validate_DisplacementControlModeWithoutSettings_Throws()
    {
        var model = ModelWithPathControl(new FemPathControlSettings(FemPathControlMode.DisplacementControl));
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_ArcLengthModeWithoutSettings_Throws()
    {
        var model = ModelWithPathControl(new FemPathControlSettings(FemPathControlMode.ArcLength));
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_DisplacementControlValid_DoesNotThrow()
    {
        var dc = new FemDisplacementControlSettings(
            ControlNodeTag: 2, ControlDof: 1,
            InitialIncrement: 0.001, MinIncrement: 0.0001, MaxIncrement: 0.01,
            TargetDisplacement: 0.05, MaxSteps: 200);
        var model = ModelWithPathControl(new FemPathControlSettings(FemPathControlMode.DisplacementControl, DisplacementControl: dc));
        model.Validate();
    }

    [Fact]
    public void Validate_DisplacementControlDofFixed_Throws()
    {
        var fixedNode = new FemLinearNode(1, 0, 0, 0, [true, false, false, false, false, false]);
        var valid = ValidModel();
        var model = new FemNonlinearModel
        {
            Nodes = [fixedNode, valid.Nodes[1]], Sections = valid.Sections, Elements = valid.Elements,
            Stages = [new FemNonlinearStage
            {
                Tag = valid.Stages[0].Tag, Loads = valid.Stages[0].Loads,
                PathControl = new FemPathControlSettings(FemPathControlMode.DisplacementControl,
                    DisplacementControl: new FemDisplacementControlSettings(1, 1, 0.001, 0.0001, 0.01, 0.05, 200))
            }]
        };
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Validate_DisplacementControlNonFiniteTarget_Throws(double target)
    {
        var dc = new FemDisplacementControlSettings(2, 1, 0.001, 0.0001, 0.01, target, 200);
        var model = ModelWithPathControl(new FemPathControlSettings(FemPathControlMode.DisplacementControl, DisplacementControl: dc));
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_DisplacementControlMinAboveMax_Throws()
    {
        var dc = new FemDisplacementControlSettings(2, 1, 0.001, 0.02, 0.01, 0.05, 200); // Min > Max
        var model = ModelWithPathControl(new FemPathControlSettings(FemPathControlMode.DisplacementControl, DisplacementControl: dc));
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_ArcLengthValid_DoesNotThrow()
    {
        var al = new FemArcLengthSettings(S: 0.01, Alpha: 1.0, MinS: 0.001, MaxSteps: 100, MonitorNodeTag: 2, MonitorDof: 1);
        var model = ModelWithPathControl(new FemPathControlSettings(FemPathControlMode.ArcLength, ArcLength: al));
        model.Validate();
    }

    [Fact]
    public void Validate_ContinueWithModeWithoutSettings_Throws()
    {
        var model = ModelWithPathControl(new FemPathControlSettings(
            FemPathControlMode.LoadControl, ContinueWithMode: FemPathControlMode.DisplacementControl));
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_ContinueWithOnNonLoadControlMode_Throws()
    {
        var dc = new FemDisplacementControlSettings(2, 1, 0.001, 0.0001, 0.01, 0.05, 200);
        var model = ModelWithPathControl(new FemPathControlSettings(
            FemPathControlMode.DisplacementControl, DisplacementControl: dc,
            ContinueWithMode: FemPathControlMode.ArcLength,
            ContinueWithArcLength: new FemArcLengthSettings(0.01, 1.0, 0.001, 100, 2, 1)));
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_UndefinedModeEnumValue_Throws()
    {
        var model = ModelWithPathControl(new FemPathControlSettings((FemPathControlMode)99));
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_DisplacementControlModeWithArcLengthSettings_Throws()
    {
        var dc = new FemDisplacementControlSettings(2, 1, 0.001, 0.0001, 0.01, 0.05, 200);
        var al = new FemArcLengthSettings(0.01, 1.0, 0.001, 100, 2, 1);
        var model = ModelWithPathControl(new FemPathControlSettings(
            FemPathControlMode.DisplacementControl, DisplacementControl: dc, ArcLength: al));
        Assert.Throws<InvalidOperationException>(model.Validate);
    }

    [Fact]
    public void Validate_ContinueWithSettingsWithoutContinueWithMode_Throws()
    {
        var cdc = new FemDisplacementControlSettings(2, 1, 0.001, 0.0001, 0.01, 0.05, 200);
        var model = ModelWithPathControl(new FemPathControlSettings(
            FemPathControlMode.LoadControl, ContinueWithDisplacementControl: cdc));
        Assert.Throws<InvalidOperationException>(model.Validate);
    }
}
