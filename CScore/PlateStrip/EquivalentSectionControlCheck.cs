using CScore.Fem;

namespace CScore.PlateStrip;

/// <summary>Результат внутренней алгебраической проверки самосогласованности редукции:
/// прямая интеграция плитных усилий по ширине против прогноза сохранённой касательной
/// EquivalentSection.Forces.</summary>
public sealed record EquivalentSectionControlCheckResult(
    bool IsCalculable,
    BeamStrainState BeamState,
    double[] Direct,
    double[] Predicted,
    double[] Residual,
    bool IsConsistent,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Сравнивает прямую интеграцию плитных усилий по ширине (StripResultantIntegrator)
/// с прогнозом сохранённой касательной EquivalentSection.Forces для произвольного состояния
/// деформации балки.</summary>
public static class EquivalentSectionControlCheck
{
    public static EquivalentSectionControlCheckResult Run(
        EquivalentSection? section,
        IReadOnlyList<IPlateSectionResponse>? widthSources,
        BeamStrainState beamState,
        double relativeTolerance = 1e-6,
        double absoluteTolerance = 1e-9)
    {
        if (section == null || !section.IsCalculable || section.Strip == null)
            return Fail("equivalent_section_control_missing_section",
                "Эквивалентное сечение не задано, не рассчитано или не содержит геометрию полосы.",
                beamState);
        if (widthSources == null || widthSources.Count != section.WidthIntegrationPoints)
            return Fail("equivalent_section_control_source_count_mismatch",
                "Число источников по ширине должно совпадать с числом точек интегрирования сечения.",
                beamState);
        if (!double.IsFinite(beamState.Eps0) || !double.IsFinite(beamState.KappaY) || !double.IsFinite(beamState.KappaZ))
            return Fail("equivalent_section_control_invalid_state",
                "Состояние деформации балки должно быть конечным.",
                beamState);
        if (!(relativeTolerance >= 0.0) || !double.IsFinite(relativeTolerance) ||
            !(absoluteTolerance >= 0.0) || !double.IsFinite(absoluteTolerance))
            return Fail("equivalent_section_control_invalid_tolerance",
                "Допуски проверки должны быть конечными и неотрицательными.",
                beamState);

        var direct = StripResultantIntegrator.Integrate(section.Strip.ExplicitWidthM, widthSources, beamState);
        var predicted = section.Forces(beamState);
        var residual = new double[3];
        bool consistent = true;
        for (int i = 0; i < 3; i++)
        {
            residual[i] = direct[i] - predicted[i];
            double scale = Math.Max(Math.Abs(direct[i]), Math.Abs(predicted[i]));
            if (Math.Abs(residual[i]) > absoluteTolerance + relativeTolerance * scale)
                consistent = false;
        }

        var diagnostics = new List<FemValidationDiagnostic>();
        if (!consistent)
            diagnostics.Add(new(
                "equivalent_section_control_residual_exceeded",
                "Невязка прямой интеграции и прогноза касательной превышает допуск.",
                false));

        return new(true, beamState, direct, predicted, residual, consistent, diagnostics);
    }

    static EquivalentSectionControlCheckResult Fail(string code, string message, BeamStrainState beamState) =>
        new(false, beamState, [], [], [], false, [new FemValidationDiagnostic(code, message)]);
}
