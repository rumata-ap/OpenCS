using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using OpenCS.Gmsh;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;
using ShellResult = OpenCS.OpenSees.Structural.ShellResult;

namespace OpenCS.OpenSees.Tests.Audit;

/// <summary>Реальные OpenSees audit-тесты: равновесие Q4/T3/beam, угол арматуры,
/// provenance v2, preflight и mesh sensitivity smoke. Скипаются без executable.</summary>
public sealed class ShellAuditOpenSeesIntegrationTests
{
    [Fact]
    public async Task Q4WithTipLoad_EquilibriumPasses_WhenExecutableAvailable()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        ShellOpenSeesModel model = ShellModelFixtures.Q4WithTipLoad();

        ShellAnalysisRunResult run = await Runner().RunAsync(model, executable, CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.Completed, run.Outcome);
        ShellEquilibriumStepReport equilibrium = EquilibriumOf(model, run.Result!);
        Assert.True(equilibrium.Pass, $"Residual: {equilibrium.Residual}");
    }

    [Fact]
    public async Task T3WithTipLoad_EquilibriumPasses_WhenExecutableAvailable()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        ShellOpenSeesModel model = ShellModelFixtures.T3WithTipLoad(ShellIntegrationPolicy.Full);

        ShellAnalysisRunResult run = await Runner().RunAsync(model, executable, CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.Completed, run.Outcome);
        ShellEquilibriumStepReport equilibrium = EquilibriumOf(model, run.Result!);
        Assert.True(equilibrium.Pass, $"Residual: {equilibrium.Residual}");
    }

    [Fact]
    public async Task SharedNodeColumn_EquilibriumPasses_WhenExecutableAvailable()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        ShellOpenSeesModel model = ShellBeamConnectionFixtures.SharedNodeColumn();

        ShellAnalysisRunResult run = await Runner().RunAsync(model, executable, CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.Completed, run.Outcome);
        ShellEquilibriumStepReport equilibrium = EquilibriumOf(model, run.Result!);
        Assert.True(equilibrium.Pass, $"Residual: {equilibrium.Residual}");
    }

    [Fact]
    public async Task RebarAngle45_ElementRunsAndEquilibriumPasses_WhenExecutableAvailable()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();

        var section = new PlateSection
        {
            H = 0.2,
            NLayers = 2,
            RebarLayers = [new PlateRebarLayer
            {
                Asx = 0.001, Asy = 0.002, Zsx = -0.07, Zsy = 0.07, Angle = 45.0
            }]
        };
        PlateSectionShellMappingResult mapped = PlateSectionOpenSeesMapper.Map(
            section, ShellFrame.Identity, new RebarCapableResolver());

        Assert.Contains(mapped.Section.Layers,
            layer => layer.Kind == ShellLayerKind.RebarX && layer.DirectionDegrees == 45.0);
        Assert.Contains(mapped.Section.Layers,
            layer => layer.Kind == ShellLayerKind.RebarY && layer.DirectionDegrees == 135.0);

        var model = new ShellOpenSeesModel
        {
            Nodes =
            [
                new(1, 0, 0, 0, [true, true, true, true, true, true], "angle:1"),
                new(2, 1, 0, 0, new bool[6], "angle:2"),
                new(3, 1, 1, 0, new bool[6], "angle:3"),
                new(4, 0, 1, 0, [true, true, true, true, true, true], "angle:4")
            ],
            Materials = mapped.Materials,
            Sections = [mapped.Section],
            Elements = [new(10, ShellElementKind.ASDShellQ4, [1, 2, 3, 4],
                mapped.Section.Tag, mapped.Section.Fingerprint,
                ShellFrame.Identity, ShellIntegrationPolicy.Full, "angle:e:10")],
            Stages = [new() { Tag = "stage-1",
                Loads = [new(2, 0, 0, -1000, 0, 0, 0), new(3, 0, 0, -1000, 0, 0, 0)] }]
        };

        ShellAnalysisRunResult run = await Runner().RunAsync(model, executable, CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.Completed, run.Outcome);
        ShellEquilibriumStepReport equilibrium = EquilibriumOf(model, run.Result!);
        Assert.True(equilibrium.Pass, $"Residual: {equilibrium.Residual}");
    }

    [Fact]
    public async Task Q4Run_ProducesV2CatalogProvenance_WhenExecutableAvailable()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        ShellAnalysisRunResult run = await Runner().RunAsync(
            ShellModelFixtures.Q4Elastic(), executable, CancellationToken.None);

        Assert.Equal(ShellAnalysisOutcome.Completed, run.Outcome);
        ShellStateCatalog? catalog = run.Result!.StateCatalog;
        Assert.NotNull(catalog);
        Assert.Equal(ShellStateCatalogProvenanceKind.V2WithProvenance, catalog!.ProvenanceKind);
        Assert.NotEmpty(catalog.ShellLayerGroups);
        Assert.All(catalog.ShellLayerGroups, group =>
        {
            Assert.True(group.SectionTag > 0);
            Assert.True(group.MaterialTag > 0);
            Assert.NotNull(group.LayerKind);
            Assert.False(string.IsNullOrWhiteSpace(group.SourceId));
        });
    }

    [Fact]
    public void UnsupportedRegularization_StrictPreflight_BlocksWithoutOpenSees()
    {
        var policy = new ShellAuditPolicy
        {
            Mode = ShellAuditMode.Strict,
            Regularization = new ShellRegularizationPolicy { Mode = ShellRegularizationMode.CrackBand }
        };

        ShellAuditPreflightResult preflight = ShellAuditPreflight.Run(
            ShellModelFixtures.Q4Elastic(), V2Catalog(), policy, new ShellRegularizationCapability([]));

        Assert.False(preflight.IsCalculable);
        Assert.Contains(preflight.Diagnostics, diagnostic =>
            diagnostic.Code == ShellDiagnosticCodes.RegularizationUnsupported &&
            diagnostic.Severity == ShellDiagnosticSeverity.Blocking);
    }

    [Fact]
    public async Task Q4WithTipLoad_FullAuditFlow_Passes_WhenExecutableAvailable()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        ShellOpenSeesModel model = ShellModelFixtures.Q4WithTipLoad();
        var policy = new ShellAuditPolicy
        {
            Mode = ShellAuditMode.Strict,
            AbsoluteEquilibriumTolerance = 1e-2,
            RelativeEquilibriumTolerance = 1e-2
        };

        ShellAuditPreflightResult preflight = ShellAuditPreflight.Run(
            model, V2Catalog(), policy, new ShellRegularizationCapability([]));
        Assert.True(preflight.IsCalculable);

        ShellAnalysisRunResult run = await Runner().RunAsync(model, executable, CancellationToken.None);
        Assert.Equal(ShellAnalysisOutcome.Completed, run.Outcome);
        Assert.NotNull(run.Result!.StateCatalog);

        ShellEquilibriumStepReport equilibrium = EquilibriumOf(model, run.Result);
        ShellEnergyConfidence energy = ShellEnergyAuditor.DetermineConfidence(
            hasNativeEnergyResponse: false, hasStateIntegralData: false, hasLoadHistory: true);

        ShellAuditVerdict verdict = ShellAuditVerdictResolver.Resolve(
            preflight, [equilibrium], energy, policy, sensitivity: null);

        Assert.Equal(ShellAuditVerdict.Passed, verdict);
    }

    [Fact]
    public async Task MeshSensitivitySmoke_ThreeLevels_NotBlocked_WhenExecutableAvailable()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        IReadOnlyList<ShellSensitivityCase> cases =
        [
            new ShellSensitivityCase(ShellSensitivityLevel.Coarse, BuildSquarePatch(1, "prebuilt:1x1"), "prebuilt:1x1"),
            new ShellSensitivityCase(ShellSensitivityLevel.Medium, BuildSquarePatch(2, "prebuilt:2x2"), "prebuilt:2x2"),
            new ShellSensitivityCase(ShellSensitivityLevel.Fine, BuildSquarePatch(4, "prebuilt:4x4"), "prebuilt:4x4")
        ];

        var sensitivity = new ShellSensitivityRunner(new FixedCaseFactory(cases), Runner());
        ShellMeshSensitivityReport report = await sensitivity.RunAsync(
            new ShellAuditPolicy { SensitivityRelativeTolerance = 0.1 }, executable, CancellationToken.None);

        Assert.Equal(3, report.Cases.Count);
        Assert.All(report.Cases, sensitivityCase =>
            Assert.Equal(ShellAnalysisOutcome.Completed, sensitivityCase.Outcome));
        Assert.NotEqual(ShellAuditVerdict.Blocked, report.Verdict);
    }

    private static ShellAnalysisRunner Runner() => new(
        new ShellTclGenerator(),
        new OpenSeesArtifactStore(Path.Combine(Path.GetTempPath(), "opencs-audit-artifacts")),
        new OpenSeesProcessRunner(),
        new ShellResultParser(),
        TimeSpan.FromSeconds(60));

    private static ShellEquilibriumStepReport EquilibriumOf(
        ShellOpenSeesModel model, ShellResult result)
    {
        RCShellStepResult last = result.Steps.Last(step => step.Converged);
        ShellResultant applied = ShellEquilibriumAuditor.AppliedResultantAtStep(
            model.Stages, last.StageIndex, last.LoadFactor,
            model.Nodes.ToDictionary(node => node.Tag));
        ShellResultant reaction = ShellEquilibriumAuditor.ReactionResultant(
            last.Reactions, model.Nodes.ToDictionary(node => node.Tag));
        return ShellEquilibriumAuditor.Evaluate(
            last.StepIndex, last.StageIndex, last.LoadFactor,
            applied, reaction, new ShellAuditPolicy
            {
                AbsoluteEquilibriumTolerance = 1e-2,
                RelativeEquilibriumTolerance = 1e-2
            });
    }

    private static ShellStateCatalog V2Catalog() => new(2, [], [], []);

    private static ShellOpenSeesModel BuildSquarePatch(int subdivisions, string fingerprint)
    {
        int grid = subdivisions + 1;
        var nodes = new List<NormalizedShellNode>(grid * grid);
        for (int i = 0; i < grid; i++)
        {
            for (int j = 0; j < grid; j++)
            {
                bool fixedNode = i == 0;
                int tag = i * grid + j + 1;
                nodes.Add(new NormalizedShellNode(tag,
                    (double)j / subdivisions, (double)i / subdivisions, 0,
                    fixedNode ? [true, true, true, true, true, true] : new bool[6],
                    $"smoke:{fingerprint}:n:{tag}"));
            }
        }

        var elements = new List<NormalizedShellElement>(subdivisions * subdivisions);
        for (int i = 0; i < subdivisions; i++)
        {
            for (int j = 0; j < subdivisions; j++)
            {
                int a = i * grid + j + 1;
                int b = a + grid;
                int c = b + 1;
                int d = a + 1;
                int tag = i * subdivisions + j + 1;
                elements.Add(new NormalizedShellElement(tag, ShellElementKind.ASDShellQ4, [a, b, c, d],
                    20, fingerprint, ShellFrame.Identity, ShellIntegrationPolicy.Full,
                    $"smoke:{fingerprint}:e:{tag}"));
            }
        }

        int firstTop = subdivisions * grid + 1;
        var loads = new List<ShellNodalLoad>(grid);
        for (int j = 0; j < grid; j++)
            loads.Add(new ShellNodalLoad(firstTop + j, 0, 0, -1000.0 / grid, 0, 0, 0));

        return new ShellOpenSeesModel
        {
            Nodes = nodes,
            Materials = [new(1, "smoke:concrete", new ElasticIsotropicShellMaterialSpec(30e9, 0.2))],
            Sections = [new(20, "smoke:plate", 0.2, ShellFrame.Identity,
                [
                    new(0, ShellLayerKind.Concrete, -0.075, 0.05, 1, 0, "smoke:c0"),
                    new(1, ShellLayerKind.Concrete, -0.025, 0.05, 1, 0, "smoke:c1"),
                    new(2, ShellLayerKind.Concrete, 0.025, 0.05, 1, 0, "smoke:c2"),
                    new(3, ShellLayerKind.Concrete, 0.075, 0.05, 1, 0, "smoke:c3")
                ],
                ShellMappingMode.Exact, [], fingerprint)],
            Elements = elements,
            Stages = [new() { Tag = "stage-1", Loads = loads }]
        };
    }

    private sealed class FixedCaseFactory : IShellSensitivityCaseFactory
    {
        private readonly IReadOnlyList<ShellSensitivityCase> _cases;

        public FixedCaseFactory(IReadOnlyList<ShellSensitivityCase> cases) => _cases = cases;

        public IReadOnlyList<ShellSensitivityCase> Create(IReadOnlyList<ShellSensitivityLevel> levels) => _cases;
    }

    private sealed class RebarCapableResolver : IPlateSectionShellMaterialResolver
    {
        public IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId) =>
            [new(1, $"concrete:{sourceMaterialId}", new ElasticIsotropicShellMaterialSpec(30e9, 0.2))];

        public IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId) =>
        [
            new(500, $"rebar:{sourceMaterialId}:uniaxial", new ElasticUniaxialShellMaterialSpec(200e9)),
            new(2, $"rebar:{sourceMaterialId}:plate", new PlateRebarShellMaterialSpec(500, 0))
        ];
    }
}
