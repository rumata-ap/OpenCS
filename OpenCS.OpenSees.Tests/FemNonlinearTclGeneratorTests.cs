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
            LoadSteps = 4, Tolerance = 1e-6, MaxIterations = 30, GeomTransfKind = "PDelta"
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
        Assert.Contains("test NormUnbalance", tcl);
        Assert.Contains("algorithm Newton", tcl);
        Assert.Contains("integrator LoadControl", tcl);
    }

    [Fact]
    public void Generate_EmitsRecordersBeforeStepLoopAndOrderFile()
    {
        string tcl = new FemNonlinearTclGenerator().Generate(Console());
        Assert.Contains("recorder Node -file nonlinear_node_disp.out -time -node 1 2 -dof 1 2 3 4 5 6 disp", tcl);
        Assert.Contains("recorder Node -file nonlinear_node_reactions.out -time -node 1 -dof 1 2 3 4 5 6 reaction", tcl);
        Assert.Contains("recorder Element -file nonlinear_element_forces.out -time -ele 1 localForce", tcl);
        Assert.Contains("recorder_order.json", tcl);
        Assert.Contains("\"nodeTags\":[1,2]", tcl);
        Assert.Contains("\"restrainedTags\":[1]", tcl);
        Assert.Contains("\"elemTags\":[1]", tcl);

        int recorderIndex = tcl.IndexOf("recorder Node -file nonlinear_node_disp.out", StringComparison.Ordinal);
        int loopIndex = tcl.IndexOf("for {set i 1}", StringComparison.Ordinal);
        Assert.True(recorderIndex < loopIndex && recorderIndex >= 0 && loopIndex >= 0);
    }

    [Fact]
    public void Generate_EmitsStepLoopWithBreakOnFailure()
    {
        string tcl = new FemNonlinearTclGenerator().Generate(Console());
        Assert.Contains("for {set i 1} {$i <= 4} {incr i}", tcl);
        Assert.Contains("set rc [analyze 1]", tcl);
        Assert.Contains("if {$rc != 0} {break}", tcl);
        Assert.Contains("step_status.out", tcl);
        Assert.Contains("completed.marker", tcl);
    }
}
