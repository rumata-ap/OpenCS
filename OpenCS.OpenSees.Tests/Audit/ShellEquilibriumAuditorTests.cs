using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests.Audit;

public sealed class ShellEquilibriumAuditorTests
{
    [Fact]
    public void AppliedResultantAtStep_SingleStage_ScalesByCurrentLambda()
    {
        var stages = new[]
        {
            new ShellNonlinearStage
            {
                Tag = "s0",
                MaxLoadFactor = 1.0,
                Loads = [new ShellNodalLoad(2, 0, 0, -1000, 0, 0, 0)]
            }
        };

        var nodes = new Dictionary<int, NormalizedShellNode>
        {
            [2] = new(2, 1, 0, 0, new bool[6], null)
        };
        ShellResultant applied = ShellEquilibriumAuditor.AppliedResultantAtStep(stages, 0, 0.5, nodes);

        Assert.Equal(0, applied.Fx, 12);
        Assert.Equal(-500, applied.Fz, 12);
        Assert.Equal(500, applied.My, 12);
    }

    [Fact]
    public void AppliedResultantAtStep_MultiStage_UsesPreviousStageMaxPlusCurrentLambda()
    {
        var stages = new[]
        {
            new ShellNonlinearStage { Tag = "dead", MaxLoadFactor = 2.0,
                Loads = [new ShellNodalLoad(2, 0, 0, -500, 0, 0, 0)] },
            new ShellNonlinearStage { Tag = "live", MaxLoadFactor = 1.0,
                Loads = [new ShellNodalLoad(3, 0, 0, -300, 0, 0, 0)] }
        };

        var nodes = new Dictionary<int, NormalizedShellNode>
        {
            [2] = new(2, 0, 0, 0, new bool[6], null),
            [3] = new(3, 0, 0, 0, new bool[6], null)
        };
        ShellResultant applied = ShellEquilibriumAuditor.AppliedResultantAtStep(stages, 1, 0.5, nodes);

        Assert.Equal(-1150, applied.Fz, 12);
    }

    [Fact]
    public void NodalForce_MomentAboutOrigin_IsCrossProductPlusMoment()
    {
        ShellResultant force = ShellResultantMath.NodalForce(2, 0, 0,
            new ShellResultant(0, 0, 1000, 0, 0, 0));

        Assert.Equal(0, force.Fx, 12);
        Assert.Equal(1000, force.Fz, 12);
        Assert.Equal(-2000, force.My, 12);
    }

    [Fact]
    public void ReactionResultant_SumsForceAndMomentWithNodeCoordinates()
    {
        var nodes = new Dictionary<int, NormalizedShellNode>
        {
            [1] = new(1, 0, 0, 0, new bool[6], null),
            [2] = new(2, 4, 0, 0, new bool[6], null)
        };
        var reactions = new[]
        {
            new ShellNodeReaction(1, 0, 0, 500, 0, 0, 0),
            new ShellNodeReaction(2, 0, 0, 500, 0, 0, 0)
        };

        ShellResultant resultant = ShellEquilibriumAuditor.ReactionResultant(reactions, nodes);

        Assert.Equal(1000, resultant.Fz, 12);
        Assert.Equal(-2000, resultant.My, 12);
    }

    [Fact]
    public void Evaluate_WithinTolerance_Passes()
    {
        var report = ShellEquilibriumAuditor.Evaluate(
            stepIndex: 1, stageIndex: 0, loadFactor: 1.0,
            applied: new ShellResultant(0, 0, -1000, 0, 0, 0),
            reaction: new ShellResultant(0, 0, 1000.0005, 0, 0, 0),
            new ShellAuditPolicy { AbsoluteEquilibriumTolerance = 1e-3, RelativeEquilibriumTolerance = 1e-3 });

        Assert.True(report.Pass);
        Assert.True(report.AbsoluteError <= 1e-3);
    }

    [Fact]
    public void Evaluate_BeyondTolerance_FailsWithEquilibriumNotSatisfiedCapability()
    {
        var report = ShellEquilibriumAuditor.Evaluate(
            stepIndex: 1, stageIndex: 0, loadFactor: 1.0,
            applied: new ShellResultant(0, 0, -1000, 0, 0, 0),
            reaction: new ShellResultant(0, 0, 700, 0, 0, 0),
            new ShellAuditPolicy { AbsoluteEquilibriumTolerance = 1e-3, RelativeEquilibriumTolerance = 1e-3 });

        Assert.False(report.Pass);
    }
}
