namespace OpenCS.OpenSees.Audit;

/// <summary>Типизированный отчёт audit shell-модели: verdict, preflight, равновесие,
/// energy, regularization, sensitivity и diagnostics.</summary>
public sealed record ShellAuditReport(
    ShellAuditVerdict Verdict,
    ShellAuditPreflightResult Preflight,
    IReadOnlyList<ShellEquilibriumStepReport> EquilibriumSteps,
    ShellEnergyConfidence EnergyConfidence,
    double ExternalWork,
    bool RegularizationApplied,
    IReadOnlyList<ShellRegularizationMode> SupportedRegularizationModes,
    ShellMeshSensitivityReport? Sensitivity,
    IReadOnlyList<ShellDiagnostic> Diagnostics);

/// <summary>Собирает итоговый verdict из preflight, равновесия, energy и sensitivity.</summary>
public static class ShellAuditVerdictResolver
{
    /// <summary>Возвращает Blocked для нерасчётного preflight/sensitivity, Warning для
    /// неравновесия или недостаточного energy confidence, иначе Passed.</summary>
    public static ShellAuditVerdict Resolve(
        ShellAuditPreflightResult preflight,
        IReadOnlyList<ShellEquilibriumStepReport> equilibriumSteps,
        ShellEnergyConfidence energyConfidence,
        ShellAuditPolicy policy,
        ShellMeshSensitivityReport? sensitivity)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        ArgumentNullException.ThrowIfNull(equilibriumSteps);
        ArgumentNullException.ThrowIfNull(policy);

        if (!preflight.IsCalculable)
            return ShellAuditVerdict.Blocked;
        if (sensitivity is not null && sensitivity.Verdict == ShellAuditVerdict.Blocked)
            return ShellAuditVerdict.Blocked;
        if (equilibriumSteps.Any(step => !step.Pass))
            return ShellAuditVerdict.Warning;
        if ((int)energyConfidence > (int)policy.MinEnergyConfidence)
            return ShellAuditVerdict.Warning;
        if (sensitivity is not null && sensitivity.Verdict == ShellAuditVerdict.MeshDependent)
            return ShellAuditVerdict.MeshDependent;
        return ShellAuditVerdict.Passed;
    }
}
