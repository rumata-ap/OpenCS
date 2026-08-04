using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.Planar.Fragments;
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.CScore.Fragments;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tests.Fixtures;
using ShellResult = OpenCS.OpenSees.Structural.ShellResult;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public sealed class MultiStoryColumnRunnerTests
{
    [Fact]
    public async Task BuildLevelSnapshotsAsync_BuildsOneSnapshotPerLevelWithEmbeddedPointConstraint()
    {
        var fragment = ValidFragment();
        var mesher = new RecordingMesher();

        var built = await new MultiStoryColumnRunner().BuildLevelSnapshotsAsync(
            fragment, mesher, level => new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            CancellationToken.None);

        Assert.Empty(built.Diagnostics);
        Assert.Equal(fragment.Levels.Count, built.Levels.Count);
        Assert.Equal(fragment.Levels.Count, mesher.Requests.Count);
        Assert.All(mesher.Requests, request =>
        {
            var point = Assert.Single(request.ConstraintObjects!);
            Assert.Equal(PlanarConstraintGeometryKind.Point, point.Geometry.Kind);
        });
    }

    [Fact]
    public async Task BuildLevelSnapshotsAsync_ReturnsDiagnosticsForNonCalculableSnapshot()
    {
        var fragment = ValidFragment();
        var mesher = new RecordingMesher(isCalculable: false);

        var built = await new MultiStoryColumnRunner().BuildLevelSnapshotsAsync(
            fragment, mesher, level => new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            CancellationToken.None);

        Assert.NotEmpty(built.Diagnostics);
    }

    [Fact]
    public async Task BuildModelAsync_AssemblesModelWithStagesAndPolicyFromFirstStage()
    {
        var fragment = ValidFragment();
        var mesher = new RecordingMesher();

        var built = await new MultiStoryColumnRunner().BuildModelAsync(
            fragment, mesher, level => new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            LookupMaterial, CalcType.C, CancellationToken.None);

        Assert.Empty(built.MeshDiagnostics);
        Assert.Empty(built.AssemblyDiagnostics);
        Assert.NotNull(built.Model);
        Assert.Single(built.Model!.Stages);
        Assert.True(built.Model.Policy.Algorithm is "Newton" or "NewtonLineSearch");
    }

    [Fact]
    public async Task BuildModelAsync_MapsPlanarPointLoadToShellNodalLoadOnAnchorNode()
    {
        // RecordingMesher.BuildAsync даёт anchor-узел с Index=5 (U=2, V=2) — совпадает с
        // ColumnAnchorLocalXY уровня (см. MakeLevel), поэтому PlanarLoad.Point с
        // PointU=2/PointV=2 резолвится в этот же узел без ambiguity.
        var fragment = ValidFragment();
        fragment.Levels[^1].Loads.Add(new PlanarLoad
        {
            Tag = "top-axial",
            Kind = PlanarLoadKind.Point,
            CoordinateSystem = PlanarLoadCoordinateSystem.Global,
            Components = new PlanarVector3(0, 0, -1000),
            PointU = 2, PointV = 2
        });
        var mesher = new RecordingMesher();

        var built = await new MultiStoryColumnRunner().BuildModelAsync(
            fragment, mesher, level => new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            LookupMaterial, CalcType.C, CancellationToken.None);

        Assert.Empty(built.AssemblyDiagnostics);
        Assert.NotNull(built.Model);
        var stageLoads = built.Model!.Stages[0].Loads;
        int topAnchorTag = built.NodeIndexToTagByLevel[fragment.Levels[^1].Id][5];
        Assert.Contains(stageLoads, load => load.NodeTag == topAnchorTag && load.Fz == -1000);
    }

    [Fact]
    public async Task BuildModelAsync_AppliesBoundaryTemplateActionsPerLevel()
    {
        var fragment = ValidFragment();
        fragment.Levels[0].Boundaries.Add(new FloorJunctionBoundary
        {
            Id = "level-1-fix",
            RegionId = fragment.Levels[0].PlateRegion.Id,
            Cut = new PlanarCutInterface
            {
                Id = "level-1-fix",
                Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve,
                    [new PlanarPoint2D(0, 0), new PlanarPoint2D(0, 4)]),
                NormalFromFragmentToOmittedSide = new PlanarVector3(-1, 0, 0),
                // MeshConstraintId (не BoundaryKey): RecordingMesher — fake mesher без реального
                // Gmsh, поэтому у fake-снапшота нет BoundaryMappings, только request-local
                // ConstraintMappings — тот же mapping-путь, что PlanarCutInterfaceMeshMapper.Map
                // использует для constraint-объектов срезов 5/5.1/6 (см. RecordingMesher выше:
                // ConstraintObjectId "level-1-fix" -> OrderedCurveEdges [(0,3)]).
                MeshConstraintId = "level-1-fix",
                ModeByDof = PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.PreserveSupport)
            }
        });
        fragment.BoundaryTemplates["level-1-fix"] = new PlanarBoundaryActionSet
            { SourceMode = PlanarBoundaryActionSourceMode.Template };
        var mesher = new RecordingMesher();

        var built = await new MultiStoryColumnRunner().BuildModelAsync(
            fragment, mesher, level => new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            LookupMaterial, CalcType.C, CancellationToken.None);

        Assert.Empty(built.AssemblyDiagnostics);
        Assert.NotNull(built.Model);
    }

    [Fact]
    public async Task BuildModelAsync_ReportsMissingBoundaryTemplateAsDiagnostic()
    {
        var fragment = ValidFragment();
        fragment.Levels[0].Boundaries.Add(new FloorJunctionBoundary
        {
            Id = "level-1-fix",
            RegionId = fragment.Levels[0].PlateRegion.Id,
            Cut = new PlanarCutInterface { Id = "level-1-fix" }
        });
        // Намеренно НЕ добавляем template в fragment.BoundaryTemplates.
        var mesher = new RecordingMesher();

        var built = await new MultiStoryColumnRunner().BuildModelAsync(
            fragment, mesher, level => new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            LookupMaterial, CalcType.C, CancellationToken.None);

        Assert.Null(built.Model);
        Assert.NotEmpty(built.AssemblyDiagnostics);
    }

    [Fact]
    public async Task RunAsync_PopulatesFiberAndLayerStatesAndForceBalanceOnConvergence()
    {
        var fragment = ValidFragment();
        var mesher = new RecordingMesher();
        var fakeRunner = new FakeShellAnalysisRunner(ShellAnalysisOutcome.Completed,
            steps: [new RCShellStepResult(0, 0, 1.0, true, [], [], [], [], [])]);

        var result = await new MultiStoryColumnRunner(fakeRunner).RunAsync(
            fragment, mesher, level => new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            LookupMaterial, CalcType.C, "opensees.exe", CancellationToken.None);

        Assert.True(result.IsConverged);
        Assert.NotNull(result.ForceBalance);
        // StateCatalog в этом fake-прогоне не задан -> экстремумы остаются нулевыми, но поля
        // должны существовать и не бросать при доступе (регресс на отсутствие NRE).
        Assert.Equal(0, result.MaxConcreteCompressionStrain);
        Assert.Equal(0, result.MaxRebarTensileStrain);
        Assert.Equal(FragmentAuditVerdict.Valid, result.AuditReport.Verdict);
    }

    [Fact]
    public async Task RunAsync_ReturnsAuditedResultOnMeshFailure()
    {
        var fragment = ValidFragment();
        var mesher = new RecordingMesher(isCalculable: false);

        var result = await new MultiStoryColumnRunner().RunAsync(
            fragment, mesher, level => new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            LookupMaterial, CalcType.C, "opensees.exe", CancellationToken.None);

        Assert.False(result.IsConverged);
        Assert.NotEmpty(result.MeshDiagnostics);
        Assert.Equal(FragmentAuditVerdict.Invalid, result.AuditReport.Verdict);
    }

    [Fact]
    public async Task RunAsync_ReturnsIncompleteWhenLastConvergedStepIsNotFullLoadOnLastStage()
    {
        var fragment = ValidFragment();
        var mesher = new RecordingMesher();
        var fakeRunner = new FakeShellAnalysisRunner(ShellAnalysisOutcome.Completed,
            steps: [new RCShellStepResult(0, 0, 0.5, true, [], [], [], [], [])]);

        var result = await new MultiStoryColumnRunner(fakeRunner).RunAsync(
            fragment, mesher, level => new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            LookupMaterial, CalcType.C, "opensees.exe", CancellationToken.None);

        Assert.False(result.IsConverged);
        Assert.NotEmpty(result.AnalysisDiagnostics);
        Assert.Equal(FragmentAuditVerdict.Invalid, result.AuditReport.Verdict);
    }

    [Fact]
    public async Task RunAsync_ReturnsConvergedWhenLastStepIsFullLoadOnLastStage()
    {
        var fragment = ValidFragment();
        var mesher = new RecordingMesher();
        var fakeRunner = new FakeShellAnalysisRunner(ShellAnalysisOutcome.Completed,
            steps: [new RCShellStepResult(0, 0, 1.0, true, [], [], [], [], [])]);

        var result = await new MultiStoryColumnRunner(fakeRunner).RunAsync(
            fragment, mesher, level => new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed),
            LookupMaterial, CalcType.C, "opensees.exe", CancellationToken.None);

        Assert.True(result.IsConverged);
    }

    sealed class FakeShellAnalysisRunner : IShellAnalysisRunner
    {
        readonly ShellAnalysisOutcome _outcome;
        readonly IReadOnlyList<RCShellStepResult> _steps;

        public FakeShellAnalysisRunner(ShellAnalysisOutcome outcome, IReadOnlyList<RCShellStepResult> steps)
        {
            _outcome = outcome;
            _steps = steps;
        }

        public Task<ShellAnalysisRunResult> RunAsync(
            ShellOpenSeesModel model, string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult(new ShellAnalysisRunResult(
                _outcome,
                _outcome == ShellAnalysisOutcome.Completed
                    ? new ShellResult { Steps = _steps, Status = "completed" }
                    : null,
                "artifacts", null));
    }

    internal static MultiStoryColumnFragment ValidFragment()
    {
        var level1 = MakeLevel("level-1", 1, 0);
        var level2 = MakeLevel("level-2", 2, 3);
        var (section, _, _) = CrossSectionFixtures.RectangularSection();
        return new MultiStoryColumnFragment
        {
            FragmentId = 1,
            Levels = { level1, level2 },
            Segments = { new ColumnSegment { Id = "seg-1", Section = section, GJ = 1000 } },
            BaseSupport = ColumnBaseFixity.Fixed,
            StageConfig = FragmentStageConfig.CreateDefault1Stage()
        };
    }

    static ColumnFloorLevel MakeLevel(string id, int regionId, double originZ)
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] },
            frame: new Frame3D(
                new PlanarVector3(0, 0, originZ), new PlanarVector3(1, 0, 0),
                new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1)));
        region.Id = regionId;
        return new ColumnFloorLevel
        {
            Id = id,
            PlateRegion = region,
            PlateSection = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 },
            ColumnAnchorLocalXY = (2, 2)
        };
    }

    // id 1/2 — shell-материалы плиты (ConcreteMaterialId/RebarMaterialId в MakeLevel);
    // id 10/20 — балочные материалы сегмента (CrossSectionFixtures.RectangularSection) —
    // раздельные пространства id, оба нужны одному lookupMaterial.
    static (Material Concrete, Material Rebar) ShellMaterials() =>
    (
        new Material { Id = 1, Tag = "B25", Type = MatType.Concrete,
            C = new MaterialChars { E = 30_000_000, Fc = -17_000, Ft = 1_150, Ec0 = -0.002, Ec2 = -0.0035 } },
        new Material { Id = 2, Tag = "A400", Type = MatType.ReSteelF,
            C = new MaterialChars { E = 200_000_000, Ft = 355_000, Ru = 500_000, Et2 = 0.05 } }
    );

    static Material? LookupMaterial(int id)
    {
        if (id == 1) return ShellMaterials().Concrete;
        if (id == 2) return ShellMaterials().Rebar;
        var (_, concrete, steel) = CrossSectionFixtures.RectangularSection();
        return CrossSectionFixtures.Materials(concrete, steel).GetValueOrDefault(id);
    }

    sealed class RecordingMesher : IPlanarMesher
    {
        readonly bool _isCalculable;
        public List<PlanarMeshingRequest> Requests { get; } = new();

        public RecordingMesher(bool isCalculable = true) => _isCalculable = isCalculable;

        public Task<PlanarMeshSnapshot> BuildAsync(PlanarMeshingRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            // Узел должен совпадать с глобальным Z уровня (frame.Origin.Z), иначе
            // PlanarLoadMapper.MapPoint (frame.Origin + LocalX*U + LocalY*V) не найдёт anchor-узел
            // ни для одного уровня, кроме originZ=0.
            double originZ = request.Region.Frame.Origin.Z;
            return Task.FromResult(new PlanarMeshSnapshot
            {
                Id = Requests.Count,
                RegionId = request.Region.Id,
                InputFingerprint = $"snapshot-{Requests.Count}",
                IsCalculable = _isCalculable,
                Diagnostics = _isCalculable
                    ? []
                    : [new FemValidationDiagnostic("planar_connection_snapshot_not_calculable", "Снапшот не расчётен.")],
                // Полный квадратный контур (0,0)-(4,0)-(4,4)-(0,4) с двумя Quadrangle4 через
                // среднюю точку (2,1)/(2,2) — тот же fixture pattern, что и
                // MultiStoryColumnShellModelAssemblerTests.LevelSnapshot, нужен для реальной
                // сборки shell-модели в Task 11 (адаптеру нужен непустой список центроидов).
                // Индексы 0/3 — левое ребро контура (0,0)-(0,4), используется тестом boundary
                // pipeline (Task 12) через request-local constraint "level-1-fix"; индекс 5 —
                // anchor-узел (2,2), совпадает с ColumnAnchorLocalXY во всех фикстурах этого файла.
                Nodes =
                [
                    new(0, 0, 0, 0, 0, originZ), new(1, 4, 0, 4, 0, originZ),
                    new(2, 4, 4, 4, 4, originZ), new(3, 0, 4, 0, 4, originZ),
                    new(4, 2, 1, 2, 1, originZ), new(5, 2, 2, 2, 2, originZ)
                ],
                Elements =
                [
                    new(0, PlanarMeshElementKind.Quadrangle4, [0, 4, 5, 3]),
                    new(1, PlanarMeshElementKind.Quadrangle4, [4, 1, 2, 5])
                ],
                ConstraintMappings = _isCalculable
                    ?
                    [
                        new PlanarConstraintMeshMapping { ConstraintObjectId = "anchor", PointNodeIndices = [5] },
                        new PlanarConstraintMeshMapping
                        {
                            ConstraintObjectId = "level-1-fix",
                            OrderedCurveEdges = [new PlanarMeshEdge(0, 3)]
                        }
                    ]
                    : []
            });
        }
    }
}
