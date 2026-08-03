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
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using ShellResult = OpenCS.OpenSees.Structural.ShellResult;

namespace OpenCS.OpenSees.CScore.Fragments
{
    /// <summary>
    /// Оркестратор нелинейного расчёта floor junction (плита + стена) в OpenSees:
    /// доменная валидация -> двухсторонний Gmsh workflow -> shell model assembly ->
    /// stages + template boundary actions -> нелинейный прогон -> последний шаг полной нагрузки
    /// -> continuity/force-balance проверки -> аудит.
    /// </summary>
    public class FloorJunctionRunner
    {
        private readonly IShellAnalysisRunner? _analysisRunner;

        /// <summary>Создаёт runner. При analysisRunner == null используется реальный
        /// ShellAnalysisRunner (production default).</summary>
        public FloorJunctionRunner(IShellAnalysisRunner? analysisRunner = null)
        {
            _analysisRunner = analysisRunner;
        }

        public async Task<FloorJunctionResult> RunAsync(
            FloorJunctionFragment fragment,
            IPlanarMesher mesher,
            PlanarMeshSettings plateMeshSettings,
            PlanarMeshSettings wallMeshSettings,
            Func<int, Material?> lookupMaterial,
            CalcType calcType,
            string openSeesExecutablePath,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(fragment);
            ArgumentNullException.ThrowIfNull(mesher);
            ArgumentNullException.ThrowIfNull(lookupMaterial);

            var result = new FloorJunctionResult { FragmentId = fragment.FragmentId };

            // 1. Доменная валидация.
            var domainDiagnostics = fragment.Validate();
            if (domainDiagnostics.Any(diagnostic => diagnostic.IsError))
            {
                result.AssemblyDiagnostics = domainDiagnostics
                    .Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}").ToList();
                result.AuditReport = new FloorJunctionAuditReport().Audit(fragment, result);
                return result;
            }

            // 2. Двухсторонний Gmsh workflow (request-local connection + cut constraints).
            var plateCuts = fragment.Boundaries
                .Where(boundary => boundary.RegionId == fragment.PlateRegion.Id).ToList();
            var wallCuts = fragment.Boundaries
                .Where(boundary => boundary.RegionId == fragment.WallRegion.Id).ToList();
            var additionalConstraintsA = plateCuts
                .Where(boundary => boundary.Cut.BoundaryKey is null)
                .Select(boundary => boundary.Cut.CreateMeshConstraint()).ToList();
            var additionalConstraintsB = wallCuts
                .Where(boundary => boundary.Cut.BoundaryKey is null)
                .Select(boundary => boundary.Cut.CreateMeshConstraint()).ToList();

            var workflow = new OpenCS.Gmsh.PlanarConnectionMeshingWorkflow(mesher);
            var meshing = await workflow.BuildAsync(
                fragment.Connection,
                fragment.PlateRegion, plateMeshSettings,
                fragment.WallRegion, wallMeshSettings,
                additionalConstraintsA,
                additionalConstraintsB,
                sourceFingerprintA: fragment.PlateRegion.GeometryFingerprint,
                sourceFingerprintB: fragment.WallRegion.GeometryFingerprint,
                cancellationToken);
            if (meshing.Diagnostics.Any(diagnostic => diagnostic.IsError))
            {
                result.MeshDiagnostics = meshing.Diagnostics
                    .Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}").ToList();
                result.AuditReport = new FloorJunctionAuditReport().Audit(fragment, result);
                return result;
            }
            PlanarMeshSnapshot plateSnapshot = meshing.SideA!;
            PlanarMeshSnapshot wallSnapshot = meshing.SideB!;
            PlanarConnectionMeshMapping mapping = meshing.Mapping!;
            var currentMappingDiagnostics = PlanarConnectionMapper.ValidateCurrent(
                fragment.Connection,
                fragment.PlateRegion,
                mapping,
                plateSnapshot,
                fragment.WallRegion,
                wallSnapshot);
            if (currentMappingDiagnostics.Any(diagnostic => diagnostic.IsError))
            {
                result.MeshDiagnostics = currentMappingDiagnostics
                    .Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}").ToList();
                result.AuditReport = new FloorJunctionAuditReport().Audit(fragment, result);
                return result;
            }
            // Provenance snapshot-а (Gmsh/Generator версии) доступен через plateSnapshot.Provenance;
            // каталога артефактов в контракте snapshot нет (API difference от раннего плана).

            // 3. Assembly двух shell-моделей в одну.
            var resolver = new PlateSectionShellMaterialResolver(
                lookupMaterial, calcType, SteelModelKind.Steel02, null);
            FloorJunctionShellAssemblyResult assembled = FloorJunctionShellModelAssembler.Assemble(
                plateSnapshot, fragment.PlateRegion, fragment.PlateSection,
                wallSnapshot, fragment.WallRegion, fragment.WallSection,
                resolver, mapping);
            if (!assembled.IsCalculable)
            {
                result.AssemblyDiagnostics = assembled.Diagnostics
                    .Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}").ToList();
                result.AuditReport = new FloorJunctionAuditReport().Audit(fragment, result);
                return result;
            }
            var model = assembled.Model;
            foreach ((int tag, string provenance) in assembled.SectionProvenance)
                result.ProvenanceMap[tag] = $"section|{provenance}";
            foreach ((int tag, string provenance) in assembled.MaterialProvenance)
                result.ProvenanceMap[tag] = $"material|{provenance}";
            result.PlateNodeIndexToTag = assembled.PlateNodeIndexToTag
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            result.WallNodeIndexToTag = assembled.WallNodeIndexToTag
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            result.JunctionPairs = assembled.JunctionPairs.ToList();

            // 4. Stages + единая policy (из первой стадии), как VerticalPlanarFragmentRunner.
            var stages = new List<ShellNonlinearStage>();
            for (int index = 0; index < fragment.StageConfig.Stages.Count; index++)
                stages.Add(new ShellNonlinearStage
                {
                    Tag = $"stage-{index + 1}",
                    LoadFactorStep = fragment.StageConfig.Stages[index].Solver.InitialStep,
                    MaxLoadFactor = 1.0
                });
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

            // 5. Boundary pipeline по всем стадиям.
            var boundaryDiagnostics = new List<string>();
            double appliedTotal = 0, mappedDeltaTotal = 0;
            foreach (var boundary in fragment.Boundaries)
            {
                PlanarMeshSnapshot snapshot = boundary.RegionId == fragment.PlateRegion.Id
                    ? plateSnapshot : wallSnapshot;
                var componentNodeMap = boundary.RegionId == fragment.PlateRegion.Id
                    ? assembled.PlateNodeIndexToTag : assembled.WallNodeIndexToTag;
                if (snapshot.RegionId != boundary.RegionId)
                {
                    boundaryDiagnostics.Add(
                        $"floor_junction_boundary_mapping_missing: boundary '{boundary.Id}' " +
                        $"регион {boundary.RegionId} не найден среди snapshots.");
                    continue;
                }
                var cutMapped = PlanarCutInterfaceMeshMapper.Map(boundary.Cut, snapshot);
                if (!cutMapped.IsCalculable || cutMapped.Mapping is null)
                {
                    boundaryDiagnostics.AddRange(cutMapped.Diagnostics.Select(
                        d => $"{boundary.Id}: {d.Code}: {d.Message}"));
                    continue;
                }
                for (int index = 0; index < fragment.StageConfig.Stages.Count; index++)
                {
                    var stage = fragment.StageConfig.Stages[index];
                    if (!fragment.BoundaryTemplates.TryGetValue(boundary.Id, out var template))
                    {
                        boundaryDiagnostics.Add(
                            $"floor_junction_boundary_template_missing: stage {index + 1}, " +
                            $"boundary '{boundary.Id}' не задан template.");
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
                        boundaryDiagnostics.AddRange(resolved.Diagnostics.Select(
                            d => $"stage {index + 1}, {boundary.Id}: {d.Code}: {d.Message}"));
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
                        boundaryDiagnostics.AddRange(mapped.Diagnostics.Select(
                            d => $"stage {index + 1}, {boundary.Id}: {d.Code}: {d.Message}"));
                        continue;
                    }
                    var applied = PlanarBoundaryActionOpenSeesAdapter.Apply(
                        model, mapped, componentNodeMap, stageIndex: index);
                    if (applied.Model is null)
                    {
                        boundaryDiagnostics.AddRange(applied.Diagnostics.Select(
                            d => $"stage {index + 1}, {boundary.Id}: {d.Code}: {d.Message}"));
                        continue;
                    }
                    model = applied.Model;
                    appliedTotal += mapped.AppliedForceGlobal.Length;
                    mappedDeltaTotal += (mapped.MappedForceGlobal - mapped.AppliedForceGlobal).Length;
                }
            }
            if (boundaryDiagnostics.Count > 0)
            {
                result.BoundaryDiagnostics = boundaryDiagnostics;
                result.AuditReport = new FloorJunctionAuditReport().Audit(fragment, result);
                return result;
            }
            result.BoundaryForceUnbalanceRatio = mappedDeltaTotal / Math.Max(1.0, appliedTotal);

            // 6. Validate финальной модели после stages/actions (throw -> diagnostics).
            try { model.Validate(); }
            catch (InvalidOperationException ex)
            {
                result.AssemblyDiagnostics = [$"floor_junction_model_validation_failed: {ex.Message}"];
                result.AuditReport = new FloorJunctionAuditReport().Audit(fragment, result);
                return result;
            }

            // 7. ShellAnalysisRunner (инъецируемый; default — concrete ShellAnalysisRunner).
            IShellAnalysisRunner runner = _analysisRunner ?? new ShellAnalysisRunner(
                new ShellTclGenerator(),
                new OpenSeesArtifactStore(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opencs-floor-junction-artifacts")),
                new OpenSeesProcessRunner(),
                new ShellResultParser(),
                TimeSpan.FromSeconds(120));
            ShellAnalysisRunResult run = await runner.RunAsync(
                model, openSeesExecutablePath, cancellationToken);
            if (run.Outcome != ShellAnalysisOutcome.Completed)
            {
                result.IsConverged = false;
                result.AnalysisDiagnostics = [run.ErrorMessage ?? $"OpenSees outcome: {run.Outcome}."];
                result.AuditReport = new FloorJunctionAuditReport().Audit(fragment, result);
                return result;
            }
            result.OpenSeesArtifactDirectory = run.ArtifactDirectory;
            ShellResult shellResult = run.Result!;

            // 8. Последний сошедшийся шаг полной нагрузки (последняя стадия + LoadFactor == 1.0).
            //    Отсутствие сошедшихся шагов — блокирующая диагностика, не исключение.
            RCShellStepResult? lastStep = shellResult.Steps.LastOrDefault(step => step.Converged);
            int lastStageIndex = fragment.StageConfig.Stages.Count - 1;
            if (lastStep is null)
            {
                result.IsConverged = false;
                result.AnalysisDiagnostics =
                    ["floor_junction_analysis_incomplete: ни один шаг нелинейного расчёта не сошёлся."];
                result.AuditReport = new FloorJunctionAuditReport().Audit(fragment, result);
                return result;
            }
            result.IsConverged = lastStep.StageIndex == lastStageIndex &&
                Math.Abs(lastStep.LoadFactor - 1.0) < 1e-6;
            if (!result.IsConverged)
            {
                result.AnalysisDiagnostics =
                [
                    $"floor_junction_analysis_incomplete: последний сошедшийся шаг — стадия " +
                    $"{lastStep.StageIndex + 1} из {fragment.StageConfig.Stages.Count}, " +
                    $"LoadFactor={lastStep.LoadFactor:F3}."
                ];
                result.AuditReport = new FloorJunctionAuditReport().Audit(fragment, result);
                return result;
            }

            // 9. Result checks: interface continuity (1e-6 м / 1e-6 рад) и force balance (relative 1e-3).
            const double continuityToleranceM = 1e-6;
            const double continuityToleranceRad = 1e-6;
            var continuityItems = new List<InterfaceContinuityItem>();
            var continuityDiagnostics = new List<string>();
            foreach ((int plateNodeTag, int wallNodeTag) in assembled.JunctionPairs)
            {
                ShellNodeDisplacement? plateDisp = lastStep.Displacements
                    .FirstOrDefault(item => item.NodeTag == plateNodeTag);
                ShellNodeDisplacement? wallDisp = lastStep.Displacements
                    .FirstOrDefault(item => item.NodeTag == wallNodeTag);
                // Отсутствующая запись перемещений — блокирующая диагностика расчёта,
                // а не необработанное First()-исключение.
                if (plateDisp is null || wallDisp is null)
                {
                    continuityDiagnostics.Add(
                        $"floor_junction_analysis_incomplete: отсутствуют перемещения узлов " +
                        $"junction-пары {plateNodeTag}->{wallNodeTag}.");
                    continue;
                }
                double dU = Math.Max(Math.Abs(plateDisp.Ux - wallDisp.Ux),
                    Math.Max(Math.Abs(plateDisp.Uy - wallDisp.Uy), Math.Abs(plateDisp.Uz - wallDisp.Uz)));
                double dR = Math.Max(Math.Abs(plateDisp.Rx - wallDisp.Rx),
                    Math.Max(Math.Abs(plateDisp.Ry - wallDisp.Ry), Math.Abs(plateDisp.Rz - wallDisp.Rz)));
                continuityItems.Add(new InterfaceContinuityItem(plateNodeTag, wallNodeTag, dU, dR));
                if (dU > continuityToleranceM || dR > continuityToleranceRad)
                    continuityDiagnostics.Add(
                        $"floor_junction_interface_continuity_failed: пара {plateNodeTag}->{wallNodeTag}: " +
                        $"|ΔU|={dU:G6} м, |ΔR|={dR:G6} рад.");
            }
            result.InterfaceContinuity = continuityItems;
            if (continuityDiagnostics.Count > 0)
            {
                result.IsConverged = false;
                result.AnalysisDiagnostics = continuityDiagnostics;
                result.AuditReport = new FloorJunctionAuditReport().Audit(fragment, result);
                return result;
            }

            // Приложенная нагрузка финального шага — это сумма всех активных стадий:
            // завершённые стадии вносят MaxLoadFactor, текущая стадия — текущий LoadFactor
            // (та же семантика, что у ShellEquilibriumAuditor.AppliedResultantAtStep). Только
            // нагрузка последней стадии давала ложный баланс при многостадийном нагружении.
            if (lastStep.StageIndex < 0 || lastStep.StageIndex >= model.Stages.Count)
            {
                result.IsConverged = false;
                result.AnalysisDiagnostics =
                [
                    $"floor_junction_analysis_incomplete: шаг ссылается на несуществующую стадию " +
                    $"{lastStep.StageIndex + 1} из {model.Stages.Count}."
                ];
                result.AuditReport = new FloorJunctionAuditReport().Audit(fragment, result);
                return result;
            }
            var nodesByTag = model.Nodes.ToDictionary(node => node.Tag);
            ShellResultant appliedResultant = ShellEquilibriumAuditor.AppliedResultantAtStep(
                model.Stages, lastStep.StageIndex, lastStep.LoadFactor, nodesByTag);
            double loadFx = appliedResultant.Fx;
            double loadFy = appliedResultant.Fy;
            double loadFz = appliedResultant.Fz;
            double rx = lastStep.Reactions.Sum(reaction => reaction.Fx);
            double ry = lastStep.Reactions.Sum(reaction => reaction.Fy);
            double rz = lastStep.Reactions.Sum(reaction => reaction.Fz);
            double appliedMagnitude = Math.Sqrt(loadFx * loadFx + loadFy * loadFy + loadFz * loadFz);
            double reactionMagnitude = Math.Sqrt(rx * rx + ry * ry + rz * rz);
            double delta = Math.Sqrt((rx - loadFx) * (rx - loadFx) +
                                     (ry - loadFy) * (ry - loadFy) +
                                     (rz - loadFz) * (rz - loadFz));
            double unbalance = delta / Math.Max(1.0, appliedMagnitude);
            // ForceBalance записывается даже при нарушении баланса — результат для аудита и UI.
            result.ForceBalance = new FloorJunctionForceBalance(appliedMagnitude, reactionMagnitude, unbalance);
            if (unbalance > 1e-3)
            {
                result.IsConverged = false;
                result.AnalysisDiagnostics =
                [
                    $"floor_junction_force_balance_failed: невязка {unbalance * 100:F2}% превышает допуск 0.1% " +
                    $"(приложено {appliedMagnitude:G6}, реакции {reactionMagnitude:G6})."
                ];
                result.AuditReport = new FloorJunctionAuditReport().Audit(fragment, result);
                return result;
            }

            // 10. Аудит.
            result.AuditReport = new FloorJunctionAuditReport().Audit(fragment, result);
            return result;
        }
    }
}
