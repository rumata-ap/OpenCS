using CScore.Fem;

namespace CScore.Planar;

/// <summary>Общие проверки normalized boundary actions.</summary>
public static class PlanarBoundaryActionValidation
{
    /// <summary>Проверяет силовое действие.</summary>
    public static IReadOnlyList<FemValidationDiagnostic> Validate(PlanarBoundaryForceAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var diagnostics = ValidateCommon(action.InterfaceId, action.DofMask, action.Frame, action.UnitSystem, action.Samples.Count);
        ValidateSamples(action.Samples, action.ReferencePoint, diagnostics, force: true);
        return diagnostics;
    }

    /// <summary>Проверяет кинематическое действие.</summary>
    public static IReadOnlyList<FemValidationDiagnostic> Validate(PlanarBoundaryKinematicAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var diagnostics = ValidateCommon(action.InterfaceId, action.DofMask, action.Frame, action.UnitSystem, action.Samples.Count);
        ValidateSamples(action.Samples, PlanarVector3.Zero, diagnostics, force: false);
        return diagnostics;
    }

    static List<FemValidationDiagnostic> ValidateCommon(
        string interfaceId,
        PlanarDofMask dofMask,
        Frame3D frame,
        PlanarBoundaryUnitSystem units,
        int sampleCount)
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        if (string.IsNullOrWhiteSpace(interfaceId))
            diagnostics.Add(new("planar_boundary_interface_id_missing", "У boundary action не задан InterfaceId."));
        if (dofMask == PlanarDofMask.None)
            diagnostics.Add(new("planar_boundary_dof_missing", "У boundary action не задана маска DOF."));
        if (sampleCount == 0)
            diagnostics.Add(new("planar_boundary_samples_missing", "Boundary action не содержит samples."));
        if (units != PlanarBoundaryUnitSystem.Si)
            diagnostics.Add(new("planar_boundary_units_unsupported", $"Единицы boundary action {units} не поддерживаются."));
        try { frame.Validate(); }
        catch (InvalidOperationException ex) { diagnostics.Add(new("planar_boundary_frame_invalid", ex.Message)); }
        return diagnostics;
    }

    static void ValidateSamples<T>(
        IReadOnlyList<T> samples,
        PlanarVector3 referencePoint,
        ICollection<FemValidationDiagnostic> diagnostics,
        bool force)
        where T : notnull
    {
        if (!referencePoint.IsFinite)
            diagnostics.Add(new("planar_boundary_reference_point_invalid", "Reference point boundary action содержит нечисловую компоненту."));

        double previousS = double.NegativeInfinity;
        foreach (T sample in samples)
        {
            double s;
            PlanarVector3 first;
            PlanarVector3 second;
            if (sample is PlanarBoundaryForceSample forceSample)
            {
                s = forceSample.S;
                first = forceSample.ForcePerLength;
                second = forceSample.MomentPerLength;
            }
            else
            {
                var kinematicSample = (PlanarBoundaryKinematicSample)(object)sample;
                s = kinematicSample.S;
                first = kinematicSample.Displacement;
                second = kinematicSample.Rotation;
            }

            if (!double.IsFinite(s) || s < 0 || s > 1)
                diagnostics.Add(new("planar_boundary_sample_s_invalid", $"Координата sample s={s:G17} должна находиться в [0,1]."));
            else if (s <= previousS)
                diagnostics.Add(new("planar_boundary_samples_not_ordered", "Samples boundary action должны иметь строго возрастающий s."));
            previousS = s;

            if (!first.IsFinite || !second.IsFinite)
                diagnostics.Add(new("planar_boundary_sample_vector_invalid", force
                    ? "Force sample содержит нечисловую силу или момент."
                    : "Kinematic sample содержит нечисловое перемещение или вращение."));
        }
    }
}
