using OpenCS.OpenSees.Tests.Fixtures;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public sealed class ShellKinematicLoadTests
{
    [Fact]
    public void Validate_RejectsKinematicDofOverFixedNode()
    {
        var model = ShellModelFixtures.Q4Elastic() with
        {
            Stages =
            [
                new ShellNonlinearStage
                {
                    Tag = "parent-kinematic",
                    KinematicLoads = [new ShellKinematicLoad(1, 1, 0.01)]
                }
            ]
        };

        var exception = Assert.Throws<InvalidOperationException>(() => model.Validate());

        Assert.Contains("kinematic", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_EmitsSpForKinematicLoadAndNotFixForIt()
    {
        var model = ShellModelFixtures.Q4Elastic() with
        {
            Stages =
            [
                new ShellNonlinearStage
                {
                    Tag = "parent-kinematic",
                    KinematicLoads = [new ShellKinematicLoad(2, 3, 0.01)]
                }
            ]
        };

        var script = new ShellTclGenerator().Generate(model);

        Assert.Contains("sp 2 3", script);
        Assert.Contains("fix 1 1 1 1 1 1 1", script);
        Assert.DoesNotContain("fix 2 0 0 1", script);
    }

    [Fact]
    public void Validate_RejectsDuplicateKinematicDofInStage()
    {
        var model = ShellModelFixtures.Q4Elastic() with
        {
            Stages =
            [
                new ShellNonlinearStage
                {
                    Tag = "duplicate",
                    KinematicLoads =
                    [
                        new ShellKinematicLoad(2, 3, 0.01),
                        new ShellKinematicLoad(2, 3, 0.02)
                    ]
                }
            ]
        };

        var exception = Assert.Throws<InvalidOperationException>(() => model.Validate());

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
