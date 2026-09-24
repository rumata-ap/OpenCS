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
    /// <summary>Источник опор shell-модели и балки при сверке полосы.</summary>
    public enum PlateStripSupportMode
    {
        /// <summary>Опорная схема полосы задана явно; закрепления shell строятся из неё.</summary>
        Explicit,
        /// <summary>Опоры — кандидаты родительской схемы; схема полосы и концевые действия
        /// выводятся из них и из shell-прогона (Срез 8a).</summary>
        DerivedFromParent
    }

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

        /// <summary>Источник опор (Срез 8a).</summary>
        public PlateStripSupportMode SupportMode { get; init; } = PlateStripSupportMode.Explicit;
        /// <summary>Кандидаты в опоры из родительской схемы — для <see cref="PlateStripSupportMode.DerivedFromParent"/>.</summary>
        public IReadOnlyList<StripSupportCandidate> ParentSupports { get; init; } = [];
        /// <summary>Допуск совпадения точки опоры полосы со следом кандидата, м.</summary>
        public double SupportMatchToleranceM { get; init; } = StripSupportDerivation.DefaultMatchToleranceM;
        /// <summary>Допуск совпадения узла сетки со следом опоры или интерфейса, м: узлы на
        /// встроенных кривых и рёбрах контура лежат на следе точно.</summary>
        public double NodeOnFootprintToleranceM { get; init; } = 1e-6;
        /// <summary>Граничные интерфейсы полосы: краевые силы и кинематические ГУ.</summary>
        public IReadOnlyList<StripBoundaryInterface> Interfaces { get; init; } = [];
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
    ///
    /// Срез 8a: режим <see cref="PlateStripSupportMode.DerivedFromParent"/> — опоры shell из
    /// кандидатов родителя, схема полосы и концевые моменты выводятся из них и из shell-прогона,
    /// который играет роль родителя. Моменты на шарнирных концах при этом заморожены на
    /// значениях прогона × λ, поэтому сверка осмысленна до трещинообразования. Кинематические
    /// интерфейсы применяются к обеим моделям: к оболочке — <c>sp</c> в той же пропорциональной
    /// стадии, к балке — заданными перемещениями.
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
            bool derived = request.SupportMode == PlateStripSupportMode.DerivedFromParent;
            var domainDiagnostics = new List<FemValidationDiagnostic>();

            // 1–2. Станции и автовывод опор — до мешинга: ошибка здесь не стоит прогона Gmsh.
            StripSupportDerivationResult? derivation = null;
            if (derived)
            {
                var stations = request.StationFractions;
                if (stations.Count < 2 || Math.Abs(stations[0]) > 1e-12 || Math.Abs(stations[^1] - 1.0) > 1e-12)
                    return Fail(result, domainDiagnostics, new FemValidationDiagnostic("plate_strip_stations_must_span_strip",
                        "В режиме опор из родителя станции обязаны начинаться с 0 и кончаться 1: " +
                        "концевые моменты снимаются с эпюры на концах полосы."));

                derivation = StripSupportDerivation.Derive(
                    request.Analogy, request.Region.Frame, request.ParentSupports, request.SupportMatchToleranceM);
                domainDiagnostics.AddRange(derivation.Diagnostics);
                result.Derivation = derivation;
                if (!derivation.IsCalculable)
                    return Fail(result, domainDiagnostics);
            }

            // 3. Граничные интерфейсы: проверка и перенос кинематики на балку.
            foreach (var boundary in request.Interfaces)
            {
                var checks = derived
                    ? boundary.Validate(request.Analogy, request.ParentSupports, request.SupportMatchToleranceM)
                    : boundary.Validate(request.Analogy);
                domainDiagnostics.AddRange(checks);
                if (!derived && HasMode(boundary, PlanarBoundaryDofMode.PreserveSupport))
                    domainDiagnostics.Add(new("plate_strip_preserve_support_unchecked",
                        $"Граница «{boundary.Id}»: PreserveSupport не проверен на реальную опору — в " +
                        "режиме явной схемы кандидатов родителя нет.", false));
            }
            if (domainDiagnostics.Any(d => d.IsError))
                return Fail(result, domainDiagnostics);

            var kinematic = StripKinematicMapper.MapAll(
                request.Region.Frame, request.Analogy, request.Interfaces, request.StationFractions);
            domainDiagnostics.AddRange(kinematic.Diagnostics);
            if (!kinematic.IsCalculable)
                return Fail(result, domainDiagnostics);
            result.Prescribed = kinematic.Values;

            var kinematicInterfaces = request.Interfaces
                .Where(b => HasMode(b, PlanarBoundaryDofMode.Kinematic))
                .ToList();
            if (kinematicInterfaces.Count > 0 && !IsAlignedWithGlobal(request.Region.Frame))
                return Fail(result, domainDiagnostics, new FemValidationDiagnostic("plate_strip_runner_kinematic_frame_unsupported",
                    "Кинематические интерфейсы в сверке поддерживаются только для региона, оси которого " +
                    "совпадают с глобальными: режимы DOF заданы в осях региона, а DOF оболочки — в глобальных."));

            // 4. Constraints мешинга. Explicit без интерфейсов — прежний контракт: [].
            IReadOnlyList<PlanarConstraintObject> constraints = [];
            if (derived || kinematicInterfaces.Count > 0)
            {
                var built = BuildConstraintObjects(request, derived, kinematicInterfaces, out var conflict);
                if (conflict != null)
                    return Fail(result, domainDiagnostics, conflict);
                constraints = built;
            }

            var snapshot = await mesher.BuildAsync(
                new PlanarMeshingRequest(request.Region, request.MeshSettings, constraints), cancellationToken);
            if (!snapshot.IsCalculable)
            {
                result.MeshDiagnostics = snapshot.Diagnostics.Select(d => d.Message).ToList();
                result.DomainDiagnostics = domainDiagnostics;
                return result;
            }

            var shell = PlanarMeshSnapshotShellModelAdapter.Build(
                snapshot, request.Region.Frame, request.Section, request.RebarField, resolver);
            if (shell.Model is null)
            {
                result.BoundaryDiagnostics = shell.RebarDiagnostics
                    .Select(t => t.Diagnostic.Message).ToList();
                result.DomainDiagnostics = domainDiagnostics;
                return result;
            }

            // Перегрузка с регионом дополнительно валидирует boundary contract снимка; она
            // нужна только когда среди нагрузок есть краевые. Для поверхностных и точечных
            // достаточно системы координат — иначе регион без размеченных BoundarySegments
            // отвергал бы совершенно корректную поверхностную нагрузку.
            bool hasBoundaryLoads = request.Loads.Any(l => l.Kind == PlanarLoadKind.Boundary);
            var loadMapping = hasBoundaryLoads
                ? PlanarLoadMapper.Map(request.Region, snapshot, request.Loads)
                : PlanarLoadMapper.Map(request.Region.Frame, snapshot, request.Loads);
            if (!loadMapping.IsCalculable)
            {
                result.BoundaryDiagnostics = loadMapping.Diagnostics.Select(d => d.Message).ToList();
                result.DomainDiagnostics = domainDiagnostics;
                return result;
            }

            // Единицы: PlanarLoad задаётся в кН (как StripLoad доменной части), OpenSees
            // работает в СИ. Конверсия зеркальна той, что ShellResultantRotation делает на
            // выходе; без неё расчёт шёл бы с нагрузкой, заниженной ровно в 1000 раз, а
            // сверка shell/beam разъезжалась бы на тот же множитель.
            const double kilonewtonToNewton = 1000.0;
            var nodalLoads = PlanarLoadOpenSeesAdapter.Map(loadMapping, shell.NodeIndexToTag)
                .Select(load => new ShellNodalLoad(
                    load.NodeTag,
                    load.Fx * kilonewtonToNewton, load.Fy * kilonewtonToNewton, load.Fz * kilonewtonToNewton,
                    load.Mx * kilonewtonToNewton, load.My * kilonewtonToNewton, load.Mz * kilonewtonToNewton))
                .ToList();

            // 6. Закрепления shell.
            List<NormalizedShellNode> nodes;
            if (derived)
            {
                nodes = ApplyParentSupports(shell.Model.Nodes, request, out var coverage);
                if (coverage.Count > 0)
                    return Fail(result, domainDiagnostics, coverage.ToArray());
            }
            else
                nodes = ApplySupports(shell.Model.Nodes, request);

            // 7. Кинематика shell: sp в той же пропорциональной стадии, fix на этих DOF снимается.
            var kinematicLoads = ApplyShellKinematics(nodes, request, kinematicInterfaces, out var kinematicErrors);
            if (kinematicErrors.Count > 0)
                return Fail(result, domainDiagnostics, kinematicErrors.ToArray());

            var model = shell.Model with
            {
                Nodes = nodes,
                Stages =
                [
                    new ShellNonlinearStage
                    {
                        Tag = "stage-1",
                        Loads = nodalLoads,
                        KinematicLoads = kinematicLoads,
                        LoadFactorStep = 1.0,
                        MaxLoadFactor = 1.0
                    }
                ],
                // Algorithm не наследуется в model.Policy — задаём явно.
                Policy = shell.Model.Policy with { Algorithm = "Newton" }
            };

            var run = await analysisRunner.RunAsync(model, openSeesExecutablePath, cancellationToken);
            if (run.Outcome != ShellAnalysisOutcome.Completed || run.Result is null)
            {
                result.BoundaryDiagnostics =
                    [run.ErrorMessage ?? $"OpenSees outcome: {run.Outcome}."];
                result.DomainDiagnostics = domainDiagnostics;
                return result;
            }

            var converged = run.Result.Steps.Where(s => s.Converged).ToList();
            if (converged.Count == 0)
            {
                result.BoundaryDiagnostics = ["Нет ни одного сошедшегося шага нагружения."];
                result.DomainDiagnostics = domainDiagnostics;
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
                result.DomainDiagnostics = domainDiagnostics;
                return result;
            }

            var elementTagToIndex = shell.ElementIndexToTag.ToDictionary(p => p.Value, p => p.Key);

            result.ShellResultants = ShellStripResultantSampler.Sample(
                lastStep.SectionResultants, elementTagToIndex, snapshot, request.Region.Frame,
                request.Analogy, request.StationFractions, out var samplerDiagnostics);
            domainDiagnostics.AddRange(samplerDiagnostics);
            result.ShellMaxDeflectionM = MaxCorridorDeflection(lastStep.Displacements, nodes, request);

            // 9. Концевые действия из shell-прогона в роли родителя.
            KnownEndActions? endActions = null;
            if (derived)
            {
                double[]? start = result.ShellResultants.Count > 0 ? result.ShellResultants[0] : null;
                double[]? end = result.ShellResultants.Count > 0 ? result.ShellResultants[^1] : null;
                if (!StripParentEndActions.TryFrom(start, end, derivation!, out endActions, out var endDiagnostics))
                    return Fail(result, domainDiagnostics, endDiagnostics.ToArray());
                result.EndActions = endActions;
                if (endActions!.StartMy != 0.0 || endActions.StartMz != 0.0 ||
                    endActions.EndMy != 0.0 || endActions.EndMz != 0.0)
                    domainDiagnostics.Add(new("plate_strip_end_moments_frozen",
                        "Концевые моменты полосы взяты из родителя и масштабируются вместе с нагрузкой: " +
                        "перераспределения моментов на опоры в нелинейном расчёте полосы нет.", false));
            }

            var scheme = derivation?.Scheme ?? request.SupportScheme;
            var beam = RunBeam(request, scheme, endActions, kinematic.Values, domainDiagnostics);
            // Диагностики самого решателя обязаны дойти до результата: без них «не сошлось»
            // возвращалось бы без единого объяснения.
            if (beam is not null) domainDiagnostics.AddRange(beam.Diagnostics);
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

        static PlateStripAnalogyResult Fail(
            PlateStripAnalogyResult result, List<FemValidationDiagnostic> diagnostics,
            params FemValidationDiagnostic[] extra)
        {
            diagnostics.AddRange(extra);
            result.DomainDiagnostics = diagnostics;
            return result;
        }

        static StripBeamNonlinearSolveResult? RunBeam(
            PlateStripAnalogyRequest request, StripBeamSupportScheme scheme, KnownEndActions? endActions,
            IReadOnlyList<StripPrescribedDisplacement> prescribed, List<FemValidationDiagnostic> diagnostics)
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
                var mapped = StripLoadMapper.Map(
                    request.Region.Frame, request.Analogy, load, 1e-6, request.Interfaces);
                diagnostics.AddRange(mapped.Diagnostics);
                if (mapped.IsCalculable && mapped.Load is not null)
                    stripLoads.Add(mapped.Load);
            }
            // Чисто кинематическое нагружение или нагружение одними концевыми действиями законно:
            // досрочный выход — только когда воздействий нет вовсе.
            bool hasOtherActions = prescribed.Count > 0 || endActions is { IsZero: false };
            if (stripLoads.Count == 0 && !hasOtherActions)
            {
                diagnostics.Add(new("plate_strip_load_outside_strip",
                    "Ни одна нагрузка региона не перенеслась на полосу."));
                return null;
            }

            var grid = StripSectionSourceGrid.Uniform(
                request.WidthSources, request.StationFractions.Count - 1);

            return StripBeamNonlinearModel.Solve(
                grid, request.Analogy.ExplicitWidthM, request.Analogy.Geometry.LengthM,
                request.StationFractions, scheme, new StripLoadSet(stripLoads),
                endActions, request.NewtonOptions, prescribed);
        }

        /// <summary>Региональные constraints плюс встроенные следы кандидатов и кинематических
        /// интерфейсов, не лежащие на границе контура. След, совпадающий с уже добавленным,
        /// пропускается: две совпадающие встроенные кривые Gmsh не принимает.</summary>
        static IReadOnlyList<PlanarConstraintObject> BuildConstraintObjects(
            PlateStripAnalogyRequest request, bool derived,
            IReadOnlyList<StripBoundaryInterface> kinematicInterfaces,
            out FemValidationDiagnostic? conflict)
        {
            conflict = null;
            var hull = request.Region.RequireHull();
            double tol = request.NodeOnFootprintToleranceM;
            var result = request.Region.ConstraintObjects.ToList();
            var ids = new HashSet<string>(result.Select(c => c.Id), StringComparer.Ordinal);
            var embedded = new List<PlanarConstraintGeometry>();

            // Сначала следы опор, затем интерфейсы (каждая группа — по Id): при совпадении геометрии
            // выживает след опоры, и его Id остаётся в provenance сетки.
            var automatic = new List<(string Id, PlanarConstraintGeometry Geometry)>();
            if (derived)
                automatic.AddRange(request.ParentSupports
                    .Select(c => (c.Id, c.Footprint))
                    .OrderBy(a => a.Id, StringComparer.Ordinal));
            automatic.AddRange(kinematicInterfaces
                .Select(b => ($"interface:{b.Id}", b.Geometry))
                .OrderBy(a => a.Item1, StringComparer.Ordinal));

            foreach (var (id, geometry) in automatic)
            {
                if (geometry.Points.Count == 0) continue;
                if (StripSupportGeometry.IsOnHullBoundary(geometry, hull, tol)) continue;
                if (embedded.Any(e => Covers(e, geometry, tol))) continue;
                if (!ids.Add(id))
                {
                    conflict = new("plate_strip_constraint_id_conflict",
                        $"Автоматический constraint «{id}» совпадает по Id с constraint региона.");
                    return [];
                }

                var structural = new PlanarStructuralFacet(PlanarStructuralKind.None);
                result.Add(geometry.Points.Count == 1
                    ? PlanarConstraintObject.Point(id, geometry.Points[0], structural,
                        new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint))
                    : PlanarConstraintObject.Curve(id, geometry.Points, structural,
                        new PlanarMeshFacet(PlanarMeshKind.EmbeddedCurve)));
                embedded.Add(geometry);
            }
            return result;
        }

        /// <summary>Все точки и середины отрезков geometry лежат на существующем следе.</summary>
        static bool Covers(PlanarConstraintGeometry existing, PlanarConstraintGeometry geometry, double tol)
        {
            var points = geometry.Points;
            for (int i = 0; i < points.Count; i++)
            {
                if (StripSupportGeometry.DistanceToFootprint(points[i], existing) > tol) return false;
                if (i < points.Count - 1)
                {
                    var mid = new PlanarPoint2D(
                        (points[i].U + points[i + 1].U) / 2.0, (points[i].V + points[i + 1].V) / 2.0);
                    if (StripSupportGeometry.DistanceToFootprint(mid, existing) > tol) return false;
                }
            }
            return true;
        }

        /// <summary>Закрепления shell из кандидатов родителя: NodalRestraint — его маска, колонна и
        /// стена — перемещения UX, UY, UZ. Покрытие проверяется по каждому кандидату: кандидат без
        /// единого узла сетки означал бы молча потерянную опору.</summary>
        static List<NormalizedShellNode> ApplyParentSupports(
            IReadOnlyList<NormalizedShellNode> nodes, PlateStripAnalogyRequest request,
            out List<FemValidationDiagnostic> coverage)
        {
            coverage = [];
            double tol = request.NodeOnFootprintToleranceM;
            var result = nodes.ToList();
            var covered = new HashSet<string>(StringComparer.Ordinal);

            for (int n = 0; n < result.Count; n++)
            {
                var node = result[n];
                var uv = RegionPoint(request.Region.Frame, node);
                bool[]? fixity = null;
                foreach (var candidate in request.ParentSupports)
                {
                    if (StripSupportGeometry.DistanceToFootprint(uv, candidate.Footprint) > tol) continue;
                    covered.Add(candidate.Id);
                    fixity ??= (bool[])node.Fixed.Clone();
                    if (candidate.Kind == StripSupportKind.NodalRestraint)
                        for (int k = 0; k < 6; k++) fixity[k] |= candidate.RestrainedDofs[k];
                    else
                        fixity[0] = fixity[1] = fixity[2] = true;
                }
                if (fixity != null) result[n] = node with { Fixed = fixity };
            }

            foreach (var candidate in request.ParentSupports.Where(c => !covered.Contains(c.Id)))
                coverage.Add(new("plate_strip_parent_support_not_meshed",
                    $"Опора «{candidate.Id}» ({candidate.Source.SourceKind} «{candidate.Source.SourceId}») " +
                    "не совпала ни с одним узлом сетки — shell-модель потеряла бы эту опору."));
            return result;
        }

        /// <summary>Заданные перемещения узлов оболочки на кинематических интерфейсах. Значение в узле
        /// — сэмплы, интерполированные по нормированной длине дуги узла вдоль интерфейса; режимы DOF
        /// заданы в осях региона, которые для этого пути совпадают с глобальными.</summary>
        static IReadOnlyList<ShellKinematicLoad> ApplyShellKinematics(
            List<NormalizedShellNode> nodes, PlateStripAnalogyRequest request,
            IReadOnlyList<StripBoundaryInterface> interfaces, out List<FemValidationDiagnostic> errors)
        {
            errors = [];
            var loads = new List<ShellKinematicLoad>();
            if (interfaces.Count == 0) return loads;

            double tol = request.NodeOnFootprintToleranceM;
            var frame = request.Region.Frame;
            PlanarVector3[] regionAxes = [frame.LocalX, frame.LocalY, frame.LocalZ];
            var byNodeDof = new Dictionary<(int Tag, int Dof), double>();

            foreach (var boundary in interfaces)
            {
                var action = boundary.KinematicAction!;
                var modes = boundary.ModeByDof ?? PlanarBoundaryModeByDof.None;
                PlanarBoundaryDofMode[] values = [modes.UX, modes.UY, modes.UZ, modes.RX, modes.RY, modes.RZ];
                int hits = 0;

                for (int n = 0; n < nodes.Count; n++)
                {
                    var uv = RegionPoint(frame, nodes[n]);
                    if (!TryArcParameter(boundary.Geometry.Points, uv, tol, out double s)) continue;
                    hits++;

                    var (displacement, rotation) = EvaluateKinematic(action, s);
                    var d = PlanarBoundaryFrameConverter.ToGlobalVector(action.Frame, displacement);
                    var r = PlanarBoundaryFrameConverter.ToGlobalVector(action.Frame, rotation);
                    var fixity = (bool[])nodes[n].Fixed.Clone();

                    for (int k = 0; k < 6; k++)
                    {
                        if (values[k] != PlanarBoundaryDofMode.Kinematic) continue;
                        int globalDof = GlobalAxisIndex(regionAxes[k % 3]) + (k >= 3 ? 3 : 0);
                        var vector = k >= 3 ? r : d;
                        // Скобки обязательны: switch-выражение связывает сильнее %, без них вышло бы
                        // globalDof % (3 switch {...}).
                        double value = (globalDof % 3) switch { 0 => vector.X, 1 => vector.Y, _ => vector.Z };
                        var key = (nodes[n].Tag, globalDof);
                        if (byNodeDof.TryGetValue(key, out double existing) &&
                            Math.Abs(existing - value) > 1e-12 * Math.Max(1.0, Math.Abs(value)))
                        {
                            errors.Add(new("plate_strip_kinematic_conflict",
                                $"Узел оболочки {nodes[n].Tag}: интерфейсы задают разные значения DOF {globalDof + 1}."));
                            continue;
                        }
                        byNodeDof[key] = value;
                        fixity[globalDof] = false; // sp и fix на одном DOF в OpenSees конфликтуют
                    }
                    nodes[n] = nodes[n] with { Fixed = fixity };
                }

                if (hits == 0)
                    errors.Add(new("plate_strip_boundary_not_meshed",
                        $"Граница «{boundary.Id}» не совпала ни с одним узлом сетки — кинематика потерялась бы."));
            }

            loads.AddRange(byNodeDof
                .OrderBy(p => p.Key.Tag).ThenBy(p => p.Key.Dof)
                .Select(p => new ShellKinematicLoad(p.Key.Tag, p.Key.Dof + 1, p.Value)));
            return loads;
        }

        static PlanarPoint2D RegionPoint(Frame3D frame, NormalizedShellNode node)
        {
            var local = PlanarBoundaryFrameConverter.ToLocalPoint(frame, new PlanarVector3(node.X, node.Y, node.Z));
            return new PlanarPoint2D(local.X, local.Y);
        }

        /// <summary>Нормированная длина дуги ближайшей точки ломаной, если узел на ней.</summary>
        static bool TryArcParameter(IReadOnlyList<PlanarPoint2D> points, PlanarPoint2D p, double tol, out double s)
        {
            s = double.NaN;
            if (points.Count < 2) return false;
            double total = 0.0, best = double.PositiveInfinity, bestArc = 0.0;
            for (int i = 0; i < points.Count - 1; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                double du = b.U - a.U, dv = b.V - a.V;
                double length = Math.Sqrt(du * du + dv * dv);
                double t = length > 0.0
                    ? Math.Clamp(((p.U - a.U) * du + (p.V - a.V) * dv) / (length * length), 0.0, 1.0)
                    : 0.0;
                double distance = Math.Sqrt(
                    Math.Pow(a.U + t * du - p.U, 2) + Math.Pow(a.V + t * dv - p.V, 2));
                if (distance < best)
                {
                    best = distance;
                    bestArc = total + t * length;
                }
                total += length;
            }
            if (best > tol || !(total > 0.0)) return false;
            s = bestArc / total;
            return true;
        }

        static (PlanarVector3 Displacement, PlanarVector3 Rotation) EvaluateKinematic(
            PlanarBoundaryKinematicAction action, double s)
        {
            var samples = action.Samples;
            if (samples.Count == 0) return (PlanarVector3.Zero, PlanarVector3.Zero);
            if (samples.Count == 1 || action.Interpolation == PlanarBoundaryInterpolationKind.Uniform)
                return (samples[0].Displacement, samples[0].Rotation);
            int upper = 1;
            while (upper < samples.Count && samples[upper].S < s) upper++;
            if (upper >= samples.Count) return (samples[^1].Displacement, samples[^1].Rotation);
            var left = samples[upper - 1];
            var right = samples[upper];
            double denominator = right.S - left.S;
            double t = denominator <= 1e-12 ? 0.0 : Math.Clamp((s - left.S) / denominator, 0.0, 1.0);
            return (
                left.Displacement + (right.Displacement - left.Displacement) * t,
                left.Rotation + (right.Rotation - left.Rotation) * t);
        }

        static bool HasMode(StripBoundaryInterface boundary, PlanarBoundaryDofMode mode)
        {
            var modes = boundary.ModeByDof ?? PlanarBoundaryModeByDof.None;
            return new[] { modes.UX, modes.UY, modes.UZ, modes.RX, modes.RY, modes.RZ }.Contains(mode);
        }

        static bool IsAlignedWithGlobal(Frame3D frame) =>
            new[] { frame.LocalX, frame.LocalY, frame.LocalZ }.All(axis =>
                Math.Abs(Math.Max(Math.Abs(axis.X), Math.Max(Math.Abs(axis.Y), Math.Abs(axis.Z))) - 1.0) <= 1e-9);

        static int GlobalAxisIndex(PlanarVector3 axis) =>
            Math.Abs(axis.X) >= Math.Abs(axis.Y) && Math.Abs(axis.X) >= Math.Abs(axis.Z) ? 0
            : Math.Abs(axis.Y) >= Math.Abs(axis.Z) ? 1 : 2;

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

        /// <summary>Наибольший прогиб узлов оболочки внутри коридора полосы. Регион может быть
        /// шире и длиннее полосы (неразрезная плита), поэтому максимум по всей сетке сравнивал бы
        /// прогиб чужого пролёта.</summary>
        static double MaxCorridorDeflection(
            IReadOnlyList<ShellNodeDisplacement> displacements, IReadOnlyList<NormalizedShellNode> nodes,
            PlateStripAnalogyRequest request)
        {
            double length = request.Analogy.Geometry.LengthM;
            double halfWidth = request.Analogy.ExplicitWidthM / 2.0;
            const double tol = 1e-6;
            var inCorridor = nodes
                .Where(node =>
                {
                    var local = PlanarBoundaryFrameConverter.ToLocalPoint(
                        request.Analogy.StripFrame, new PlanarVector3(node.X, node.Y, node.Z));
                    return local.X >= -tol && local.X <= length + tol && Math.Abs(local.Y) <= halfWidth + tol;
                })
                .Select(node => node.Tag)
                .ToHashSet();
            return displacements
                .Where(d => inCorridor.Contains(d.NodeTag))
                .Select(d => Math.Abs(d.Uz))
                .DefaultIfEmpty(0.0)
                .Max();
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
