using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class ShellResultsTests
{
    [Fact]
    public void ShellResult_TopLevelViewsReflectLastStep()
    {
        var step1 = new RCShellStepResult(1, 0, 0.5, true,
            [new ShellNodeDisplacement(1, 0.001, 0, 0, 0, 0, 0)], [], [], [], []);
        var step2 = new RCShellStepResult(2, 0, 1.0, true,
            [new ShellNodeDisplacement(1, 0.002, 0, 0, 0, 0, 0)], [], [], [], []);

        var result = new ShellResult { Steps = [step1, step2] };

        Assert.Equal(2, result.Steps.Count);
    }
}
