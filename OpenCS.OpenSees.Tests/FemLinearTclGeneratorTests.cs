using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Tests;

public class FemLinearTclGeneratorTests
{
    static FemLinearModel Console()
    {
        var n1 = new FemLinearNode(1, 0, 0, 0, [true, true, true, true, true, true]);
        var n2 = new FemLinearNode(2, 3, 0, 0, new bool[6]);
        return new FemLinearModel
        {
            Nodes = [n1, n2],
            Elements = [new FemLinearElement(1, 1, 2, 0.02, 3e10, 1e6, 1.0, 5e-4, 2e-4, (0, 0, 1))],
            Loads = [new FemLinearNodalLoad(2, 0, 0, -1000, 0, 0, 0)]
        };
    }

    [Fact]
    public void Generate_EmitsCoreModelCommands()
    {
        string tcl = new FemLinearTclGenerator().Generate(Console());
        Assert.Contains("model basic -ndm 3 -ndf 6", tcl);
        Assert.Contains("node 1 0 0 0", tcl);
        Assert.Contains("node 2 3 0 0", tcl);
        Assert.Contains("fix 1 1 1 1 1 1 1", tcl);
        Assert.Contains("geomTransf Linear", tcl);
        Assert.Contains("element elasticBeamColumn 1 1 2", tcl);
        Assert.Contains("load 2 0 0 -1000 0 0 0", tcl);
        Assert.Contains("analyze 1", tcl);
        Assert.Contains("node_disp.out", tcl);
        Assert.Contains("element_forces.out", tcl);
        Assert.Contains("completed.marker", tcl);
    }

    [Fact]
    public void Generate_FreeNodeGetsAllZeroFix()
    {
        string tcl = new FemLinearTclGenerator().Generate(Console());
        Assert.Contains("fix 2 0 0 0 0 0 0", tcl);
    }
}
