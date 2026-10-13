using CScore.Fem;
using CScore.PlateStrip;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore.Fragments;

/// <summary>Результат сборки OpenSees-модели полосы.</summary>
public sealed record PlateStripFiberBeamModel(
    FemNonlinearModel? Model, IReadOnlyList<FemValidationDiagnostic> Diagnostics)
{
    public bool IsCalculable => Model is not null && !Diagnostics.Any(d => d.IsError);
}

/// <summary>
/// Срез 8c: сборка нелинейной балочной модели OpenSees (forceBeamColumn, 3D) для той же полосы,
/// что решает собственный Ньютон CScore: те же станции, опоры, нагрузки, концевые действия и осадки.
///
/// Соответствие DOF: u, v, w, θy, θz полосы = Ux, Uy, Uz, Ry, Rz (θy = −w′ в обеих моделях),
/// кручение закреплено на всех узлах — у пятистепенной формулировки Эйлера–Бернулли θx нет.
/// Единицы: домен полосы — кН, м; OpenSees — Н, м.
/// </summary>
public static class PlateStripFiberBeamModelBuilder
{
    const double KilonewtonToNewton = 1000.0;
    const double StationTolerance = 1e-9;

    // Индексы DOF OpenSees, 1-based.
    const int Ux = 1, Uy = 2, Uz = 3, Rx = 4, Ry = 5, Rz = 6;

    /// <param name="sections">Фибровые сечения OpenSees по элементам полосы (длина = станции − 1).</param>
    /// <param name="stationFractions">Станции в долях длины, от 0 до 1.</param>
    /// <param name="lengthM">Длина полосы, м.</param>
    /// <param name="scheme">Условия на концах.</param>
    /// <param name="loads">Нагрузки полосы (кН, кН/м, кН·м).</param>
    /// <param name="endActions">Концевые действия родителя (значения эпюры на концах).</param>
    /// <param name="prescribed">Заданные перемещения узлов (DOF полосы 0..4 = u, v, w, θy, θz).</param>
    /// <param name="loadSteps">Число шагов нагружения до полной нагрузки.</param>
    /// <param name="integrationPoints">Число точек интегрирования элемента.</param>
    public static PlateStripFiberBeamModel Build(
        IReadOnlyList<OpenSeesSectionModel> sections,
        IReadOnlyList<double> stationFractions,
        double lengthM,
        StripBeamSupportScheme scheme,
        IReadOnlyList<StripLoad> loads,
        KnownEndActions? endActions,
        IReadOnlyList<StripPrescribedDisplacement> prescribed,
        int loadSteps = 4,
        int integrationPoints = 5)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(stationFractions);
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentNullException.ThrowIfNull(loads);
        ArgumentNullException.ThrowIfNull(prescribed);

        var diagnostics = new List<FemValidationDiagnostic>();
        PlateStripFiberBeamModel Fail(string code, string message)
        {
            diagnostics.Add(new(code, message));
            return new(null, diagnostics);
        }

        int stations = stationFractions.Count;
        if (stations < 2)
            return Fail("plate_strip_fiber_model_stations", "Нужно не менее двух станций.");
        if (sections.Count != stations - 1)
            return Fail("plate_strip_fiber_model_sections",
                $"Сечений {sections.Count}, ожидалось {stations - 1} (по числу элементов).");
        if (!(lengthM > 0.0) || !double.IsFinite(lengthM))
            return Fail("plate_strip_fiber_model_length", "Длина полосы должна быть положительной.");
        if (Math.Abs(stationFractions[0]) > StationTolerance || Math.Abs(stationFractions[^1] - 1.0) > StationTolerance)
            return Fail("plate_strip_fiber_model_span",
                "Станции должны начинаться с 0 и заканчиваться 1: фибровая модель строится на всём пролёте.");
        for (int i = 1; i < stations; i++)
            if (!(stationFractions[i] > stationFractions[i - 1]))
                return Fail("plate_strip_fiber_model_stations_order", "Станции должны строго возрастать.");

        // --- Узлы и закрепления -------------------------------------------------------------
        var fixedMask = new bool[stations][];
        for (int i = 0; i < stations; i++)
            fixedMask[i] = [false, false, false, true, false, false];   // Rx закреплён везде

        int last = stations - 1;
        fixedMask[0][Uy - 1] = fixedMask[0][Uz - 1] = true;
        fixedMask[last][Uy - 1] = fixedMask[last][Uz - 1] = true;
        fixedMask[0][Ux - 1] = true;                                    // продольное закрепление начала
        if (scheme.AxialRestraint == StripAxialRestraint.BothEnds)
            fixedMask[last][Ux - 1] = true;
        if (scheme.StartCondition == StripBeamEndCondition.Fixed)
            fixedMask[0][Ry - 1] = fixedMask[0][Rz - 1] = true;
        if (scheme.EndCondition == StripBeamEndCondition.Fixed)
            fixedMask[last][Ry - 1] = fixedMask[last][Rz - 1] = true;

        // --- Заданные перемещения: DOF освобождается от закрепления, значение — кинематическое ---
        var kinematic = new List<FemLinearKinematicLoad>();
        var kinematicDofs = new HashSet<(int Node, int Dof)>();
        foreach (var item in prescribed)
        {
            if (item.NodeIndex < 0 || item.NodeIndex >= stations)
                return Fail("plate_strip_fiber_model_prescribed_node",
                    $"Заданное перемещение ссылается на несуществующий узел {item.NodeIndex}.");
            int dof = item.Dof switch
            {
                0 => Ux, 1 => Uy, 2 => Uz, 3 => Ry, 4 => Rz,
                _ => -1
            };
            if (dof < 0)
                return Fail("plate_strip_fiber_model_prescribed_dof",
                    $"Заданное перемещение: DOF {item.Dof} не поддерживается в формулировке Эйлера–Бернулли.");
            if (!kinematicDofs.Add((item.NodeIndex, dof)))
                return Fail("plate_strip_fiber_model_prescribed_duplicate",
                    $"Заданное перемещение узла {item.NodeIndex}, DOF {item.Dof} задано дважды.");
            fixedMask[item.NodeIndex][dof - 1] = false;
            kinematic.Add(new FemLinearKinematicLoad(item.NodeIndex + 1, dof, item.Value));
        }

        var nodes = new List<FemLinearNode>(stations);
        for (int i = 0; i < stations; i++)
            nodes.Add(new FemLinearNode(i + 1, stationFractions[i] * lengthM, 0.0, 0.0, fixedMask[i]));

        var elements = new List<FemNonlinearElement>(stations - 1);
        // Один экземпляр секции = один тег: одинаковые сечения соседних элементов не дублируются
        // (материалы OpenSees имеют глобальные теги, повтор определений недопустим).
        var sectionMap = new Dictionary<int, OpenSeesSectionModel>();
        var tagByInstance = new Dictionary<OpenSeesSectionModel, int>(ReferenceEqualityComparer.Instance);
        for (int e = 0; e < stations - 1; e++)
        {
            if (!tagByInstance.TryGetValue(sections[e], out int sectionTag))
            {
                sectionTag = tagByInstance.Count + 1;
                tagByInstance[sections[e]] = sectionTag;
                sectionMap[sectionTag] = sections[e];
            }
            elements.Add(new FemNonlinearElement(
                e + 1, e + 1, e + 2, SectionTag: sectionTag, integrationPoints, Vecxz: (0.0, 0.0, 1.0)));
        }

        // --- Нагрузки -----------------------------------------------------------------------
        var nodal = new Dictionary<int, double[]>();   // узел → Fx, Fy, Fz, Mx, My, Mz (Н, Н·м)
        void AddNodal(int node, double fx, double fy, double fz, double my, double mz)
        {
            if (!nodal.TryGetValue(node, out var f)) nodal[node] = f = new double[6];
            f[0] += fx; f[1] += fy; f[2] += fz; f[4] += my; f[5] += mz;
        }

        var distributed = new List<FemLinearDistributedLoad>();
        var points = new List<FemLinearPointLoad>();

        foreach (var load in loads)
        {
            if (load.IsDistributed)
            {
                for (int e = 0; e < stations - 1; e++)
                {
                    double fa = stationFractions[e], fb = stationFractions[e + 1];
                    double s = Math.Max(load.StationStartFraction, fa);
                    double t = Math.Min(load.StationEndFraction, fb);
                    if (t - s <= StationTolerance) continue;

                    var (qxs, qys, qzs) = load.IntensityAt(s);
                    var (qxe, qye, qze) = load.IntensityAt(t);
                    distributed.Add(new FemLinearDistributedLoad(
                        e + 1,
                        WyStart: qys * KilonewtonToNewton, WzStart: qzs * KilonewtonToNewton, WxStart: qxs * KilonewtonToNewton,
                        WyEnd: qye * KilonewtonToNewton, WzEnd: qze * KilonewtonToNewton, WxEnd: qxe * KilonewtonToNewton,
                        AOverL: (s - fa) / (fb - fa), BOverL: (t - fa) / (fb - fa)));
                }
                continue;
            }

            if (load.MxKnM != 0.0)
                diagnostics.Add(new("plate_strip_fiber_model_torque_ignored",
                    $"Нагрузка «{load.SourceTag}»: крутящий момент не переносится в фибровую модель " +
                    "(кручение закреплено, формулировка Эйлера–Бернулли).", false));

            int station = -1;
            for (int i = 0; i < stations; i++)
                if (Math.Abs(stationFractions[i] - load.StationFraction) <= StationTolerance) { station = i; break; }

            if (station >= 0)
            {
                AddNodal(station + 1,
                    load.PxKn * KilonewtonToNewton, load.PyKn * KilonewtonToNewton, load.PzKn * KilonewtonToNewton,
                    0.0, load.MzKnM * KilonewtonToNewton);
                continue;
            }

            if (load.MzKnM != 0.0)
                return Fail("plate_strip_fiber_model_point_moment",
                    $"Нагрузка «{load.SourceTag}»: сосредоточенный момент между станциями не поддерживается.");

            int element = -1;
            for (int e = 0; e < stations - 1; e++)
                if (load.StationFraction > stationFractions[e] && load.StationFraction < stationFractions[e + 1])
                { element = e; break; }
            if (element < 0)
                return Fail("plate_strip_fiber_model_point_outside",
                    $"Нагрузка «{load.SourceTag}»: положение {load.StationFraction:G6} вне полосы.");

            double xOverL = (load.StationFraction - stationFractions[element]) /
                            (stationFractions[element + 1] - stationFractions[element]);
            points.Add(new FemLinearPointLoad(
                element + 1,
                Py: load.PyKn * KilonewtonToNewton, Pz: load.PzKn * KilonewtonToNewton,
                Px: load.PxKn * KilonewtonToNewton, XOverL: xOverL));
        }

        // KnownEndActions заданы как значения эпюры на концах: на первом узле — со знаком минус.
        if (endActions is { IsZero: false })
        {
            if (!endActions.IsFinite)
                return Fail("plate_strip_fiber_model_end_actions", "Концевые действия должны быть конечными.");
            if (endActions.StartT != 0.0 || endActions.EndT != 0.0)
                diagnostics.Add(new("plate_strip_fiber_model_torque_ignored",
                    "Концевые крутящие моменты не переносятся в фибровую модель.", false));
            AddNodal(1, -endActions.StartN * KilonewtonToNewton, 0.0, 0.0,
                -endActions.StartMy * KilonewtonToNewton, -endActions.StartMz * KilonewtonToNewton);
            AddNodal(stations, endActions.EndN * KilonewtonToNewton, 0.0, 0.0,
                endActions.EndMy * KilonewtonToNewton, endActions.EndMz * KilonewtonToNewton);
        }

        if (distributed.Count == 0 && points.Count == 0 && nodal.Count == 0 && kinematic.Count == 0)
            return Fail("plate_strip_fiber_model_no_actions", "Ни одного воздействия на полосу нет.");

        if (loadSteps < 1) loadSteps = 1;
        var stage = new FemNonlinearStage
        {
            Tag = "plate-strip-fiber",
            Loads = nodal
                .OrderBy(p => p.Key)
                .Select(p => new FemLinearNodalLoad(p.Key, p.Value[0], p.Value[1], p.Value[2],
                    p.Value[3], p.Value[4], p.Value[5]))
                .ToList(),
            DistributedLoads = distributed,
            PointLoads = points,
            KinematicLoads = kinematic,
            LoadFactorStep = 1.0 / loadSteps,
            MaxLoadFactor = 1.0
        };

        var model = new FemNonlinearModel
        {
            Nodes = nodes,
            Sections = sectionMap,
            Elements = elements,
            Stages = [stage],
            GeomTransfKind = "Linear",
            ElementFormulation = "forceBeamColumn",
            Policy = new NonlinearAnalysisPolicy { Tolerance = 1e-8, MaxIterations = 60, Algorithm = "Newton" },
            RecordFiberStates = false
        };
        try
        {
            model.Validate();
        }
        catch (InvalidOperationException ex)
        {
            return Fail("plate_strip_fiber_model_invalid", ex.Message);
        }

        return new(model, diagnostics);
    }
}
