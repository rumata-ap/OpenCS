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
            Loads = [new FemLinearNodalLoad(2, 0, 0, -1000, 0, 0, 0)],
            LoadFactorStep = 0.25, MaxLoadFactor = 1.0, RefinementDivisions = 10,
            Tolerance = 1e-6, MaxIterations = 30, GeomTransfKind = "PDelta"
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
        Assert.Contains("algorithm Newton", tcl);
        Assert.Contains("integrator LoadControl", tcl);
    }

    [Fact]
    public void Generate_EmitsTextSnapshotsBeforeStepLoopAndOrderFile()
    {
        string tcl = new FemNonlinearTclGenerator().Generate(Console());
        Assert.DoesNotContain("recorder Node", tcl);
        Assert.DoesNotContain("recorder Element", tcl);
        Assert.Contains("set nonlinearNodeDisp [open nonlinear_node_disp.out w]", tcl);
        Assert.Contains("set nonlinearNodeReactions [open nonlinear_node_reactions.out w]", tcl);
        Assert.Contains("set nonlinearElementForces [open nonlinear_element_forces.out w]", tcl);
        Assert.Contains("puts $nonlinearNodeDisp", tcl);
        Assert.Contains("puts $nonlinearNodeReactions", tcl);
        Assert.Contains("puts $nonlinearElementForces", tcl);
        Assert.Contains("recorder_order.json", tcl);
        Assert.Contains("\"nodeTags\":[1,2]", tcl);
        Assert.Contains("\"restrainedTags\":[1]", tcl);
        Assert.Contains("\"elemTags\":[1]", tcl);

        int recorderIndex = tcl.IndexOf("set nonlinearNodeDisp", StringComparison.Ordinal);
        int loopIndex = tcl.IndexOf("set currentLambda", StringComparison.Ordinal);
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
            Loads = baseModel.Loads, LoadFactorStep = baseModel.LoadFactorStep,
            MaxLoadFactor = baseModel.MaxLoadFactor, RefinementDivisions = baseModel.RefinementDivisions,
            Tolerance = baseModel.Tolerance, MaxIterations = baseModel.MaxIterations,
            GeomTransfKind = baseModel.GeomTransfKind,
            PointLoads = [new FemLinearPointLoad(1, -1500, 250, 0, 0.5)]
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
            Loads = baseModel.Loads, LoadFactorStep = baseModel.LoadFactorStep,
            MaxLoadFactor = baseModel.MaxLoadFactor, RefinementDivisions = baseModel.RefinementDivisions,
            Tolerance = baseModel.Tolerance, MaxIterations = baseModel.MaxIterations,
            GeomTransfKind = "Corotational",
            PointLoads = [new FemLinearPointLoad(1, -1500, 0, 0, 0.4)]
        };

        Assert.Throws<InvalidOperationException>(() => new FemNonlinearTclGenerator().Generate(model));
    }
}
