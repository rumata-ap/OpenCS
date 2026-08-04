using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CScore;
using CScore.Planar;
using CScore.Planar.Fragments;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using ShellResult = OpenCS.OpenSees.Structural.ShellResult;

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

    internal sealed record ModelBuildOutcome(
        ShellOpenSeesModel? Model,
        IReadOnlyDictionary<string, int> AnchorNodeTagByLevel,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> NodeIndexToTagByLevel,
        List<string> MeshDiagnostics,
        List<string> AssemblyDiagnostics,
        string? GmshArtifactDirectory = null,
        double ForceUnbalanceRatio = 0);

    internal async Task<ModelBuildOutcome> BuildModelAsync(
        MultiStoryColumnFragment fragment,
        IPlanarMesher mesher,
        System.Func<ColumnFloorLevel, PlanarMeshSettings> meshSettingsFor,
        System.Func<int, Material?> lookupMaterial,
        CalcType calcType,
        CancellationToken cancellationToken)
    {
        LevelSnapshotsOutcome snapshots = await BuildLevelSnapshotsAsync(
            fragment, mesher, meshSettingsFor, cancellationToken);
        if (snapshots.Diagnostics.Count > 0)
            return new(null, new Dictionary<string, int>(),
                new Dictionary<string, IReadOnlyDictionary<int, int>>(), snapshots.Diagnostics, [],
                snapshots.GmshArtifactDirectory);

        var resolver = new PlateSectionShellMaterialResolver(
            lookupMaterial, calcType, SteelModelKind.Steel02, null);
        MultiStoryColumnShellAssemblyResult assembled = MultiStoryColumnShellModelAssembler.Assemble(
            snapshots.Levels, fragment.Segments, fragment.BaseSupport,
            fragment.GeomTransfKind, fragment.ElementFormulation,
            resolver, calcType, lookupMaterial);
        if (!assembled.IsCalculable)
            return new(null, new Dictionary<string, int>(),
                new Dictionary<string, IReadOnlyDictionary<int, int>>(), [],
                assembled.Diagnostics.Select(d => $"{d.Code}: {d.Message}").ToList(),
                snapshots.GmshArtifactDirectory);

        var model = assembled.Model;
        var levelSnapshotById = snapshots.Levels.ToDictionary(item => item.Level.Id, item => item.Snapshot);
        var loadDiagnostics = new List<string>();
        var stages = new List<ShellNonlinearStage>();
        for (int i = 0; i < fragment.StageConfig.Stages.Count; i++)
        {
            var stageConfig = fragment.StageConfig.Stages[i];
            var stageLoads = new List<ShellNodalLoad>();
            foreach (var level in fragment.Levels)
            {
                if (level.Loads.Count == 0) continue;
                // Map(Frame3D, ...) вместо Map(PlanarRegion, ...): этот срез допускает только
                // PlanarLoad.Point на anchor-узле, которому не нужен boundary contract
                // (PlanarBoundaryContractMapper) — используется overload без него, чтобы
                // отсутствие/неполнота внешних boundary ролей уровня не блокировала точечную
                // нагрузку на колонну.
                PlanarLoadMappingResult mapped = PlanarLoadMapper.Map(
                    level.PlateRegion.Frame, levelSnapshotById[level.Id], level.Loads);
                if (!mapped.IsCalculable)
                {
                    loadDiagnostics.AddRange(mapped.Diagnostics.Select(
                        d => $"stage {i + 1}, level {level.Id}: {d.Message}"));
                    continue;
                }
                foreach (var load in PlanarLoadOpenSeesAdapter.Map(mapped, assembled.NodeIndexToTagByLevel[level.Id]))
                    stageLoads.Add(load with
                    {
                        Fx = load.Fx * stageConfig.SurfaceLoadScale,
                        Fy = load.Fy * stageConfig.SurfaceLoadScale,
                        Fz = load.Fz * stageConfig.SurfaceLoadScale
                    });
            }
            stages.Add(new ShellNonlinearStage
            {
                Tag = $"stage-{i + 1}",
                Loads = stageLoads,
                LoadFactorStep = stageConfig.Solver.InitialStep,
                MaxLoadFactor = 1.0
            });
        }
        if (loadDiagnostics.Count > 0)
            return new(null, new Dictionary<string, int>(),
                new Dictionary<string, IReadOnlyDictionary<int, int>>(), [], loadDiagnostics,
                snapshots.GmshArtifactDirectory);

        SolverParameters solverParams = fragment.StageConfig.Stages.Count > 0
            ? fragment.StageConfig.Stages[0].Solver
            : new SolverParameters();
        string openSeesAlgorithm = solverParams.Algorithm switch
        {
            "Newton" => "Newton",
            _ => "NewtonLineSearch"
        };
        model = model with
        {
            Stages = stages,
            Policy = model.Policy with
            {
                Algorithm = openSeesAlgorithm,
                MaxIterations = solverParams.MaxIterations
            }
        };

        var stageDiagnostics = new List<string>();
        double appliedTotal = 0, mappedDeltaTotal = 0;
        var levelById = snapshots.Levels.ToDictionary(item => item.Level.Id, item => item.Snapshot);
        for (int stageIndex = 0; stageIndex < fragment.StageConfig.Stages.Count; stageIndex++)
        {
            var stage = fragment.StageConfig.Stages[stageIndex];
            foreach (var (level, _) in snapshots.Levels)
            {
                var snapshot = levelById[level.Id];
                var nodeMap = assembled.NodeIndexToTagByLevel[level.Id];
                foreach (var boundary in level.Boundaries)
                {
                    if (!fragment.BoundaryTemplates.TryGetValue(boundary.Id, out var template))
                    {
                        stageDiagnostics.Add(
                            $"stage {stageIndex + 1}, {boundary.Id}: multistory_column_boundary_template_missing — " +
                            $"для boundary '{boundary.Id}' не задан template.");
                        continue;
                    }

                    var cutMapped = PlanarCutInterfaceMeshMapper.Map(boundary.Cut, snapshot);
                    if (!cutMapped.IsCalculable || cutMapped.Mapping is null)
                    {
                        stageDiagnostics.AddRange(cutMapped.Diagnostics.Select(
                            d => $"stage {stageIndex + 1}, {boundary.Id}: {d.Message}"));
                        continue;
                    }

                    var scaled = PlanarBoundaryActionSetScaling.Scale(template, stage.CutInterfaceScale);
                    var request = new PlanarBoundaryActionRequest
                    {
                        Interface = boundary.Cut,
                        SourceMode = PlanarBoundaryActionSourceMode.Template,
                        TargetFrame = boundary.Cut.Frame
                    };
                    var resolved = new PlanarBoundaryActionResolver().Resolve(
                        request, parentProvider: null, new PlanarBoundaryTemplateProvider(scaled));
                    if (!resolved.IsCalculable)
                    {
                        stageDiagnostics.AddRange(resolved.Diagnostics.Select(
                            d => $"stage {stageIndex + 1}, {boundary.Id}: {d.Message}"));
                        continue;
                    }

                    var actionSet = new PlanarBoundaryActionSet
                    {
                        SourceMode = resolved.SourceMode,
                        ForceActions = resolved.ForceActions,
                        KinematicActions = resolved.KinematicActions,
                        SourceReferences = resolved.SourceReferences,
                        Diagnostics = resolved.Diagnostics
                    };
                    var mapped = PlanarBoundaryActionMeshMapper.Map(
                        boundary.Cut, snapshot, actionSet, cutMapped.Mapping);
                    if (!mapped.IsCalculable)
                    {
                        stageDiagnostics.AddRange(mapped.Diagnostics.Select(
                            d => $"stage {stageIndex + 1}, {boundary.Id}: {d.Message}"));
                        continue;
                    }

                    var applied = PlanarBoundaryActionOpenSeesAdapter.Apply(
                        model, mapped, nodeMap, stageIndex: stageIndex);
                    if (applied.Model is null)
                    {
                        stageDiagnostics.AddRange(applied.Diagnostics.Select(
                            d => $"stage {stageIndex + 1}, {boundary.Id}: {d.Message}"));
                        continue;
                    }
                    model = applied.Model;

                    appliedTotal += mapped.AppliedForceGlobal.Length;
                    mappedDeltaTotal += (mapped.MappedForceGlobal - mapped.AppliedForceGlobal).Length;
                }
            }
        }

        if (stageDiagnostics.Count > 0)
            return new(null, new Dictionary<string, int>(),
                new Dictionary<string, IReadOnlyDictionary<int, int>>(), [], stageDiagnostics,
                snapshots.GmshArtifactDirectory);

        return new(model, assembled.AnchorNodeTagByLevel, assembled.NodeIndexToTagByLevel, [], [],
            snapshots.GmshArtifactDirectory, mappedDeltaTotal / System.Math.Max(1.0, appliedTotal));
    }

    public async Task<MultiStoryColumnResult> RunAsync(
        MultiStoryColumnFragment fragment,
        IPlanarMesher mesher,
        Func<ColumnFloorLevel, PlanarMeshSettings> meshSettingsFor,
        Func<int, Material?> lookupMaterial,
        CalcType calcType,
        string openSeesExecutablePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        ArgumentNullException.ThrowIfNull(mesher);
        ArgumentNullException.ThrowIfNull(lookupMaterial);

        var result = new MultiStoryColumnResult { FragmentId = fragment.FragmentId };

        var domainDiagnostics = fragment.Validate();
        if (domainDiagnostics.Any(d => d.IsError))
        {
            result.AssemblyDiagnostics = domainDiagnostics.Select(d => $"{d.Code}: {d.Message}").ToList();
            result.AuditReport = new MultiStoryColumnAuditReport().Audit(fragment, result);
            return result;
        }

        ModelBuildOutcome built = await BuildModelAsync(
            fragment, mesher, meshSettingsFor, lookupMaterial, calcType, cancellationToken);
        result.GmshArtifactDirectory = built.GmshArtifactDirectory;

        if (built.MeshDiagnostics.Count > 0)
        {
            result.MeshDiagnostics = built.MeshDiagnostics;
            result.AuditReport = new MultiStoryColumnAuditReport().Audit(fragment, result);
            return result;
        }
        if (built.AssemblyDiagnostics.Count > 0)
        {
            result.AssemblyDiagnostics = built.AssemblyDiagnostics;
            result.AuditReport = new MultiStoryColumnAuditReport().Audit(fragment, result);
            return result;
        }

        IShellAnalysisRunner runner = _analysisRunner ?? new ShellAnalysisRunner(
            new ShellTclGenerator(),
            new OpenSeesArtifactStore(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "opencs-multistory-column-artifacts")),
            new OpenSeesProcessRunner(),
            new ShellResultParser(),
            TimeSpan.FromSeconds(180));

        ShellAnalysisRunResult run = await runner.RunAsync(
            built.Model!, openSeesExecutablePath, cancellationToken);
        result.OpenSeesArtifactDirectory = run.ArtifactDirectory;

        bool executionCompleted = run.Outcome == ShellAnalysisOutcome.Completed;
        if (!executionCompleted)
        {
            result.IsConverged = false;
            result.AnalysisDiagnostics = [run.ErrorMessage ?? $"OpenSees outcome: {run.Outcome}."];
            result.AuditReport = new MultiStoryColumnAuditReport().Audit(fragment, result);
            return result;
        }

        ShellResult shellResult = run.Result!;
        RCShellStepResult? lastStep = shellResult.Steps.LastOrDefault(step => step.Converged);
        int lastStageIndex = fragment.StageConfig.Stages.Count - 1;
        result.IsConverged = lastStep is not null &&
            lastStep.StageIndex == lastStageIndex && Math.Abs(lastStep.LoadFactor - 1.0) < 1e-6;
        if (!result.IsConverged)
        {
            result.AnalysisDiagnostics =
            [
                lastStep is null
                    ? "multistory_column_analysis_incomplete: ни один шаг не сошёлся."
                    : $"multistory_column_analysis_incomplete: последний сошедшийся шаг — стадия " +
                      $"{lastStep.StageIndex + 1} из {fragment.StageConfig.Stages.Count}, " +
                      $"LoadFactor={lastStep.LoadFactor:F3}."
            ];
            result.AuditReport = new MultiStoryColumnAuditReport().Audit(fragment, result);
            return result;
        }

        var nodesByTag = built.Model!.Nodes.ToDictionary(node => node.Tag);
        ShellResultant appliedResultant = ShellEquilibriumAuditor.AppliedResultantAtStep(
            built.Model.Stages, lastStep!.StageIndex, lastStep.LoadFactor, nodesByTag);
        double loadFx = appliedResultant.Fx, loadFy = appliedResultant.Fy, loadFz = appliedResultant.Fz;
        double rx = lastStep.Reactions.Sum(reaction => reaction.Fx);
        double ry = lastStep.Reactions.Sum(reaction => reaction.Fy);
        double rz = lastStep.Reactions.Sum(reaction => reaction.Fz);
        double appliedMagnitude = Math.Sqrt(loadFx * loadFx + loadFy * loadFy + loadFz * loadFz);
        double reactionMagnitude = Math.Sqrt(rx * rx + ry * ry + rz * rz);
        // OpenSees reaction recorder возвращает силы реакций с знаком, противоположным приложенной
        // нагрузке (силы опор на конструкцию), поэтому residual = reaction + applied, а не
        // |reactionMagnitude - appliedMagnitude| — та же конвенция, что в FloorJunctionRunner.
        double delta = Math.Sqrt((rx + loadFx) * (rx + loadFx) +
                                  (ry + loadFy) * (ry + loadFy) +
                                  (rz + loadFz) * (rz + loadFz));
        double relativeUnbalance = delta / Math.Max(1.0, appliedMagnitude);
        result.ForceBalance = new FloorJunctionForceBalance(appliedMagnitude, reactionMagnitude, relativeUnbalance);

        double minConcreteStrain = 0;
        double maxRebarStrain = 0;
        var layerStates = new List<LayerMaterialState>();
        var fiberStates = new List<ColumnFiberState>();
        if (shellResult.StateCatalog is { } catalog)
        {
            var stateParser = new ShellStateParser();

            foreach (var group in catalog.ShellLayerGroups.Where(g => g.ResponseKind == "stress"))
                foreach (int elementTag in group.ElementTags)
                    foreach (var state in stateParser.ParseShellLayers(
                        run.ArtifactDirectory!, catalog, elementTag,
                        group.IntegrationPoint, group.LayerIndex, lastStep.StepIndex))
                    {
                        double eps11 = state.Strain[0];
                        double eps22 = state.Strain[1];
                        bool isConcrete = state.ShellLayerKind == ShellLayerKind.Concrete;
                        if (isConcrete)
                            minConcreteStrain = Math.Min(minConcreteStrain, Math.Min(eps11, eps22));
                        else
                            maxRebarStrain = Math.Max(maxRebarStrain, Math.Max(eps11, eps22));
                        layerStates.Add(new LayerMaterialState
                        {
                            ElementId = elementTag,
                            LayerIndex = group.LayerIndex,
                            LayerKind = state.ShellLayerKind.ToString(),
                            Stress = state.Stress[0],
                            Strain = isConcrete ? Math.Min(eps11, eps22) : Math.Max(eps11, eps22)
                        });
                    }

            var beamSectionsByTag = built.Model.NonlinearBeamSections;
            var segmentIdByElementTag = fragment.Segments
                .Zip(built.Model.NonlinearBeamElements, (segment, element) => (segment.Id, element.Tag))
                .ToDictionary(pair => pair.Tag, pair => pair.Id);
            foreach (var location in catalog.BeamFiberLocations)
            {
                if (!segmentIdByElementTag.TryGetValue(location.ElementTag, out string? segmentId))
                    continue;
                foreach (var fiberState in stateParser.ParseBeamFibers(
                    run.ArtifactDirectory!, catalog, location.ElementTag,
                    location.IntegrationPoint, location.FiberIndex, lastStep.StepIndex))
                {
                    string sourceType = beamSectionsByTag.TryGetValue(location.SectionTag, out var section)
                        ? section.Materials.FirstOrDefault(m => m.Tag == location.MaterialTag)?.SourceType ?? "Unknown"
                        : "Unknown";
                    bool isConcrete = sourceType == "Concrete";
                    if (isConcrete)
                        minConcreteStrain = Math.Min(minConcreteStrain, fiberState.Strain);
                    else
                        maxRebarStrain = Math.Max(maxRebarStrain, fiberState.Strain);
                    fiberStates.Add(new ColumnFiberState
                    {
                        SegmentId = segmentId!,
                        ElementTag = location.ElementTag,
                        FiberIndex = location.FiberIndex,
                        Kind = sourceType,
                        Stress = fiberState.StressPa,
                        Strain = fiberState.Strain
                    });
                }
            }
        }
        result.MaxConcreteCompressionStrain = minConcreteStrain;
        result.MaxRebarTensileStrain = maxRebarStrain;
        result.LayerStates = layerStates;
        result.FiberStates = fiberStates;

        result.EnergyConfidence = ShellEnergyAuditor.DetermineConfidence(
            hasNativeEnergyResponse: false,
            hasStateIntegralData: false,
            hasLoadHistory: shellResult.Steps.Any(s => s.Converged)).ToString();

        result.AuditReport = new MultiStoryColumnAuditReport().Audit(fragment, result);
        return result;
    }
}
