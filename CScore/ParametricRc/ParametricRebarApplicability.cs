using CScore.Sp63.Normal;

namespace CScore.ParametricRc;

/// <summary>Типизированная причина, по которой расчётный слой неприменим.</summary>
public enum ParametricRebarApplicabilityReason
{
    None,
    UnsupportedTaskKind,
    InconsistentAxis,
    AxisMismatch,
    BiaxialLoad
}

/// <summary>Результат проверки применимости параметрического слоя.</summary>
public sealed record ParametricRebarApplicabilityResult(
    bool IsApplicable,
    ParametricRebarApplicabilityReason Reason,
    IdealizedRebarAxis? AllowedAxis)
{
    /// <summary>Стабильный код для JSON результата расчёта.</summary>
    public string? ReasonCode => Reason switch
    {
        ParametricRebarApplicabilityReason.UnsupportedTaskKind => "idealized_rebar_task_not_supported",
        ParametricRebarApplicabilityReason.InconsistentAxis => "idealized_rebar_axis_inconsistent",
        ParametricRebarApplicabilityReason.AxisMismatch => "idealized_rebar_axis_mismatch",
        ParametricRebarApplicabilityReason.BiaxialLoad => "idealized_rebar_biaxial_load",
        _ => null
    };

    /// <summary>Псевдоним для интеграций, использующих короткое имя кода причины.</summary>
    public string? Code => ReasonCode;
}

/// <summary>Консервативно ограничивает задачи для расчётных слоёв арматуры.</summary>
public static class ParametricRebarApplicability
{
    static readonly HashSet<string> AllowedKinds =
        ["strain_state", "strain_state_batch", "sp63_normal"];

    /// <summary>Проверяет только уровень вида задачи, без анализа нагрузки.</summary>
    public static bool IsSupportedTaskKind(string kind) => AllowedKinds.Contains(kind);

    /// <summary>Проверяет, содержит ли сечение хотя бы один расчётный слой.</summary>
    public static bool HasIdealizedLayer(CrossSection section) => section.Areas.Any(
        a => a.RebarRepresentation == RebarRepresentation.IdealizedLayer);

    /// <summary>Проверяет применимость к конкретной нагрузке.</summary>
    public static ParametricRebarApplicabilityResult Evaluate(
        CrossSection section, string kind, LoadItem load)
        => Evaluate(section, kind, load, null);

    /// <summary>Проверяет применимость к нагрузке с явно разобранной осью СП 63.</summary>
    public static ParametricRebarApplicabilityResult Evaluate(
        CrossSection section, string kind, LoadItem load, Sp63NormalAxis? requestedAxis)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(load);

        var axes = section.Areas
            .Where(a => a.RebarRepresentation == RebarRepresentation.IdealizedLayer)
            .Select(a => a.IdealizedAxis)
            .Distinct()
            .ToArray();
        if (axes.Length == 0)
            return new(true, ParametricRebarApplicabilityReason.None, null);
        if (!AllowedKinds.Contains(kind))
            return Failure(ParametricRebarApplicabilityReason.UnsupportedTaskKind, axes);
        if (axes.Length != 1 || axes[0] is null)
            return Failure(ParametricRebarApplicabilityReason.InconsistentAxis, axes);

        var axis = axes[0]!.Value;
        if (requestedAxis is not null &&
            ((requestedAxis == Sp63NormalAxis.Mx && axis != IdealizedRebarAxis.Mx) ||
             (requestedAxis == Sp63NormalAxis.My && axis != IdealizedRebarAxis.My)))
            return Failure(ParametricRebarApplicabilityReason.AxisMismatch, axes);

        if (axis == IdealizedRebarAxis.Mx && Math.Abs(load.My) > 1e-9 ||
            axis == IdealizedRebarAxis.My && Math.Abs(load.Mx) > 1e-9)
            return Failure(ParametricRebarApplicabilityReason.BiaxialLoad, axes);

        return new(true, ParametricRebarApplicabilityReason.None, axis);
    }

    /// <summary>Возвращает прежний строковый API для обработчиков задач.</summary>
    public static string? Reject(CrossSection section, string kind, LoadItem load,
        IdealizedRebarAxis? requestedAxis = null)
        => Evaluate(section, kind, load, requestedAxis is IdealizedRebarAxis.Mx
            ? Sp63NormalAxis.Mx
            : requestedAxis is IdealizedRebarAxis.My ? Sp63NormalAxis.My : null)
            .ReasonCode;

    static ParametricRebarApplicabilityResult Failure(
        ParametricRebarApplicabilityReason reason, IdealizedRebarAxis?[] axes)
        => new(false, reason, axes.Length == 1 ? axes[0] : null);
}
