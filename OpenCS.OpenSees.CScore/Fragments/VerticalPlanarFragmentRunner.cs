using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CScore;
using CScore.Planar;
using CScore.Planar.Fragments;
using CScore.PlateRebar;
using OpenCS.OpenSees.Structural;

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
                return result;
            }
            if (built.BoundaryDiagnostics.Count > 0)
            {
                result.BoundaryDiagnostics = built.BoundaryDiagnostics;
                return result;
            }

            // Шаги 5-9 (реальный прогон OpenSees, послойные состояния, баланс, энергетика,
            // аудит) реализуются в Task 6.
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
            model = model with { Stages = stages };

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
