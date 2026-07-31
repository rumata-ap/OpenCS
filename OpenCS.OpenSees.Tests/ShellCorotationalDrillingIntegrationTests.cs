using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;

namespace OpenCS.OpenSees.Tests;

public sealed class ShellCorotationalDrillingIntegrationTests
{
    [Fact]
    public async Task Q4WithCorotationalAndDrillingStabilization_ConvergesUnderLoad()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();

        const string fingerprint = "corotational-drilling-fingerprint";
        var model = new ShellOpenSeesModel
        {
            Nodes =
            [
                new(1, 0, 0, 0, [true, true, true, true, true, true], null),
                new(2, 1, 0, 0, [true, true, true, true, true, true], null),
                new(3, 1, 1, 0, new bool[6], null),
                new(4, 0, 1, 0, new bool[6], null)
            ],
            Materials = [new(1, "concrete", new ElasticIsotropicShellMaterialSpec(30e9, 0.2))],
            Sections = [new(20, "plate", 0.2, ShellFrame.Identity,
                [
                    new(0, ShellLayerKind.Concrete, -0.05, 0.1, 1, 0, "layer:0"),
                    new(1, ShellLayerKind.Concrete, 0, 0.1, 1, 0, "layer:1"),
                    new(2, ShellLayerKind.Concrete, 0.05, 0.1, 1, 0, "layer:2")
                ],
                ShellMappingMode.Exact, [], fingerprint)],
            Elements = [new(10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], 20, fingerprint,
                ShellFrame.Identity, ShellIntegrationPolicy.Full, null)],
            ShellCorotational = true,
            Drilling = new DrillingPolicy { Mode = ShellDrillingMode.Stabilization, StabilizationValue = 1.0 },
            Stages = [new()
            {
                Tag = "lateral-push",
                LoadFactorStep = 0.2,
                MaxLoadFactor = 1.0,
                Loads = [new(3, 0, 0, -20000, 0, 0, 0), new(4, 0, 0, -20000, 0, 0, 0)]
            }]
        };

        using var fixture = new ShellArtifactFixture();
        string scriptPath = Path.Combine(fixture.Directory, "script.tcl");
        File.WriteAllText(scriptPath, new ShellTclGenerator().Generate(model));

        OpenSeesRunResult run = await new OpenSeesProcessRunner().RunAsync(new OpenSeesRunRequest
        {
            ExecutablePath = executable,
            WorkingDirectory = fixture.Directory,
            ScriptPath = scriptPath,
            Timeout = TimeSpan.FromSeconds(30)
        }, CancellationToken.None);

        Assert.True(run.ExitCode == 0, $"stdout:\n{run.Stdout}\nstderr:\n{run.Stderr}");

        ShellResult result = new ShellResultParser().Parse(
            fixture.Directory, model.Elements.ToDictionary(e => e.Tag));

        Assert.Equal("completed", result.Status);
        Assert.True(result.Steps.Count >= 5, $"Ожидалось минимум 5 шагов (λ 0.2→1.0), получено {result.Steps.Count}.");
        Assert.Equal(1.0, result.Steps[^1].LoadFactor, precision: 6);
    }
}
