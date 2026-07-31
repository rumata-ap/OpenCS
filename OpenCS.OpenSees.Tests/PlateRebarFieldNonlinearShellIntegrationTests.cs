using CScore;
using CScore.PlateRebar;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;
using ShellResult = OpenCS.OpenSees.Structural.ShellResult;

namespace OpenCS.OpenSees.Tests;

public sealed class PlateRebarFieldNonlinearShellIntegrationTests
{
    private static (Material Concrete, Material Rebar) NonlinearMaterials() =>
    (
        new Material
        {
            Id = 1, Tag = "B25", Type = MatType.Concrete,
            C = new MaterialChars { E = 30_000_000, Fc = -17_000, Ft = 1_150, Ec0 = -0.002, Ec2 = -0.0035 }
        },
        new Material
        {
            Id = 2, Tag = "A400", Type = MatType.ReSteelF,
            C = new MaterialChars { E = 200_000_000, Ft = 355_000, Ru = 500_000, Et2 = 0.05 }
        }
    );

    private static PlateSectionShellMaterialResolver NonlinearResolver()
    {
        (Material concrete, Material rebar) = NonlinearMaterials();
        var lookup = new Dictionary<int, Material> { [1] = concrete, [2] = rebar };
        return new PlateSectionShellMaterialResolver(
            id => lookup.GetValueOrDefault(id), CalcType.C, SteelModelKind.Steel02, null);
    }

    [Fact]
    public async Task MembraneTensionAndCompression_WithZoneRebar_Converges()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();

        var section = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 };
        var background = new PlateRebarLayer { Asx = 0.001, Zsx = 0.09, MaterialId = 2, Face = RebarFace.PlusN };
        var zone = new RebarZone
        {
            Face = RebarFace.PlusN, Operation = RebarZoneOperation.Replace, Priority = 1,
            Polygon = [new() { U = 5, V = 0 }, new() { U = 7, V = 0 }, new() { U = 7, V = 2 }, new() { U = 5, V = 2 }],
            Layout = new PlateRebarLayer { Asx = 0.004, Zsx = 0.09, MaterialId = 2 },
        };
        var field = new PlateRebarField([background], [zone]);

        var centroids = new (int ElementId, double U, double V)[]
        {
            (100, 0.5, 0.5), (101, 6, 1), (102, 0.5, 0.5), (103, 6, 1)
        };
        PlateRebarFieldShellMappingResult mapped = PlateRebarFieldOpenSeesMapper.MapMesh(
            section, field, ShellFrame.Identity, NonlinearResolver(), centroids);
        Assert.Equal(2, mapped.Sections.Count);

        var sectionByTag = mapped.Sections.ToDictionary(s => s.Tag);
        int tagBaseline = mapped.ElementSectionTag[100];
        int tagZone = mapped.ElementSectionTag[101];
        Assert.Equal(tagBaseline, mapped.ElementSectionTag[102]);
        Assert.Equal(tagZone, mapped.ElementSectionTag[103]);

        NormalizedShellNode[] nodes = BuildFourPanelNodes();

        var model = new ShellOpenSeesModel
        {
            Nodes = nodes,
            Materials = mapped.Materials,
            Sections = mapped.Sections,
            Elements =
            [
                new(100, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], tagBaseline,
                    sectionByTag[tagBaseline].Fingerprint, ShellFrame.Identity, ShellIntegrationPolicy.Full, "tension:baseline"),
                new(101, ShellElementKind.ASDShellQ4, [5, 6, 7, 8], tagZone,
                    sectionByTag[tagZone].Fingerprint, ShellFrame.Identity, ShellIntegrationPolicy.Full, "tension:zone"),
                new(102, ShellElementKind.ASDShellQ4, [9, 10, 11, 12], tagBaseline,
                    sectionByTag[tagBaseline].Fingerprint, ShellFrame.Identity, ShellIntegrationPolicy.Full, "compression:baseline"),
                new(103, ShellElementKind.ASDShellQ4, [13, 14, 15, 16], tagZone,
                    sectionByTag[tagZone].Fingerprint, ShellFrame.Identity, ShellIntegrationPolicy.Full, "compression:zone"),
            ],
            Stages =
            [
                new()
                {
                    Tag = "membrane",
                    LoadFactorStep = 0.05,
                    MaxLoadFactor = 1.0,
                    Loads =
                    [
                        new(3, 0, 5000, 0, 0, 0, 0), new(4, 0, 5000, 0, 0, 0, 0),
                        new(7, 0, 5000, 0, 0, 0, 0), new(8, 0, 5000, 0, 0, 0, 0),
                        new(11, 0, -5000, 0, 0, 0, 0), new(12, 0, -5000, 0, 0, 0, 0),
                        new(15, 0, -5000, 0, 0, 0, 0), new(16, 0, -5000, 0, 0, 0, 0),
                    ]
                }
            ]
        };

        using ShellIntegrationRun run = await RunAsync(executable, model);
        ShellResult result = run.Result;

        Assert.All(result.Steps, step => Assert.True(step.Converged, $"Шаг {step.StepIndex} не сошёлся."));
        Assert.Equal(1.0, result.Steps[^1].LoadFactor, precision: 6);

        double UyAverage(IEnumerable<int> nodeTags) => result.Displacements
            .Where(d => nodeTags.Contains(d.NodeTag)).Average(d => Math.Abs(d.Uy));

        double tensionBaseline = UyAverage([3, 4]);
        double tensionZone = UyAverage([7, 8]);
        double compressionBaseline = UyAverage([11, 12]);
        double compressionZone = UyAverage([15, 16]);

        Assert.True(tensionZone < tensionBaseline,
            $"Зона армирования должна быть жёстче на растяжение: baseline={tensionBaseline:e3}, zone={tensionZone:e3}");
        Assert.True(compressionZone < compressionBaseline,
            $"Зона армирования должна быть жёстче на сжатие: baseline={compressionBaseline:e3}, zone={compressionZone:e3}");
    }

    /// <summary>Четыре независимых Q4-панели 1x1 м, каждая — нижний край (y=0) полностью
    /// зафиксирован, верхний край (y=1) свободен в плоскости (мембранная растяжение/сжатие)
    /// и зафиксирован из плоскости, как в ShellMaterialStateIntegrationTests.CreateNonlinearQ4Model.</summary>
    private static NormalizedShellNode[] BuildFourPanelNodes()
    {
        NormalizedShellNode Fixed(int tag, double x, double y) =>
            new(tag, x, y, 0, [true, true, true, true, true, true], null);
        NormalizedShellNode Free(int tag, double x, double y) =>
            new(tag, x, y, 0, [false, false, true, true, true, false], null);

        var nodes = new List<NormalizedShellNode>();
        double[] offsets = [0, 3, 6, 9];
        int tag = 1;
        foreach (double x0 in offsets)
        {
            nodes.Add(Fixed(tag++, x0, 0));
            nodes.Add(Fixed(tag++, x0 + 1, 0));
            nodes.Add(Free(tag++, x0 + 1, 1));
            nodes.Add(Free(tag++, x0, 1));
        }
        return [.. nodes];
    }

    private sealed class ShellIntegrationRun(ShellArtifactFixture fixture, ShellResult result) : IDisposable
    {
        public ShellResult Result { get; } = result;
        public string Directory => fixture.Directory;
        public void Dispose() => fixture.Dispose();
    }

    private static async Task<ShellIntegrationRun> RunAsync(string executable, ShellOpenSeesModel model)
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

        Assert.True(run.ExitCode == 0, $"stdout:\n{run.Stdout}\nstderr:\n{run.Stderr}");
        ShellResult result = new ShellResultParser().Parse(
            fixture.Directory, model.Elements.ToDictionary(e => e.Tag));
        Assert.Equal("completed", result.Status);
        return new ShellIntegrationRun(fixture, result);
    }
}
