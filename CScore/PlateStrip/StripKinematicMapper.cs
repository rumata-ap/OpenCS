using CScore.Fem;
using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>Заданное перемещение узла производной балки полосы.</summary>
/// <param name="NodeIndex">Индекс узла балки (станции).</param>
/// <param name="Dof">DOF узла в порядке u, v, w, θy, θz (0..4) — как у <see cref="StripBeamElement"/>.</param>
/// <param name="Value">Значение при λ = 1, м или рад.</param>
public sealed record StripPrescribedDisplacement(int NodeIndex, int Dof, double Value);

/// <summary>Результат переноса кинематических ГУ на полосу.</summary>
public sealed record StripKinematicMappingResult(
    bool IsCalculable,
    IReadOnlyList<StripPrescribedDisplacement> Values,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Перенос кинематических граничных условий на производную балку полосы (спека Среза 8a,
/// блок C). Граница должна пересекать ось полосы в узле балки; граница вдоль полосы не
/// переносится — балка не выражает поле перемещений вдоль длины.
///
/// Режимы DOF границы заданы в осях региона (UX..RZ). DOF балки с глобальной осью a переносится,
/// если все компоненты региона с ненулевой проекцией a в режиме Kinematic; частично кинематический
/// DOF не определён и даёт ошибку. Значения — из <see cref="PlanarBoundaryKinematicAction"/>,
/// интерполированные по s и переведённые из <c>Action.Frame</c> в оси полосы. Единицы — СИ
/// (м, рад), как у балки; конверсии нет.
///
/// Валидацию интерфейса (<see cref="StripBoundaryInterface.Validate(PlateStripBeamAnalogy)"/> или
/// перегрузку с кандидатами) выполняет вызывающий: какая проверка нужна, решает оркестратор.</summary>
public static class StripKinematicMapper
{
    const double ProjectionEpsilon = 1e-9;
    const double StationTolerance = 1e-9;

    public static StripKinematicMappingResult Map(
        Frame3D regionFrame, PlateStripBeamAnalogy analogy,
        StripBoundaryInterface boundary, IReadOnlyList<double> stationFractions)
    {
        ArgumentNullException.ThrowIfNull(regionFrame);
        ArgumentNullException.ThrowIfNull(analogy);
        ArgumentNullException.ThrowIfNull(boundary);
        ArgumentNullException.ThrowIfNull(stationFractions);

        var diagnostics = new List<FemValidationDiagnostic>();
        var modes = boundary.ModeByDof ?? PlanarBoundaryModeByDof.None;
        PlanarBoundaryDofMode[] translation = [modes.UX, modes.UY, modes.UZ];
        PlanarBoundaryDofMode[] rotation = [modes.RX, modes.RY, modes.RZ];
        if (!translation.Concat(rotation).Any(m => m == PlanarBoundaryDofMode.Kinematic))
            return new(true, [], diagnostics);

        var action = boundary.KinematicAction;
        if (action == null)
        {
            diagnostics.Add(new("plate_strip_boundary_kinematic_action_missing",
                $"Граница «{boundary.Id}»: есть DOF в режиме Kinematic, но KinematicAction не задан."));
            return new(false, [], diagnostics);
        }

        if (!boundary.TryProjectToStation(regionFrame, analogy, out double station, out double s))
        {
            diagnostics.Add(new("plate_strip_kinematic_along_strip_unsupported",
                $"Граница «{boundary.Id}» не пересекает ось полосы в пределах пролёта: заданные " +
                "перемещения вдоль полосы балкой не выражаются."));
            return new(false, [], diagnostics);
        }

        int node = NearestNode(stationFractions, station);
        if (node < 0)
        {
            diagnostics.Add(new("plate_strip_kinematic_station_not_node",
                $"Граница «{boundary.Id}» пересекает ось полосы на станции {station:G6}, где нет узла " +
                "балки: заданное перемещение узла не переносится молча на соседний узел."));
            return new(false, [], diagnostics);
        }

        var (displacement, rotationValue) = Evaluate(action.Samples, s, action.Interpolation);
        var d = PlanarBoundaryFrameConverter.ToGlobalVector(action.Frame, displacement);
        var r = PlanarBoundaryFrameConverter.ToGlobalVector(action.Frame, rotationValue);

        var strip = analogy.StripFrame;
        PlanarVector3[] regionAxes = [regionFrame.LocalX, regionFrame.LocalY, regionFrame.LocalZ];
        var values = new List<StripPrescribedDisplacement>();
        bool ok = true;

        (int Dof, PlanarVector3 Axis, bool Rotation, string Name)[] beamDofs =
        [
            (0, strip.LocalX, false, "u"),
            (1, strip.LocalY, false, "v"),
            (2, strip.LocalZ, false, "w"),
            (3, strip.LocalY, true, "θy"),
            (4, strip.LocalZ, true, "θz")
        ];
        foreach (var (dof, axis, isRotation, name) in beamDofs)
        {
            var dofModes = isRotation ? rotation : translation;
            int participating = 0, kinematic = 0;
            for (int i = 0; i < 3; i++)
            {
                if (Math.Abs(axis.Dot(regionAxes[i])) <= ProjectionEpsilon) continue;
                participating++;
                if (dofModes[i] == PlanarBoundaryDofMode.Kinematic) kinematic++;
            }
            if (kinematic == 0) continue;
            if (kinematic < participating)
            {
                diagnostics.Add(new("plate_strip_kinematic_mode_mixed",
                    $"Граница «{boundary.Id}»: DOF {name} полосы опирается на компоненты региона, " +
                    "из которых только часть в режиме Kinematic — заданное значение не определено."));
                ok = false;
                continue;
            }
            values.Add(new(node, dof, (isRotation ? r : d).Dot(axis)));
        }

        if (rotation.Any(m => m == PlanarBoundaryDofMode.Kinematic) && Math.Abs(r.Dot(strip.LocalX)) > 1e-12)
            diagnostics.Add(new("plate_strip_kinematic_torsion_not_transferred",
                $"Граница «{boundary.Id}»: поворот вокруг оси полосы ({r.Dot(strip.LocalX):G6} рад) не " +
                "переносится — кручение полосы вне модели (Срез 8b).", false));

        return ok ? new(true, values, diagnostics) : new(false, [], diagnostics);
    }

    /// <summary>Перенос по всем границам с объединением: одинаковые (узел, DOF) с равными
    /// значениями дают одну запись, с разными — ошибку <c>plate_strip_kinematic_conflict</c>.</summary>
    public static StripKinematicMappingResult MapAll(
        Frame3D regionFrame, PlateStripBeamAnalogy analogy,
        IReadOnlyList<StripBoundaryInterface> boundaries, IReadOnlyList<double> stationFractions)
    {
        ArgumentNullException.ThrowIfNull(boundaries);
        var diagnostics = new List<FemValidationDiagnostic>();
        var merged = new Dictionary<(int Node, int Dof), (double Value, string Source)>();
        bool ok = true;

        foreach (var boundary in boundaries)
        {
            var mapped = Map(regionFrame, analogy, boundary, stationFractions);
            diagnostics.AddRange(mapped.Diagnostics);
            ok &= mapped.IsCalculable;

            foreach (var value in mapped.Values)
            {
                var key = (value.NodeIndex, value.Dof);
                if (!merged.TryGetValue(key, out var existing))
                {
                    merged[key] = (value.Value, boundary.Id);
                    continue;
                }
                if (Math.Abs(existing.Value - value.Value) <= 1e-12 * Math.Max(1.0, Math.Abs(value.Value)))
                    continue;
                diagnostics.Add(new("plate_strip_kinematic_conflict",
                    $"Границы «{existing.Source}» и «{boundary.Id}» задают узлу {value.NodeIndex} " +
                    $"полосы разные значения DOF {value.Dof}: {existing.Value:G6} и {value.Value:G6}."));
                ok = false;
            }
        }

        if (!ok) return new(false, [], diagnostics);
        var values = merged
            .OrderBy(p => p.Key.Node).ThenBy(p => p.Key.Dof)
            .Select(p => new StripPrescribedDisplacement(p.Key.Node, p.Key.Dof, p.Value.Value))
            .ToList();
        return new(true, values, diagnostics);
    }

    static int NearestNode(IReadOnlyList<double> stations, double station)
    {
        for (int i = 0; i < stations.Count; i++)
            if (Math.Abs(stations[i] - station) <= StationTolerance)
                return i;
        return -1;
    }

    /// <summary>Правило интерполяции сэмплов то же, что у PlanarBoundaryActionMeshMapper: один
    /// сэмпл или Uniform — первый; иначе линейно по S с клампом по краям.</summary>
    static (PlanarVector3 Displacement, PlanarVector3 Rotation) Evaluate(
        IReadOnlyList<PlanarBoundaryKinematicSample> samples, double s, PlanarBoundaryInterpolationKind interpolation)
    {
        if (samples.Count == 0) return (PlanarVector3.Zero, PlanarVector3.Zero);
        if (samples.Count == 1 || interpolation == PlanarBoundaryInterpolationKind.Uniform)
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
}
