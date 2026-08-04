using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.Planar.Fragments;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.CScore.Fragments;
using OpenCS.OpenSees.Tests.Fixtures;
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

    sealed class RecordingMesher : IPlanarMesher
    {
        readonly bool _isCalculable;
        public List<PlanarMeshingRequest> Requests { get; } = new();

        public RecordingMesher(bool isCalculable = true) => _isCalculable = isCalculable;

        public Task<PlanarMeshSnapshot> BuildAsync(PlanarMeshingRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new PlanarMeshSnapshot
            {
                Id = Requests.Count,
                RegionId = request.Region.Id,
                InputFingerprint = $"snapshot-{Requests.Count}",
                IsCalculable = _isCalculable,
                Diagnostics = _isCalculable
                    ? []
                    : [new FemValidationDiagnostic("planar_connection_snapshot_not_calculable", "Снапшот не расчётен.")],
                // Индексы 0/1 — левое ребро контура (0,0)-(0,4), используется тестом boundary
                // pipeline (Task 12) через request-local constraint "level-1-fix"; индекс 2 —
                // anchor-узел (2,2), совпадает с ColumnAnchorLocalXY во всех фикстурах этого файла.
                Nodes = [new(0, 0, 0, 0, 0, 0), new(1, 0, 4, 0, 4, 0), new(2, 2, 2, 2, 2, 0)],
                Elements = [],
                ConstraintMappings = _isCalculable
                    ?
                    [
                        new PlanarConstraintMeshMapping { ConstraintObjectId = "anchor", PointNodeIndices = [2] },
                        new PlanarConstraintMeshMapping
                        {
                            ConstraintObjectId = "level-1-fix",
                            OrderedCurveEdges = [new PlanarMeshEdge(0, 1)]
                        }
                    ]
                    : []
            });
        }
    }
}
