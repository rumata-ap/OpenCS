namespace CScore.ParametricRc;

/// <summary>Консервативно ограничивает задачи для расчётных слоёв арматуры.</summary>
public static class ParametricRebarApplicability
{
    static readonly HashSet<string> AllowedKinds = ["strain_state", "strain_state_batch", "sp63_normal"];

    /// <summary>Возвращает машинный код неприменимости либо null.</summary>
    public static string? Reject(CrossSection section, string kind, LoadItem load,
        IdealizedRebarAxis? requestedAxis = null)
    {
        var axes = section.Areas.Where(a => a.RebarRepresentation == RebarRepresentation.IdealizedLayer)
            .Select(a => a.IdealizedAxis).Distinct().ToArray();
        if (axes.Length == 0) return null;
        if (!AllowedKinds.Contains(kind)) return "idealized_rebar_task_not_supported";
        if (axes.Length != 1 || axes[0] is null) return "idealized_rebar_axis_inconsistent";
        var axis = axes[0]!.Value;
        if (requestedAxis is not null && requestedAxis != axis) return "idealized_rebar_axis_mismatch";
        if (axis == IdealizedRebarAxis.Mx && Math.Abs(load.My) > 1e-9 ||
            axis == IdealizedRebarAxis.My && Math.Abs(load.Mx) > 1e-9)
            return "idealized_rebar_biaxial_load";
        return null;
    }
}
