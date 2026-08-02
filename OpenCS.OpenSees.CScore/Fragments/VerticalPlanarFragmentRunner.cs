using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CScore;
using CScore.Planar;
using CScore.Planar.Fragments;
using CScore.PlateRebar;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using ShellResult = OpenCS.OpenSees.Structural.ShellResult;

namespace OpenCS.OpenSees.CScore.Fragments
{
    /// <summary>
    /// Оркестратор нелинейного расчёта фрагментов вертикальных стен в OpenSees:
    /// Gmsh-сетка -> cut-interface mapping -> ShellOpenSeesModel -> boundary actions по стадиям
    /// -> нелинейный прогон -> послойные состояния -> аудит СП 63.
    /// </summary>
    public class VerticalPlanarFragmentRunner
    {
        internal sealed record ModelBuildOutcome(
            ShellOpenSeesModel? Model,
            IReadOnlyDictionary<int, int> NodeIndexToTag,
            List<string> MeshDiagnostics,
            List<string> BoundaryDiagnostics,
            double ForceUnbalanceRatio = 0);

        public async Task<VerticalPlanarFragmentResult> RunAsync(
            VerticalPlanarFragment fragment,
            IPlanarMesher mesher,
            PlanarMeshSettings meshSettings,
            Func<int, Material?> lookupMaterial,
            CalcType calcType,
            string openSeesExecutablePath,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(fragment);
            ArgumentNullException.ThrowIfNull(mesher);
            ArgumentNullException.ThrowIfNull(lookupMaterial);

            var result = new VerticalPlanarFragmentResult { FragmentId = fragment.FragmentId };

            ModelBuildOutcome built = await BuildModelAsync(
                fragment, mesher, meshSettings, lookupMaterial, calcType, cancellationToken);

            if (built.MeshDiagnostics.Count > 0)
            {
                result.MeshDiagnostics = built.MeshDiagnostics;
                result.AuditReport = new FragmentAuditReport().Audit(fragment, result);
                return result;
            }
            if (built.BoundaryDiagnostics.Count > 0)
            {
                result.BoundaryDiagnostics = built.BoundaryDiagnostics;
                result.AuditReport = new FragmentAuditReport().Audit(fragment, result);
                return result;
            }

            var runner = new ShellAnalysisRunner(
                new ShellTclGenerator(),
                new OpenSeesArtifactStore(System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "opencs-fragment-artifacts")),
                new OpenSeesProcessRunner(),
                new ShellResultParser(),
                TimeSpan.FromSeconds(120));

            ShellAnalysisRunResult run = await runner.RunAsync(
                built.Model!, openSeesExecutablePath, cancellationToken);

            bool executionCompleted = run.Outcome == ShellAnalysisOutcome.Completed;
            if (!executionCompleted)
            {
                result.IsConverged = false;
                result.BoundaryDiagnostics = [run.ErrorMessage ?? $"OpenSees outcome: {run.Outcome}."];
                result.AuditReport = new FragmentAuditReport().Audit(fragment, result);
                return result;
            }

            ShellResult shellResult = run.Result!;
            RCShellStepResult lastStep = shellResult.Steps.Last(step => step.Converged);
            int lastStageIndex = fragment.StageConfig.Stages.Count - 1;

            // ShellAnalysisRunner.DetermineOutcome считает Completed уже при ЛЮБОМ сошедшемся
            // шаге — этого недостаточно: последняя стадия должна дойти до полной нагрузки
            // (LoadFactor == 1.0), иначе "сошедшийся" результат может относиться к малой доле
            // заданного воздействия и давать ложно благополучные деформации.
            result.IsConverged = lastStep.StageIndex == lastStageIndex &&
                Math.Abs(lastStep.LoadFactor - 1.0) < 1e-6;
            if (!result.IsConverged)
            {
                result.BoundaryDiagnostics =
                [
                    $"Нелинейный расчёт не достиг полной нагрузки: последний сошедшийся шаг — " +
                    $"стадия {lastStep.StageIndex + 1} из {fragment.StageConfig.Stages.Count}, " +
                    $"LoadFactor={lastStep.LoadFactor:F3}."
                ];
                result.AuditReport = new FragmentAuditReport().Audit(fragment, result);
                return result;
            }

            double minConcreteStrain = 0;
            double maxRebarStrain = 0;
            var layerStates = new List<LayerMaterialState>();
            if (shellResult.StateCatalog is { } catalog)
            {
                var stateParser = new ShellStateParser();
                var stressGroups = catalog.ShellLayerGroups.Where(g => g.ResponseKind == "stress");
                foreach (var group in stressGroups)
                {
                    foreach (int elementTag in group.ElementTags)
                    {
                        var states = stateParser.ParseShellLayers(
                            run.ArtifactDirectory!, catalog, elementTag,
                            group.IntegrationPoint, group.LayerIndex, lastStep.StepIndex);
                        foreach (var state in states)
                        {
                            // Компоненты 0/1 — нормальные деформации ε11/ε22 (локальные оси
                            // материала слоя); нагрузка может доминировать по любой из них в
                            // зависимости от ориентации фрагмента, поэтому берём экстремум по
                            // обеим, а не только по ε11.
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
                    }
                }
            }
            result.MaxConcreteCompressionStrain = minConcreteStrain;
            result.MaxRebarTensileStrain = maxRebarStrain;
            result.LayerStates = layerStates;

            // Баланс уже посчитан по каждому cut interface на каждой стадии внутри
            // BuildModelAsync и возвращён как ModelBuildOutcome.ForceUnbalanceRatio.
            result.ForceUnbalanceRatio = built.ForceUnbalanceRatio;

            result.EnergyConfidence = ShellEnergyAuditor.DetermineConfidence(
                hasNativeEnergyResponse: false,
                hasStateIntegralData: false,
                hasLoadHistory: shellResult.Steps.Any(s => s.Converged)).ToString();

            result.AuditReport = new FragmentAuditReport().Audit(fragment, result);
            return result;
        }

        internal async Task<ModelBuildOutcome> BuildModelAsync(
            VerticalPlanarFragment fragment,
            IPlanarMesher mesher,
            PlanarMeshSettings meshSettings,
            Func<int, Material?> lookupMaterial,
            CalcType calcType,
            CancellationToken cancellationToken)
        {
            var cuts = new List<PlanarCutInterface>();
            if (fragment.BottomCut is not null) cuts.Add(fragment.BottomCut);
            if (fragment.TopCut is not null) cuts.Add(fragment.TopCut);
            cuts.AddRange(fragment.SideCuts);

            var constraintObjects = cuts
                .Where(c => c.BoundaryKey is null)
                .Select(c => c.CreateMeshConstraint())
                .ToList();

            PlanarMeshSnapshot snapshot = await mesher.BuildAsync(
                new PlanarMeshingRequest(fragment.Region, meshSettings, constraintObjects),
                cancellationToken);

            if (!snapshot.IsCalculable)
                return new(null, new Dictionary<int, int>(),
                    snapshot.Diagnostics.Select(d => d.Message).ToList(), []);

            var cutMappings = new Dictionary<string, PlanarCutInterfaceMeshMapping>();
            var earlyDiagnostics = new List<string>();
            foreach (var cut in cuts)
            {
                var mapped = PlanarCutInterfaceMeshMapper.Map(cut, snapshot);
                if (!mapped.IsCalculable || mapped.Mapping is null)
                {
                    earlyDiagnostics.AddRange(mapped.Diagnostics.Select(d => $"{cut.Id}: {d.Message}"));
                    continue;
                }
                cutMappings[cut.Id] = mapped.Mapping;
            }
            if (earlyDiagnostics.Count > 0)
                return new(null, new Dictionary<int, int>(), [], earlyDiagnostics);

            var field = PlateRebarField.From(fragment.Section, fragment.Region);
            var resolver = new PlateSectionShellMaterialResolver(
                lookupMaterial, calcType, SteelModelKind.Steel02, null);
            PlanarMeshShellModelResult modelBuilt = PlanarMeshSnapshotShellModelAdapter.Build(
                snapshot, fragment.Region.Frame, fragment.Section, field, resolver);

            var model = modelBuilt.Model;
            var stages = new List<ShellNonlinearStage>();
            for (int i = 0; i < fragment.StageConfig.Stages.Count; i++)
                stages.Add(new ShellNonlinearStage
                {
                    Tag = $"stage-{i + 1}",
                    LoadFactorStep = fragment.StageConfig.Stages[i].Solver.InitialStep,
                    MaxLoadFactor = 1.0
                });

            // ShellOpenSeesModel.Policy — единая на модель настройка Newton-анализа (нет
            // per-stage policy в контракте), поэтому берём SolverParameters первой стадии.
            // Damage/softening бетонные материалы (PlasticDamageConcretePlaneStress) заметно
            // хуже сходятся на дефолтном plain Newton, чем на line search — для этого и
            // существует FragmentStage.Solver.Algorithm="NewtonWithLineSearch" по умолчанию.
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
            for (int i = 0; i < fragment.StageConfig.Stages.Count; i++)
            {
                var stage = fragment.StageConfig.Stages[i];
                foreach (var cut in cuts)
                {
                    if (!fragment.BoundaryTemplates.TryGetValue(cut.Id, out var template))
                    {
                        stageDiagnostics.Add(
                            $"stage {i + 1}, {cut.Id}: planar_boundary_template_missing — для cut interface " +
                            $"'{cut.Id}' не задан шаблон в VerticalPlanarFragment.BoundaryTemplates.");
                        continue;
                    }
                    if (!cutMappings.TryGetValue(cut.Id, out var mapping))
                        continue;

                    var scaled = PlanarBoundaryActionSetScaling.Scale(template, stage.CutInterfaceScale);
                    var request = new PlanarBoundaryActionRequest
                    {
                        Interface = cut,
                        SourceMode = PlanarBoundaryActionSourceMode.Template,
                        TargetFrame = cut.Frame
                    };
                    var resolved = new PlanarBoundaryActionResolver().Resolve(
                        request, parentProvider: null, new PlanarBoundaryTemplateProvider(scaled));
                    if (!resolved.IsCalculable)
                    {
                        stageDiagnostics.AddRange(resolved.Diagnostics.Select(
                            d => $"stage {i + 1}, {cut.Id}: {d.Message}"));
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
                    var mapped = PlanarBoundaryActionMeshMapper.Map(cut, snapshot, actionSet, cutMappings[cut.Id]);
                    if (!mapped.IsCalculable)
                    {
                        stageDiagnostics.AddRange(mapped.Diagnostics.Select(
                            d => $"stage {i + 1}, {cut.Id}: {d.Message}"));
                        continue;
                    }

                    var applied = PlanarBoundaryActionOpenSeesAdapter.Apply(
                        model, mapped, modelBuilt.NodeIndexToTag, stageIndex: i);
                    if (applied.Model is null)
                    {
                        stageDiagnostics.AddRange(applied.Diagnostics.Select(
                            d => $"stage {i + 1}, {cut.Id}: {d.Message}"));
                        continue;
                    }
                    model = applied.Model;

                    // Баланс силы на этом cut interface на этой стадии — накапливается по
                    // модулю, чтобы невязка на одном interface не гасилась знаком другого.
                    appliedTotal += mapped.AppliedForceGlobal.Length;
                    mappedDeltaTotal += (mapped.MappedForceGlobal - mapped.AppliedForceGlobal).Length;
                }
            }

            if (stageDiagnostics.Count > 0)
                return new(null, new Dictionary<int, int>(), [], stageDiagnostics);

            double forceUnbalanceRatio = mappedDeltaTotal / Math.Max(1.0, appliedTotal);
            return new(model, modelBuilt.NodeIndexToTag, [], [], forceUnbalanceRatio);
        }
    }
}
