using CScore;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;
using OpenSeesShellResult = OpenCS.OpenSees.Structural.ShellResult;

namespace OpenCS.OpenSees.Tests;

public sealed class ShellMaterialStateIntegrationTests
{
    [Fact]
    public async Task NonlinearQ4WithFourConcreteLayers_RecordsFinalMaterialState()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        ShellOpenSeesModel model = CreateNonlinearQ4Model();

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
        OpenSeesShellResult result = new ShellResultParser().Parse(
            fixture.Directory, model.Elements.ToDictionary(element => element.Tag));
        Assert.Equal("completed", result.Status);

        var finalStep = result.Steps.Last(step => step.Converged);
        Assert.Equal(1.0, finalStep.LoadFactor, precision: 6);
        Assert.NotNull(result.StateCatalog);
        Assert.Equal(4 * 4 * 2, result.StateCatalog!.ShellLayerGroups.Count);

        var states = new ShellStateParser().ParseShellLayers(
            fixture.Directory, result.StateCatalog, 10, 4, 4, finalStep.StepIndex);
        var state = Assert.Single(states);
        Assert.Equal(ShellLayerKind.Concrete, state.ShellLayerKind);
        Assert.Equal(5, state.Stress.Count);
        Assert.Equal(5, state.Strain.Count);
        Assert.Contains(state.Stress, value => Math.Abs(value) > 1e-8);
        Assert.Contains(state.Strain, value => Math.Abs(value) > 1e-12);
    }

    [Fact]
    public async Task Q4WithTipLoad_RecordsEveryIntegrationPointAndLayer()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var model = ShellModelFixtures.Q4WithTipLoad();
        using ShellIntegrationRun run = await RunAsync(executable, model);

        Assert.Equal(4 * 4 * 2, run.Catalog.ShellLayerGroups.Count);
        Assert.All(run.Catalog.ShellLayerGroups, group =>
            Assert.Equal(5, group.ComponentCount));

        var states = new ShellStateParser().ParseShellLayers(
            run.Directory, run.Catalog, 10, 4, 4, 1);

        var state = Assert.Single(states);
        Assert.Equal(ShellMaterialStateLocationKind.ShellLayer, state.Key.LocationKind);
        Assert.Equal(5, state.Stress.Count);
        Assert.Equal(5, state.Strain.Count);
        Assert.Contains(state.Stress, value => Math.Abs(value) > 1e-12);
        Assert.Contains(state.Strain, value => Math.Abs(value) > 1e-16);
    }

    [Fact]
    public async Task T3FullWithTipLoad_RecordsThreeIntegrationPoints()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var model = ShellModelFixtures.T3WithTipLoad(ShellIntegrationPolicy.Full);
        using ShellIntegrationRun run = await RunAsync(executable, model);

        Assert.Equal(3 * 4 * 2, run.Catalog.ShellLayerGroups.Count);
        Assert.DoesNotContain(run.Catalog.ShellLayerGroups,
            group => group.IntegrationPoint > 3);
    }

    [Fact]
    public async Task T3ReducedWithTipLoad_RecordsOneIntegrationPoint()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var model = ShellModelFixtures.T3WithTipLoad(ShellIntegrationPolicy.Reduced);
        using ShellIntegrationRun run = await RunAsync(executable, model);

        Assert.Equal(1 * 4 * 2, run.Catalog.ShellLayerGroups.Count);
        Assert.DoesNotContain(run.Catalog.ShellLayerGroups,
            group => group.IntegrationPoint > 1);
    }

    private static async Task<ShellIntegrationRun> RunAsync(
        string executable,
        ShellOpenSeesModel model)
    {
        var fixture = new ShellArtifactFixture();
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
        Assert.True(run.ExitCode == 0,
            $"stdout:\n{run.Stdout}\nstderr:\n{run.Stderr}\nscript:\n{File.ReadAllText(scriptPath)}");

        OpenSeesShellResult result = new ShellResultParser().Parse(
            fixture.Directory, model.Elements.ToDictionary(element => element.Tag));
        Assert.Equal("completed", result.Status);
        Assert.NotNull(result.StateCatalog);
        return new ShellIntegrationRun(fixture, result.StateCatalog!);
    }

    private static ShellOpenSeesModel CreateNonlinearQ4Model()
    {
        var section = new PlateSection
        {
            H = 0.2,
            NLayers = 4,
            ConcreteMaterialId = 1,
            RebarMaterialId = 2
        };
        var concreteMaterial = new Material
        {
            Id = 1,
            Tag = "B25",
            Type = MatType.Concrete,
            C = new MaterialChars
            {
                E = 30_000_000,
                Fc = -17_000,
                Ft = 1_150,
                Ec0 = -0.002,
                Ec2 = -0.0035
            }
        };
        var materials = new Dictionary<int, Material> { [1] = concreteMaterial };
        var resolver = new PlateSectionShellMaterialResolver(
            id => materials.GetValueOrDefault(id), CalcType.C, SteelModelKind.Steel02, null);
        PlateSectionShellMappingResult mapped = PlateSectionOpenSeesMapper.Map(
            section, ShellFrame.Identity, resolver, sectionTag: 20);

        return new ShellOpenSeesModel
        {
            Nodes =
            [
                new(1, 0, 0, 0, [true, true, true, true, true, true], "nonlinear:1"),
                new(2, 1, 0, 0, [true, true, true, true, true, true], "nonlinear:2"),
                new(3, 1, 1, 0, [false, false, true, true, true, false], "nonlinear:3"),
                new(4, 0, 1, 0, [false, false, true, true, true, false], "nonlinear:4")
            ],
            Materials = mapped.Materials,
            Sections = [mapped.Section],
            Elements = [new(10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], 20,
                mapped.Section.Fingerprint, ShellFrame.Identity, ShellIntegrationPolicy.Full, "nonlinear:q4")],
            Stages = [new()
            {
                Tag = "tension",
                LoadFactorStep = 0.05,
                MaxLoadFactor = 1.0,
                Loads = [new(3, 0, 5000, 0, 0, 0, 0), new(4, 0, 5000, 0, 0, 0, 0)]
            }]
        };
    }

    private sealed class ShellIntegrationRun(ShellArtifactFixture fixture, ShellStateCatalog catalog) : IDisposable
    {
        public ShellStateCatalog Catalog { get; } = catalog;
        public string Directory => fixture.Directory;

        public void Dispose() => fixture.Dispose();
    }
}
