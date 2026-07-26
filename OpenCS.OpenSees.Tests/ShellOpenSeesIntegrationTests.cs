using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;

namespace OpenCS.OpenSees.Tests;

public sealed class ShellOpenSeesIntegrationTests
{
    [Fact]
    public void Q4Fixture_HasFourIntegrationPoints()
    {
        var model = ShellModelFixtures.Q4Elastic();
        var element = Assert.Single(model.Elements);

        Assert.Equal(OpenCS.OpenSees.Structural.ShellElementKind.ASDShellQ4, element.Kind);
        Assert.Equal(4, element.IntegrationPointCount);
    }

    [Fact]
    public async Task Q4Fixture_RunsThroughOpenSeesWhenExecutableIsAvailable()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var model = ShellModelFixtures.Q4Elastic();
        await RunAndAssertAsync(executable, model);
    }

    [Fact]
    public async Task T3FullFixture_RunsThroughOpenSeesWhenExecutableIsAvailable()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var model = ShellModelFixtures.T3Elastic(
            OpenCS.OpenSees.Structural.ShellIntegrationPolicy.Full);
        await RunAndAssertAsync(executable, model);
    }

    [Fact]
    public async Task T3ReducedFixture_RunsThroughOpenSeesWhenExecutableIsAvailable()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var model = ShellModelFixtures.T3Elastic(
            OpenCS.OpenSees.Structural.ShellIntegrationPolicy.Reduced);
        await RunAndAssertAsync(executable, model);
    }

    private static async Task RunAndAssertAsync(
        string executable,
        OpenCS.OpenSees.Structural.ShellOpenSeesModel model)
    {
        using var fixture = new ShellArtifactFixture();
        string scriptPath = Path.Combine(fixture.Directory, "script.tcl");
        File.WriteAllText(scriptPath, new ShellTclGenerator().Generate(model));

        OpenSeesRunResult run = await new OpenSeesProcessRunner().RunAsync(
            new OpenSeesRunRequest
            {
                ExecutablePath = executable,
                WorkingDirectory = fixture.Directory,
                ScriptPath = scriptPath,
                Timeout = TimeSpan.FromSeconds(30)
            }, CancellationToken.None);

        Assert.False(run.TimedOut, run.Stderr);
        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(Path.Combine(fixture.Directory, "completed.marker")),
            $"OpenSees did not create marker. stdout:\n{run.Stdout}\nstderr:\n{run.Stderr}\nscript:\n{File.ReadAllText(scriptPath)}");
        string marker = File.ReadAllText(Path.Combine(fixture.Directory, "completed.marker")).Trim();
        Assert.True(marker == "0",
            $"OpenSees analyze did not converge (marker={marker}). stdout:\n{run.Stdout}\nstderr:\n{run.Stderr}");
        ShellResult result = new ShellResultParser().Parse(
            fixture.Directory,
            model.Elements.ToDictionary(element => element.Tag));
        Assert.Equal("completed", result.Status);
        Assert.NotEmpty(result.Displacements);
        Assert.NotEmpty(result.SectionResultants);
    }
}
