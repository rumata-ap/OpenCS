using CScore.Planar;
using CScore.Fem;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public sealed class PlanarBoundaryActionOpenSeesIntegrationTests
{
    [Fact]
    public async Task ForceBoundaryActionRunsThroughOpenSeesAndBalancesReaction()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var mapped = new PlanarBoundaryActionMeshMappingResult
        {
            NodalActions =
            [
                new(1, new(0, 0, -1000), PlanarVector3.Zero),
                new(2, new(0, 0, -1000), PlanarVector3.Zero)
            ]
        };
        var built = PlanarBoundaryActionOpenSeesAdapter.Apply(
            ShellModelFixtures.Q4Elastic(),
            mapped,
            new Dictionary<int, int> { [1] = 2, [2] = 3 },
            0);

        Assert.True(built.IsCalculable, Diagnostics(built.Diagnostics));
        ShellResult result = await RunAsync(executable, built.Model!);

        Assert.Equal("completed", result.Status);
        Assert.True(result.Displacements.All(displacement =>
            double.IsFinite(displacement.Ux) && double.IsFinite(displacement.Uy) && double.IsFinite(displacement.Uz)));
        Assert.Equal(2000, Math.Abs(result.Reactions.Sum(reaction => reaction.Fz)), 1e-3);
    }

    [Fact]
    public async Task KinematicBoundaryActionRunsThroughOpenSeesWithoutFixedZero()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var mapped = new PlanarBoundaryActionMeshMappingResult
        {
            PrescribedDofs = new Dictionary<(int NodeIndex, int Dof), double>
            {
                [(1, 2)] = -0.001
            }
        };
        var built = PlanarBoundaryActionOpenSeesAdapter.Apply(
            ShellModelFixtures.Q4Elastic(),
            mapped,
            new Dictionary<int, int> { [1] = 2 },
            0);

        Assert.True(built.IsCalculable, Diagnostics(built.Diagnostics));
        string script = new ShellTclGenerator().Generate(built.Model!);
        Assert.Contains("sp 2 3 -0.001", script);
        Assert.DoesNotContain("fix 2 0 0 1", script);
        ShellResult result = await RunAsync(executable, built.Model!);

        Assert.Equal("completed", result.Status);
        Assert.Equal(-0.001, result.Displacements.Single(item => item.NodeTag == 2).Uz, 5);
    }

    static async Task<ShellResult> RunAsync(string executable, ShellOpenSeesModel model)
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

        Assert.Equal(0, run.ExitCode);
        return new ShellResultParser().Parse(fixture.Directory, model.Elements.ToDictionary(element => element.Tag));
    }

    static string Diagnostics(IEnumerable<FemValidationDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.Message));
}
