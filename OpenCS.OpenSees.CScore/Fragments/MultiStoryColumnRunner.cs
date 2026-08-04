using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CScore.Planar;
using CScore.Planar.Fragments;
using OpenCS.OpenSees.Audit;

namespace OpenCS.OpenSees.CScore.Fragments;

/// <summary>Оркестратор нелинейного расчёта многоэтажной колонны: N независимых Gmsh-снапшотов
/// перекрытий -> составная ShellOpenSeesModel (shell + нелинейные балочные сегменты) -> boundary
/// actions по уровням -> один staged нелинейный прогон OpenSees -> shell-слои + балочные фибры ->
/// аудит СП 63.</summary>
public class MultiStoryColumnRunner
{
    readonly IShellAnalysisRunner? _analysisRunner;

    public MultiStoryColumnRunner(IShellAnalysisRunner? analysisRunner = null)
    {
        _analysisRunner = analysisRunner;
    }

    internal sealed record LevelSnapshotsOutcome(
        IReadOnlyList<(ColumnFloorLevel Level, PlanarMeshSnapshot Snapshot)> Levels,
        List<string> Diagnostics,
        string? GmshArtifactDirectory = null);

    internal async Task<LevelSnapshotsOutcome> BuildLevelSnapshotsAsync(
        MultiStoryColumnFragment fragment,
        IPlanarMesher mesher,
        System.Func<ColumnFloorLevel, PlanarMeshSettings> meshSettingsFor,
        CancellationToken cancellationToken)
    {
        var levels = new List<(ColumnFloorLevel Level, PlanarMeshSnapshot Snapshot)>();
        var diagnostics = new List<string>();
        string? gmshArtifactDirectory = null;

        foreach (var level in fragment.Levels)
        {
            var anchorPoint = PlanarConstraintObject.Point(
                "anchor",
                new PlanarPoint2D(level.ColumnAnchorLocalXY.U, level.ColumnAnchorLocalXY.V),
                new PlanarStructuralFacet(PlanarStructuralKind.None),
                new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint));
            // Cut'ы без BoundaryKey (внутренние/явные кривые, а не рёбра Hull) нуждаются в
            // собственном request-local constraint'е, иначе PlanarCutInterfaceMeshMapper.Map не
            // найдёт mesh mapping и в реальном (не fake) прогоне — тот же паттерн, что уже
            // использует VerticalPlanarFragmentRunner.BuildModelAsync для BottomCut/TopCut/SideCuts.
            var boundaryConstraints = level.Boundaries
                .Where(boundary => boundary.Cut.BoundaryKey is null)
                .Select(boundary => boundary.Cut.CreateMeshConstraint())
                .ToList();
            IReadOnlyList<PlanarConstraintObject> constraints = [anchorPoint, .. boundaryConstraints];

            PlanarMeshSnapshot snapshot = await mesher.BuildAsync(
                new PlanarMeshingRequest(level.PlateRegion, meshSettingsFor(level), constraints),
                cancellationToken);

            if (!snapshot.IsCalculable)
            {
                diagnostics.AddRange(snapshot.Diagnostics.Select(d => $"{level.Id}: {d.Message}"));
                continue;
            }
            // Артефакты первого построенного уровня — единственный String-путь result'а
            // (MultiStoryColumnResult.GmshArtifactDirectory), как и у FloorJunctionRunner,
            // который сохраняет только artifact directory стороны plate.
            gmshArtifactDirectory ??= snapshot.Provenance?.ArtifactDirectory;
            levels.Add((level, snapshot));
        }

        return new LevelSnapshotsOutcome(levels, diagnostics, gmshArtifactDirectory);
    }
}
