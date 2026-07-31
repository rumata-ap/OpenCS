using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;

namespace OpenCS.OpenSees.Tests;

public sealed class BeamFiberStateIntegrationTests
{
    [Fact]
    public async Task MixedShellBeam_RecordsFiberCatalogAndFinalStepState()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        ShellOpenSeesModel model = CreateModel();

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
            fixture.Directory, model.Elements.ToDictionary(element => element.Tag));

        Assert.Equal("completed", result.Status);
        Assert.NotNull(result.StateCatalog);
        ShellStateCatalog catalog = result.StateCatalog!;
        Assert.Equal(3 * 4, catalog.BeamFiberLocations.Count);
        Assert.All(catalog.BeamFiberLocations, location =>
        {
            Assert.Equal(100, location.ElementTag);
            Assert.InRange(location.IntegrationPoint, 1, 3);
            Assert.InRange(location.FiberIndex, 0, 3);
        });

        var states = new ShellStateParser().ParseBeamFibers(
            fixture.Directory, catalog, 100, 3, 0, 4);
        var state = Assert.Single(states);
        var shellStates = new ShellStateParser().ParseShellLayers(
            fixture.Directory, catalog, 10, 1, 1, 4);
        var shellState = Assert.Single(shellStates);

        Assert.Equal(ShellMaterialStateLocationKind.BeamFiber, state.Key.LocationKind);
        Assert.Equal(100, state.Key.ElementTag);
        Assert.Equal(3, state.Key.IntegrationPoint);
        Assert.Equal(0, state.Key.LocationIndex);
        Assert.Equal(shellState.Key.StepIndex, state.Key.StepIndex);
        Assert.Equal(shellState.Key.StageIndex, state.Key.StageIndex);
        Assert.Equal(shellState.Key.LoadFactor, state.Key.LoadFactor, precision: 12);
        Assert.True(double.IsFinite(state.StressPa));
        Assert.True(double.IsFinite(state.Strain));
        Assert.Contains(File.ReadLines(Path.Combine(fixture.Directory, "shell_beam_fiber_states.out")),
            line => !string.IsNullOrWhiteSpace(line));
    }

    private static ShellOpenSeesModel CreateModel()
    {
        NormalizedShellNode[] nodes =
        [
            new(1, 0, 0, 0, [true, true, true, true, true, true], null),
            new(2, 1, 0, 0, [true, true, true, true, true, true], null),
            new(3, 1, 1, 0, new bool[6], null),
            new(4, 0, 1, 0, new bool[6], null),
            new(5, 1, 1, -1, [true, true, true, true, true, true], "column-base")
        ];

        var steelMaterial = new OpenSeesMaterialDefinition
        {
            Tag = 40,
            Native = new Steel01Spec(4e8, 2e11, 0.01)
        };
        var columnSection = new OpenSeesSectionModel
        {
            GJ = 1e6,
            Materials = [steelMaterial],
            Fibers =
            [
                new(0.05, 0, 0.001, 40), new(-0.05, 0, 0.001, 40),
                new(0, 0.05, 0.001, 40), new(0, -0.05, 0.001, 40)
            ]
        };

        return new ShellOpenSeesModel
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
            Elements = [new(10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], 20,
                "section-fingerprint", ShellFrame.Identity, ShellIntegrationPolicy.Full, null)],
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
    }
}
