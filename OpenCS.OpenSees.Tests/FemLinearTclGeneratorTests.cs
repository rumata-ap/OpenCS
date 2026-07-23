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

    [Fact]
    public void Generate_EmitsFullUniformAndPartialTrapezoidEleLoads()
    {
        var baseModel = Console();
        var model = new FemLinearModel
        {
            Nodes = baseModel.Nodes,
            Elements = baseModel.Elements,
            Loads = baseModel.Loads,
            DistributedLoads =
            [
                new FemLinearDistributedLoad(1, 0, -2000, 0, 0, -2000, 0, 0, 1),
                new FemLinearDistributedLoad(1, -1000, 0, 0, -3000, 0, 0, 0.25, 0.75)
            ]
        };

        string tcl = new FemLinearTclGenerator().Generate(model);

        Assert.Contains("eleLoad -ele 1 -type -beamUniform 0 -2000 0", tcl);
        Assert.Contains("eleLoad -ele 1 -type -beamUniform -1000 0 0 0.25 0.75 -3000 0 0", tcl);
    }

    [Fact]
    public void Generate_EmitsBeamPointEleLoad()
    {
        var baseModel = Console();
        var model = new FemLinearModel
        {
            Nodes = baseModel.Nodes,
            Elements = baseModel.Elements,
            Loads = baseModel.Loads,
            PointLoads = [new FemLinearPointLoad(1, -1500, 250, 0, 0.5)]
        };

        string tcl = new FemLinearTclGenerator().Generate(model);

        Assert.Contains("eleLoad -ele 1 -type -beamPoint -1500 250 0.5 0", tcl);
    }

    [Fact]
    public void Generate_EmitsKinematicConstraintAlongsideForceLoad()
    {
        var baseModel = Console();
        var model = new FemLinearModel
        {
            Nodes = baseModel.Nodes,
            Elements = baseModel.Elements,
            Loads = baseModel.Loads,
            KinematicLoads = [new FemLinearKinematicLoad(2, 1, 0.015)]
        };

        string tcl = new FemLinearTclGenerator().Generate(model);

        Assert.Single(model.KinematicLoads);
        Assert.Contains("load 2 0 0 -1000 0 0 0", tcl);
        Assert.Contains($"sp 2 1 {TclNumber.Format(0.015)}", tcl);
    }
}
