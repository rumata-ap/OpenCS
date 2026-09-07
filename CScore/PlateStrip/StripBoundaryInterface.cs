using CScore.Fem;
using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>
/// Граница между заменяемой beam-аналогией и сохраняемой shell-частью региона
/// (родительская спека, «Граничные интерфейсы заменяемой области»; Срез 7).
///
/// Режимы по DOF берутся готовыми из планарных фрагментов
/// (<see cref="PlanarBoundaryModeByDof"/>/<see cref="PlanarBoundaryDofMode"/>) — свой enum не
/// заводится, чтобы у полосы и у фрагментов не разошлась семантика
/// force/kinematic/preserve_support.
///
/// <see cref="ForceAction"/> обязателен, когда хотя бы один DOF в режиме Force: только он несёт
/// интенсивность вдоль границы (<c>Samples(S, ForcePerLength, MomentPerLength)</c>).
/// PlanarLoad такого выразить не может — у него один константный вектор.
///
/// <b>Проверка «PreserveSupport только при реальной опоре» сознательно не реализуется.</b> На
/// текущем домене она нереализуема: PlateStripBeamAnalogy.StartSupportLocus/EndSupportLocus
/// инициализированы new() и никогда не null, а BeamJunctionMode.Support — значение по умолчанию,
/// так что «опора есть» истинно всегда. Кроме того, такая проверка вводила бы автовывод опорной
/// схемы из SupportLocus, который Срезы 6 и 7 явно оставляют за границей объёма.
/// </summary>
public sealed class StripBoundaryInterface
{
    /// <summary>Идентификатор интерфейса.</summary>
    public string Id { get; init; } = "";

    /// <summary>Полоса, которой принадлежит интерфейс.</summary>
    public string StripId { get; init; } = "";

    /// <summary>Участок контура региона, если граница лежит на нём.</summary>
    public PlanarBoundaryKey? BoundaryKey { get; init; }

    /// <summary>Цепочка точек границы во внутренних координатах региона.</summary>
    public PlanarConstraintGeometry Geometry { get; init; } =
        new(PlanarConstraintGeometryKind.Curve, []);

    /// <summary>Нормаль от заменяемой части к сохраняемой, в координатах региона: лежит в
    /// плоскости (U, V), поэтому третья компонента обязана быть нулевой.</summary>
    public PlanarVector3 NormalFromReplacedToRetained { get; init; } = PlanarVector3.Zero;

    /// <summary>Режимы шести степеней свободы границы.</summary>
    public PlanarBoundaryModeByDof ModeByDof { get; init; } = PlanarBoundaryModeByDof.None;

    /// <summary>Силовое действие на границе — единственный источник интенсивности вдоль неё.</summary>
    public PlanarBoundaryForceAction? ForceAction { get; init; }

    /// <summary>Проверить форму интерфейса. Диагностики, не исключения: интерфейс приходит из
    /// пользовательских данных.</summary>
    public IReadOnlyList<FemValidationDiagnostic> Validate(PlateStripBeamAnalogy analogy)
    {
        ArgumentNullException.ThrowIfNull(analogy);
        var diagnostics = new List<FemValidationDiagnostic>();

        var points = Geometry?.Points;
        if (points == null || points.Count < 2)
            diagnostics.Add(new("plate_strip_boundary_geometry_invalid",
                $"Граница «{Id}»: нужна цепочка не менее чем из двух точек."));
        else if (points.Any(p => !p.IsFinite))
            diagnostics.Add(new("plate_strip_boundary_geometry_invalid",
                $"Граница «{Id}» содержит нечисловую координату."));
        else if (points.All(p => Math.Abs(p.U - points[0].U) < 1e-12 &&
                                 Math.Abs(p.V - points[0].V) < 1e-12))
            diagnostics.Add(new("plate_strip_boundary_geometry_invalid",
                $"Граница «{Id}» вырождена в точку."));

        var normal = NormalFromReplacedToRetained;
        if (!normal.IsFinite)
            diagnostics.Add(new("plate_strip_boundary_normal_invalid",
                $"Граница «{Id}»: нормаль содержит нечисловую компоненту."));
        else
        {
            double length = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
            if (length < 1e-9)
                diagnostics.Add(new("plate_strip_boundary_normal_invalid",
                    $"Граница «{Id}»: нормаль нулевая."));
            else if (Math.Abs(length - 1.0) > 1e-6)
                diagnostics.Add(new("plate_strip_boundary_normal_invalid",
                    $"Граница «{Id}»: нормаль не единичная (длина {length:G6})."));
            if (Math.Abs(normal.Z) > 1e-9)
                diagnostics.Add(new("plate_strip_boundary_normal_invalid",
                    $"Граница «{Id}»: нормаль обязана лежать в плоскости региона " +
                    $"(компонента вне плоскости {normal.Z:G6})."));
        }

        var modes = ModeByDof ?? PlanarBoundaryModeByDof.None;
        PlanarBoundaryDofMode[] values = [modes.UX, modes.UY, modes.UZ, modes.RX, modes.RY, modes.RZ];
        if (values.Any(m => m == PlanarBoundaryDofMode.Incomplete))
            diagnostics.Add(new("plate_strip_boundary_mode_incomplete",
                $"Граница «{Id}»: режим Incomplete недопустим — по родительской спеке это ошибка, " +
                "а не режим."));

        bool hasForceDof = values.Any(m => m == PlanarBoundaryDofMode.Force);
        if (hasForceDof && ForceAction == null)
            diagnostics.Add(new("plate_strip_boundary_force_action_missing",
                $"Граница «{Id}»: есть DOF в режиме Force, но ForceAction не задан — " +
                "интенсивность краевого действия взять неоткуда."));
        if (ForceAction != null)
            diagnostics.AddRange(ForceAction.Validate());

        return diagnostics;
    }

    /// <summary>Спроецировать границу на ось полосы. Возвращает истину только для границы,
    /// пересекающей ось поперёк: такая переносится точечной нагрузкой. Граница, идущая вдоль
    /// полосы, переносится распределённой и здесь даёт ложь.
    ///
    /// regionFrame обязателен: Geometry задана в координатах региона, а PlateStripBeamAnalogy
    /// несёт только StripFrame — ровно поэтому его принимает и StripLoadMapper.Map.</summary>
    public bool TryProjectToStation(
        Frame3D regionFrame, PlateStripBeamAnalogy analogy, out double stationFraction)
    {
        ArgumentNullException.ThrowIfNull(analogy);
        stationFraction = double.NaN;

        var points = Geometry?.Points;
        if (points == null || points.Count < 2) return false;
        double length = analogy.Geometry.LengthM;
        if (!(length > 0.0) || !double.IsFinite(length)) return false;

        var local = new List<(double X, double Y)>(points.Count);
        foreach (var point in points)
        {
            if (!point.IsFinite) return false;
            var global = PlanarBoundaryFrameConverter.ToGlobalPoint(
                regionFrame, new PlanarVector3(point.U, point.V, 0.0));
            var strip = PlanarBoundaryFrameConverter.ToLocalPoint(analogy.StripFrame, global);
            local.Add((strip.X, strip.Y));
        }

        for (int i = 0; i < local.Count - 1; i++)
        {
            var (x1, y1) = local[i];
            var (x2, y2) = local[i + 1];
            if (y1 == 0.0 && y2 == 0.0) continue;      // отрезок лежит на оси — это не пересечение
            if (y1 * y2 > 0.0) continue;                // одна сторона от оси

            double t = Math.Abs(y2 - y1) < 1e-15 ? 0.0 : y1 / (y1 - y2);
            double x = x1 + t * (x2 - x1);
            // Точка пересечения лежит на оси полосы (y = 0), то есть заведомо внутри
            // коридора; остаётся проверить только попадание в пролёт.
            if (x < -1e-9 || x > length + 1e-9) continue;

            stationFraction = Math.Clamp(x / length, 0.0, 1.0);
            return true;
        }
        return false;
    }
}
