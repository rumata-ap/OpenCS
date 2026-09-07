using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.PlateStrip;
using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore.Fragments
{
    /// <summary>Вход сквозной сверки полосы плиты.</summary>
    public sealed class PlateStripAnalogyRequest
    {
        public PlanarRegion Region { get; init; } = null!;
        public PlateSection Section { get; init; } = null!;
        public PlateRebarField RebarField { get; init; } = new([], []);
        public PlateStripBeamAnalogy Analogy { get; init; } = null!;
        public IReadOnlyList<PlanarLoad> Loads { get; init; } = [];
        public StripBeamSupportScheme SupportScheme { get; init; } =
            StripBeamSupportScheme.SimplySupported;
        public PlanarMeshSettings MeshSettings { get; init; } = new(0.5, 6, PlanarMeshElementMode.Quads);
        public IReadOnlyList<double> StationFractions { get; init; } = [];
        public StripNewtonOptions NewtonOptions { get; init; } = StripNewtonOptions.Default;
        /// <summary>Источник плитного отклика для каждой точки квадратуры по ширине.</summary>
        public IReadOnlyList<IPlateSectionResponse> WidthSources { get; init; } = [];
    }

    /// <summary>
    /// Оркестратор сквозной сверки полосы плиты (Срез 7): Gmsh-сетка региона → нелинейный
    /// shell-прогон OpenSees → интегрирование resultants по сечениям полосы → нелинейная
    /// beam-аналогия CScore → сверка эпюр и прогибов.
    ///
    /// Три ловушки Runner-ов, подтверждённые кодом VerticalPlanarFragmentRunner, соблюдаются
    /// явно: Algorithm задаётся в model.Policy (не наследуется), ShellAnalysisOutcome.Completed
    /// не означает полной нагрузки (проверяется стадия и LoadFactor), результат заполняется на
    /// каждом раннем возврате.
    /// </summary>
    public class PlateStripAnalogyRunner
    {
        public async Task<PlateStripAnalogyResult> RunAsync(
            PlateStripAnalogyRequest request,
            IPlanarMesher mesher,
            IPlateSectionShellMaterialResolver resolver,
            IShellAnalysisRunner analysisRunner,
            string openSeesExecutablePath,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(mesher);
            ArgumentNullException.ThrowIfNull(resolver);
            ArgumentNullException.ThrowIfNull(analysisRunner);

            var result = new PlateStripAnalogyResult
            {
                StripId = request.Analogy.Id,
                StationFractions = request.StationFractions
            };

            var snapshot = await mesher.BuildAsync(
                new PlanarMeshingRequest(request.Region, request.MeshSettings, []), cancellationToken);
            if (!snapshot.IsCalculable)
            {
                result.MeshDiagnostics = snapshot.Diagnostics.Select(d => d.Message).ToList();
                return result;
            }

            var built = PlanarMeshSnapshotShellModelAdapter.Build(
                snapshot, request.Region.Frame, request.Section, request.RebarField, resolver);
            if (built.Model is null)
            {
                result.BoundaryDiagnostics = built.RebarDiagnostics
                    .Select(t => t.Diagnostic.Message).ToList();
                return result;
            }

            var loadMapping = PlanarLoadMapper.Map(request.Region, snapshot, request.Loads);
            if (!loadMapping.IsCalculable)
            {
                result.BoundaryDiagnostics = loadMapping.Diagnostics.Select(d => d.Message).ToList();
                return result;
            }

            var nodalLoads = PlanarLoadOpenSeesAdapter.Map(loadMapping, built.NodeIndexToTag);
            var nodes = ApplySupports(built.Model.Nodes, request);

            var model = built.Model with
            {
                Nodes = nodes,
                Stages =
                [
                    new ShellNonlinearStage
                    {
                        Tag = "stage-1",
                        Loads = nodalLoads,
                        LoadFactorStep = 1.0,
                        MaxLoadFactor = 1.0
                    }
                ],
                // Algorithm не наследуется в model.Policy — задаём явно.
                Policy = built.Model.Policy with { Algorithm = "Newton" }
            };

            var run = await analysisRunner.RunAsync(model, openSeesExecutablePath, cancellationToken);
            if (run.Outcome != ShellAnalysisOutcome.Completed || run.Result is null)
            {
                result.BoundaryDiagnostics =
                    [run.ErrorMessage ?? $"OpenSees outcome: {run.Outcome}."];
                return result;
            }

            var converged = run.Result.Steps.Where(s => s.Converged).ToList();
            if (converged.Count == 0)
            {
                result.BoundaryDiagnostics = ["Нет ни одного сошедшегося шага нагружения."];
                return result;
            }

            // Completed != полная нагрузка: проверяем и стадию, и достигнутый LoadFactor.
            var lastStep = converged[^1];
            int lastStageIndex = model.Stages.Count - 1;
            result.IsConverged = lastStep.StageIndex == lastStageIndex &&
                                 Math.Abs(lastStep.LoadFactor - 1.0) < 1e-6;
            if (!result.IsConverged)
            {
                result.BoundaryDiagnostics =
                [
                    $"Нелинейный shell-прогон не достиг полной нагрузки: последний сошедшийся шаг — " +
                    $"стадия {lastStep.StageIndex + 1} из {model.Stages.Count}, " +
                    $"LoadFactor={lastStep.LoadFactor:F3}."
                ];
                return result;
            }

            var domainDiagnostics = new List<FemValidationDiagnostic>();
            var elementTagToIndex = built.ElementIndexToTag.ToDictionary(p => p.Value, p => p.Key);

            result.ShellResultants = ShellStripResultantSampler.Sample(
                lastStep.SectionResultants, elementTagToIndex, snapshot, request.Region.Frame,
                request.Analogy, request.StationFractions, out var samplerDiagnostics);
            domainDiagnostics.AddRange(samplerDiagnostics);
            result.ShellMaxDeflectionM = lastStep.Displacements.Count > 0
                ? lastStep.Displacements.Max(d => Math.Abs(d.Uz))
                : 0.0;

            var beam = RunBeam(request, domainDiagnostics);
            result.DomainDiagnostics = domainDiagnostics;
            result.BeamIsCalculable = beam?.IsCalculable ?? false;
            if (beam is null || !beam.IsCalculable)
                return result;

            result.BeamResultants = beam.StationResultants;
            result.BeamMaxDeflectionM = MaxTransverse(beam.Displacements);
            result.MaxRelativeMomentMismatch = MomentMismatch(result.ShellResultants, beam.StationResultants);
            result.RelativeDeflectionMismatch = Relative(result.ShellMaxDeflectionM, result.BeamMaxDeflectionM);
            return result;
        }

        static StripBeamNonlinearSolveResult? RunBeam(
            PlateStripAnalogyRequest request, List<FemValidationDiagnostic> diagnostics)
        {
            if (request.WidthSources.Count == 0)
            {
                diagnostics.Add(new("plate_strip_source_grid_shape_mismatch",
                    "Не заданы источники плитного отклика по ширине полосы."));
                return null;
            }

            var stripLoads = new List<StripLoad>();
            foreach (var load in request.Loads)
            {
                var mapped = StripLoadMapper.Map(request.Region.Frame, request.Analogy, load);
                diagnostics.AddRange(mapped.Diagnostics);
                if (mapped.IsCalculable && mapped.Load is not null)
                    stripLoads.Add(mapped.Load);
            }
            if (stripLoads.Count == 0)
            {
                diagnostics.Add(new("plate_strip_load_outside_strip",
                    "Ни одна нагрузка региона не перенеслась на полосу."));
                return null;
            }

            var grid = StripSectionSourceGrid.Uniform(
                request.WidthSources, request.StationFractions.Count - 1);

            return StripBeamNonlinearModel.Solve(
                grid, request.Analogy.ExplicitWidthM, request.Analogy.Geometry.LengthM,
                request.StationFractions, request.SupportScheme, new StripLoadSet(stripLoads),
                null, request.NewtonOptions);
        }

        /// <summary>Закрепления shell-модели, выведенные из опорной схемы полосы: концы полосы
        /// в координатах региона получают ту же кинематику, что и концы производной балки.</summary>
        static List<NormalizedShellNode> ApplySupports(
            IReadOnlyList<NormalizedShellNode> nodes, PlateStripAnalogyRequest request)
        {
            double length = request.Analogy.Geometry.LengthM;
            var stripFrame = request.Analogy.StripFrame;

            return nodes.Select(node =>
            {
                var local = PlanarBoundaryFrameConverter.ToLocalPoint(
                    stripFrame, new PlanarVector3(node.X, node.Y, node.Z));
                bool atStart = Math.Abs(local.X) < 1e-6;
                bool atEnd = Math.Abs(local.X - length) < 1e-6;
                if (!atStart && !atEnd) return node;

                var fixity = new bool[6];
                fixity[2] = true;   // UZ на обоих концах при любом условии
                fixity[3] = true;   // RX: кручение полосы вне объёма редукции

                var condition = atStart ? request.SupportScheme.StartCondition
                                        : request.SupportScheme.EndCondition;
                if (condition == StripBeamEndCondition.Fixed)
                {
                    fixity[4] = true;   // RY — изгибная ротация
                    fixity[5] = true;   // RZ
                }

                if (atStart)
                {
                    fixity[0] = true;   // продольное закрепление начала — всегда
                    fixity[1] = true;
                }
                else
                {
                    fixity[1] = true;
                    if (request.SupportScheme.AxialRestraint == StripAxialRestraint.BothEnds)
                        fixity[0] = true;
                }
                return node with { Fixed = fixity };
            }).ToList();
        }

        static double MaxTransverse(double[] displacements)
        {
            double max = 0.0;
            for (int dof = 2; dof < displacements.Length; dof += StripBeamElement.DofPerNode)
                max = Math.Max(max, Math.Abs(displacements[dof]));
            return max;
        }

        static double MomentMismatch(IReadOnlyList<double[]> shell, IReadOnlyList<double[]> beam)
        {
            if (shell.Count == 0 || shell.Count != beam.Count) return double.NaN;
            double scale = Math.Max(
                shell.Max(s => Math.Abs(s[1])), beam.Max(b => Math.Abs(b[1])));
            if (scale <= 0.0) return 0.0;

            double worst = 0.0;
            for (int i = 0; i < shell.Count; i++)
                worst = Math.Max(worst, Math.Abs(shell[i][1] - beam[i][1]) / scale);
            return worst;
        }

        static double Relative(double a, double b)
        {
            double scale = Math.Max(Math.Abs(a), Math.Abs(b));
            return scale > 0.0 ? Math.Abs(a - b) / scale : 0.0;
        }
    }
}
