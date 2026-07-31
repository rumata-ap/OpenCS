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

    [Fact]
    public async Task PureBending_WithAsymmetricZoneRebar_CracksUnreinforcedFace()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();

        var section = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 };
        var zoneTensionFace = new RebarZone
        {
            Face = RebarFace.PlusN, Operation = RebarZoneOperation.Add, Priority = 1,
            Polygon = [new() { U = 0, V = 0 }, new() { U = 2, V = 0 }, new() { U = 2, V = 1 }, new() { U = 0, V = 1 }],
            Layout = new PlateRebarLayer { Asx = 0.004, Zsx = 0.09, MaterialId = 2 },
        };
        var zoneCompressionFace = new RebarZone
        {
            Face = RebarFace.MinusN, Operation = RebarZoneOperation.Add, Priority = 1,
            Polygon = [new() { U = 0, V = 4 }, new() { U = 2, V = 4 }, new() { U = 2, V = 5 }, new() { U = 0, V = 5 }],
            Layout = new PlateRebarLayer { Asx = 0.004, Zsx = -0.09, MaterialId = 2 },
        };
        var field = new PlateRebarField([], [zoneTensionFace, zoneCompressionFace]);

        // Центроиды 1/0.5 и 1/4.5 попадают только в свою зону — резолвер видит их независимо,
        // как отдельные конструктивные элементы (см. PlateRebarFieldResolver.Resolve, вызывается
        // на каждой грани отдельно). Реальная геометрия узлов ниже не связана с (u, v).
        var centroids = new (int ElementId, double U, double V)[] { (200, 1, 0.5), (201, 1, 4.5) };
        PlateRebarFieldShellMappingResult mapped = PlateRebarFieldOpenSeesMapper.MapMesh(
            section, field, ShellFrame.Identity, NonlinearResolver(), centroids);
        Assert.Equal(2, mapped.Sections.Count);

        var sectionByTag = mapped.Sections.ToDictionary(s => s.Tag);
        RCShellLayeredSection sectionTensionReinforced = sectionByTag[mapped.ElementSectionTag[200]];
        RCShellLayeredSection sectionCompressionReinforced = sectionByTag[mapped.ElementSectionTag[201]];

        // Статическая проверка (до любого запуска OpenSees): армирование действительно
        // расположено на РАЗНЫХ гранях, а не задублировано на обеих.
        Assert.Contains(sectionTensionReinforced.Layers, l => l.Kind == ShellLayerKind.RebarX && l.CenterZ > 0);
        Assert.DoesNotContain(sectionTensionReinforced.Layers, l => l.Kind == ShellLayerKind.RebarX && l.CenterZ < 0);
        Assert.Contains(sectionCompressionReinforced.Layers, l => l.Kind == ShellLayerKind.RebarX && l.CenterZ < 0);
        Assert.DoesNotContain(sectionCompressionReinforced.Layers, l => l.Kind == ShellLayerKind.RebarX && l.CenterZ > 0);

        // Геометрия и нагрузка — по образцу ShellReferenceFixtures.Q4EndMoment: чистый узловой
        // момент на свободном крае, без сдвига. Положительный My на свободном крае растягивает
        // грань +Z (+n) — см. CLAUDE.md, Sign Conventions, «Плиты». TipMomentEach подобран так,
        // чтобы заведомо превысить момент трещинообразования: при Ft≈1.15 МПа (B25) и H=0.2 м
        // упругий Mcr на метр ширины ~ Ft*H²/6 ≈ 7.7 кН·м/м — при меньшем моменте (изначально
        // пробовался 500 Н·м) сечение остаётся упругим, и изгибная жёсткость не зависит от
        // знака z (Σ E·A·z² симметрична), поэтому арматура сверху/снизу не давала разницы.
        const double Length = 2.0, Width = 1.0, TipMomentEach = 6000.0;
        NormalizedShellNode[] nodes =
        [
            new(1, 0, 0, 0, [true, true, true, true, true, true], "tension-face:fixed:1"),
            new(2, Length, 0, 0, new bool[6], "tension-face:free:2"),
            new(3, Length, Width, 0, new bool[6], "tension-face:free:3"),
            new(4, 0, Width, 0, [true, true, true, true, true, true], "tension-face:fixed:4"),
            new(5, 0, 0, 10, [true, true, true, true, true, true], "compression-face:fixed:5"),
            new(6, Length, 0, 10, new bool[6], "compression-face:free:6"),
            new(7, Length, Width, 10, new bool[6], "compression-face:free:7"),
            new(8, 0, Width, 10, [true, true, true, true, true, true], "compression-face:fixed:8"),
        ];

        var model = new ShellOpenSeesModel
        {
            Nodes = nodes,
            Materials = mapped.Materials,
            Sections = mapped.Sections,
            Elements =
            [
                new(200, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], sectionTensionReinforced.Tag,
                    sectionTensionReinforced.Fingerprint, ShellFrame.Identity, ShellIntegrationPolicy.Full, "bending:tension-face-reinforced"),
                new(201, ShellElementKind.ASDShellQ4, [5, 6, 7, 8], sectionCompressionReinforced.Tag,
                    sectionCompressionReinforced.Fingerprint, ShellFrame.Identity, ShellIntegrationPolicy.Full, "bending:compression-face-reinforced"),
            ],
            Stages =
            [
                new()
                {
                    Tag = "bending",
                    LoadFactorStep = 0.01,
                    MaxLoadFactor = 1.0,
                    Loads =
                    [
                        new(2, 0, 0, 0, 0, TipMomentEach, 0), new(3, 0, 0, 0, 0, TipMomentEach, 0),
                        new(6, 0, 0, 0, 0, TipMomentEach, 0), new(7, 0, 0, 0, 0, TipMomentEach, 0),
                    ]
                }
            ]
        };

        using ShellIntegrationRun run = await RunAsync(executable, model);
        ShellResult result = run.Result;

        Assert.All(result.Steps, step => Assert.True(step.Converged, $"Шаг {step.StepIndex} не сошёлся."));
        Assert.Equal(1.0, result.Steps[^1].LoadFactor, precision: 6);

        double RyAverage(int firstFreeNodeTag) => result.Displacements
            .Where(d => d.NodeTag == firstFreeNodeTag || d.NodeTag == firstFreeNodeTag + 1)
            .Average(d => Math.Abs(d.Ry));

        double rotationTensionReinforced = RyAverage(2);
        double rotationCompressionReinforced = RyAverage(6);
        Assert.True(rotationTensionReinforced < rotationCompressionReinforced,
            $"Плита с арматурой на растянутой грани должна поворачиваться меньше под тем же моментом: " +
            $"tensionReinforced={rotationTensionReinforced:e3}, compressionReinforced={rotationCompressionReinforced:e3}");

        // Material state introspection (срез 4) — впервые применяется к секции, построенной
        // PlateRebarFieldOpenSeesMapper (а не однородным PlateSectionOpenSeesMapper.Map, как во
        // всех существующих ShellMaterialStateIntegrationTests). Само отсутствие исключения при
        // парсинге уже подтверждает совместимость recorder/catalog-контракта с per-element
        // секциями моста PlateRebarField.
        Assert.NotNull(result.StateCatalog);
        RCShellLayer tensionConcreteA = sectionTensionReinforced.Layers
            .Where(l => l.Kind == ShellLayerKind.Concrete).OrderByDescending(l => l.CenterZ).First();
        RCShellLayer tensionConcreteB = sectionCompressionReinforced.Layers
            .Where(l => l.Kind == ShellLayerKind.Concrete).OrderByDescending(l => l.CenterZ).First();

        int finalStepIndex = result.Steps.Last(step => step.Converged).StepIndex;
        var stateParser = new ShellStateParser();
        var stateA = stateParser.ParseShellLayers(
            run.Directory, result.StateCatalog!, 200, 1, tensionConcreteA.Index + 1, finalStepIndex);
        var stateB = stateParser.ParseShellLayers(
            run.Directory, result.StateCatalog!, 201, 1, tensionConcreteB.Index + 1, finalStepIndex);

        Assert.Single(stateA);
        Assert.Single(stateB);
        Assert.All(stateA[0].Stress, value => Assert.True(double.IsFinite(value)));
        Assert.All(stateB[0].Stress, value => Assert.True(double.IsFinite(value)));
    }

    [Fact]
    public async Task ShellBeamJunction_WithZoneRebar_ConvergesAndTransfersForces()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();

        var section = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 };
        var zone = new RebarZone
        {
            Face = RebarFace.PlusN, Operation = RebarZoneOperation.Replace, Priority = 1,
            Polygon = [new() { U = 0, V = 0 }, new() { U = 2, V = 0 }, new() { U = 2, V = 2 }, new() { U = 0, V = 2 }],
            Layout = new PlateRebarLayer { Asx = 0.004, Zsx = 0.09, MaterialId = 2 },
        };
        var field = new PlateRebarField([], [zone]);

        // Единственный shell-элемент, его центроид (1,1) попадает внутрь зоны — секция плиты
        // строится мостом PlateRebarField, а не однородным PlateSectionOpenSeesMapper.Map, как
        // во всех существующих shell–beam junction тестах срезов 2–3.
        var centroids = new (int ElementId, double U, double V)[] { (10, 1, 1) };
        PlateRebarFieldShellMappingResult mapped = PlateRebarFieldOpenSeesMapper.MapMesh(
            section, field, ShellFrame.Identity, NonlinearResolver(), centroids);
        Assert.Single(mapped.Sections);
        RCShellLayeredSection plateSection = mapped.Sections[0];

        // Плита 1x1 м (узлы 1-4) + упругопластичная колонна (узел 5), растущая из общего узла 3
        // плиты — общий узел доказывает, что shell и нелинейный fiber-стержень участвуют в ОДНОМ
        // equilibrium-цикле (по образцу MixedShellBeamNonlinearIntegrationTests, но плита теперь
        // построена через реальные зоны армирования).
        NormalizedShellNode[] nodes =
        [
            new(1, 0, 0, 0, [true, true, true, true, true, true], null),
            new(2, 1, 0, 0, [true, true, true, true, true, true], null),
            new(3, 1, 1, 0, new bool[6], null),
            new(4, 0, 1, 0, new bool[6], null),
            new(5, 1, 1, -1, [true, true, true, true, true, true], "column-base"),
        ];

        var steelMaterial = new OpenSeesMaterialDefinition { Tag = 40, Native = new Steel01Spec(4e8, 2e11, 0.01) };
        var columnSection = new OpenSeesSectionModel
        {
            GJ = 1e6,
            Materials = [steelMaterial],
            Fibers =
            [
                new(0.05, 0, 0.001, 40), new(-0.05, 0, 0.001, 40),
                new(0, 0.05, 0.001, 40), new(0, -0.05, 0.001, 40),
            ]
        };

        var model = new ShellOpenSeesModel
        {
            Nodes = nodes,
            Materials = mapped.Materials,
            Sections = mapped.Sections,
            Elements = [new(10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4], plateSection.Tag,
                plateSection.Fingerprint, ShellFrame.Identity, ShellIntegrationPolicy.Full, "junction:plate")],
            NonlinearBeamSections = new Dictionary<int, OpenSeesSectionModel> { [30] = columnSection },
            NonlinearBeamElements = [new(100, 5, 3, 30, 3, (1, 0, 0))],
            Stages =
            [
                new()
                {
                    Tag = "vertical-load",
                    LoadFactorStep = 0.25,
                    MaxLoadFactor = 1.0,
                    Loads = [new(3, 0, 0, -20000, 0, 0, 0)]
                }
            ]
        };

        using ShellIntegrationRun run = await RunAsync(executable, model);
        ShellResult result = run.Result;

        Assert.All(result.Steps, step => Assert.True(step.Converged, $"Шаг {step.StepIndex} не сошёлся."));
        Assert.True(result.Steps.Count >= 4, $"Ожидалось минимум 4 шага (λ 0.25→1.0), получено {result.Steps.Count}.");
        Assert.Equal(1.0, result.Steps[^1].LoadFactor, precision: 6);

        var finalBeamForces = result.Steps[^1].BeamElementForces;
        Assert.NotEmpty(finalBeamForces);
        Assert.All(finalBeamForces, forces =>
        {
            Assert.True(double.IsFinite(forces.Ni)); Assert.True(double.IsFinite(forces.Qyi));
            Assert.True(double.IsFinite(forces.Qzi)); Assert.True(double.IsFinite(forces.Mxi));
            Assert.True(double.IsFinite(forces.Myi)); Assert.True(double.IsFinite(forces.Mzi));
            Assert.True(double.IsFinite(forces.Nj)); Assert.True(double.IsFinite(forces.Qyj));
            Assert.True(double.IsFinite(forces.Qzj)); Assert.True(double.IsFinite(forces.Mxj));
            Assert.True(double.IsFinite(forces.Myj)); Assert.True(double.IsFinite(forces.Mzj));
        });
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
