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

public sealed class PlateRebarFieldShellIntegrationTests
{
    // UniaxialMaterialTag фиксированный, заведомо выше диапазона автонумерации MapMesh
    // (concrete=1, rebar-обёртка=2) — тег добавляется в модель вручную ниже, т.к.
    // IPlateSectionShellMaterialResolver сегодня не умеет самостоятельно регистрировать
    // зависимый uniaxialMaterial (см. design doc, раздел "Real OpenSees fixture").
    private const int RebarUniaxialTag = 500;
    private const double RebarE = 200e9;

    [Fact]
    public async Task MapMesh_ElementWithZoneRebar_IsStifferThanBaselineElement()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();

        var section = new PlateSection { H = 0.2, NLayers = 4 };
        var zone = new RebarZone
        {
            Face = RebarFace.PlusN,
            Operation = RebarZoneOperation.Replace,
            Polygon =
            [
                new() { U = 5, V = 0 }, new() { U = 7, V = 0 },
                new() { U = 7, V = 2 }, new() { U = 5, V = 2 },
            ],
            Layout = new PlateRebarLayer { Asx = 0.004, Zsx = 0.09 },
        };
        var field = new PlateRebarField([], [zone]);

        // Элемент 100 — вне зоны (baseline), элемент 101 — внутри зоны армирования.
        // (u,v) — локальные координаты PlateSection, независимые от глобальных координат
        // узлов модели ниже.
        var centroids = new (int ElementId, double U, double V)[] { (100, 0.5, 0.5), (101, 6, 1) };

        var mapped = PlateRebarFieldOpenSeesMapper.MapMesh(
            section, field, ShellFrame.Identity, new RebarCapableResolver(), centroids);

        Assert.Equal(2, mapped.Sections.Count);

        var materials = mapped.Materials
            .Append(new NativeShellMaterialDefinition(
                RebarUniaxialTag, "rebar:uniaxial", new ElasticUniaxialShellMaterialSpec(RebarE)))
            .ToArray();

        ShellOpenSeesModel model = BuildModel(mapped, materials);
        ShellResult result = await RunAsync(executable, model);

        double UzAverage(IEnumerable<int> freeNodes) => result.Displacements
            .Where(d => freeNodes.Contains(d.NodeTag))
            .Average(d => Math.Abs(d.Uz));

        double uzBaseline = UzAverage([2, 3]);
        double uzReinforced = UzAverage([6, 7]);

        Assert.True(uzReinforced < uzBaseline,
            $"Элемент с доп. арматурой должен быть жёстче: uzBaseline={uzBaseline:e3}, uzReinforced={uzReinforced:e3}");
    }

    private static ShellOpenSeesModel BuildModel(
        PlateRebarFieldShellMappingResult mapped,
        IReadOnlyList<NativeShellMaterialDefinition> materials)
    {
        var sectionByTag = mapped.Sections.ToDictionary(s => s.Tag);
        int tagBaseline = mapped.ElementSectionTag[100];
        int tagReinforced = mapped.ElementSectionTag[101];

        NormalizedShellNode[] nodes =
        [
            new(1, 0, 0, 0, [true, true, true, true, true, true], "A:1"),
            new(2, 1, 0, 0, new bool[6], "A:2"),
            new(3, 1, 1, 0, new bool[6], "A:3"),
            new(4, 0, 1, 0, [true, true, true, true, true, true], "A:4"),
            new(5, 3, 0, 0, [true, true, true, true, true, true], "B:5"),
            new(6, 4, 0, 0, new bool[6], "B:6"),
            new(7, 4, 1, 0, new bool[6], "B:7"),
            new(8, 3, 1, 0, [true, true, true, true, true, true], "B:8"),
        ];

        return new ShellOpenSeesModel
        {
            Nodes = nodes,
            Materials = materials,
            Sections = mapped.Sections,
            Elements =
            [
                new(100, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], tagBaseline,
                    sectionByTag[tagBaseline].Fingerprint, ShellFrame.Identity, ShellIntegrationPolicy.Full, "A"),
                new(101, ShellElementKind.ASDShellQ4, [5, 6, 7, 8], tagReinforced,
                    sectionByTag[tagReinforced].Fingerprint, ShellFrame.Identity, ShellIntegrationPolicy.Full, "B"),
            ],
            Loads =
            [
                new(2, 0, 0, -1000, 0, 0, 0), new(3, 0, 0, -1000, 0, 0, 0),
                new(6, 0, 0, -1000, 0, 0, 0), new(7, 0, 0, -1000, 0, 0, 0),
            ]
        };
    }

    private static async Task<ShellResult> RunAsync(string executable, ShellOpenSeesModel model)
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
        ShellResult result = new ShellResultParser().Parse(
            fixture.Directory, model.Elements.ToDictionary(e => e.Tag));
        Assert.Equal("completed", result.Status);
        return result;
    }

    private sealed class RebarCapableResolver : IPlateSectionShellMaterialResolver
    {
        public NativeShellMaterialDefinition ResolveConcrete(int sourceMaterialId) =>
            new(1, $"concrete:{sourceMaterialId}", new ElasticIsotropicShellMaterialSpec(30e9, 0.2));

        public NativeShellMaterialDefinition ResolveRebar(int sourceMaterialId) =>
            new(2, $"rebar:{sourceMaterialId}", new PlateRebarShellMaterialSpec(RebarUniaxialTag, 0));
    }
}
