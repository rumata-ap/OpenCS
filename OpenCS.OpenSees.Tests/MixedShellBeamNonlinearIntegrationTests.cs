using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;

namespace OpenCS.OpenSees.Tests;

public sealed class MixedShellBeamNonlinearIntegrationTests
{
    [Fact]
    public async Task PlateWithNonlinearBeamColumn_SharedNode_ConvergesWithFullStepHistory()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();

        // Плита 1x1 м (узлы 1-4, элемент 10) + упругопластичная колонна (узел 5, элемент 100),
        // растущая из общего узла 3 плиты — общий узел доказывает, что shell и нелинейный
        // fiber-стержень участвуют в ОДНОМ equilibrium-цикле.
        NormalizedShellNode[] nodes =
        [
            new(1, 0, 0, 0, [true, true, true, true, true, true], null),
            new(2, 1, 0, 0, [true, true, true, true, true, true], null),
            new(3, 1, 1, 0, new bool[6], null),
            new(4, 0, 1, 0, new bool[6], null),
            new(5, 1, 1, -1, [true, true, true, true, true, true], "column-base")
        ];

        var steelMaterial = new OpenSeesMaterialDefinition { Tag = 40, Native = new Steel01Spec(4e8, 2e11, 0.01) };
        var columnSection = new OpenSeesSectionModel
        {
            GJ = 1e6,
            Materials = [steelMaterial],
            Fibers = [
                new(0.05, 0, 0.001, 40), new(-0.05, 0, 0.001, 40),
                new(0, 0.05, 0.001, 40), new(0, -0.05, 0.001, 40)]
        };

        var model = new ShellOpenSeesModel
        {
            Nodes = nodes,
            Materials = [new(1, "concrete", new ElasticIsotropicShellMaterialSpec(30e9, 0.2))],
            Sections = [new(20, "plate", 0.2, ShellFrame.Identity,
                [
                    new(0, ShellLayerKind.Concrete, -0.05, 0.1, 1, 0, "layer:0"),
                    new(1, ShellLayerKind.Concrete, 0, 0.1, 1, 0, "layer:1"),
                    new(2, ShellLayerKind.Concrete, 0.05, 0.1, 1, 0, "layer:2")
                ],
                ShellMappingMode.Exact, [], "section-fingerprint")],
            Elements = [new(10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], 20, "section-fingerprint",
                ShellFrame.Identity, ShellIntegrationPolicy.Full, null)],
            NonlinearBeamSections = new Dictionary<int, OpenSeesSectionModel> { [30] = columnSection },
            NonlinearBeamElements = [new(100, 5, 3, 30, 3, (1, 0, 0))],
            Stages = [new()
            {
                Tag = "vertical-load",
                LoadFactorStep = 0.25,
                MaxLoadFactor = 1.0,
                Loads = [new(3, 0, 0, -20000, 0, 0, 0)]
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
        Assert.True(result.Steps.Count >= 4, $"Ожидалось минимум 4 шага (λ 0.25→1.0), получено {result.Steps.Count}.");
        Assert.Equal(0.25, result.Steps[0].LoadFactor, precision: 6);
        Assert.Equal(1.0, result.Steps[^1].LoadFactor, precision: 6);
        Assert.Contains(result.Displacements, d => d.NodeTag == 3 && Math.Abs(d.Uz) > 1e-9);
        Assert.NotEmpty(result.Steps[^1].BeamElementForces);
    }
}
