using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests.Audit;

public sealed class ShellAuditReportTests
{
    [Fact]
    public void Resolve_BlockedPreflight_ReturnsBlocked()
    {
        var preflight = new ShellAuditPreflightResult(false,
            [new ShellDiagnostic(ShellDiagnosticCodes.StateCatalogProvenanceMissing,
                ShellDiagnosticSeverity.Blocking, "Нет catalog.")]);

        ShellAuditVerdict verdict = ShellAuditVerdictResolver.Resolve(
            preflight, [], ShellEnergyConfidence.ExternalWorkOnly, new ShellAuditPolicy(), sensitivity: null);

        Assert.Equal(ShellAuditVerdict.Blocked, verdict);
    }

    [Fact]
    public void AuditReportStoresTypedAuditOutputs()
    {
        var report = new ShellAuditReport(
            ShellAuditVerdict.Warning,
            new ShellAuditPreflightResult(true, []),
            [],
            ShellEnergyConfidence.ExternalWorkOnly,
            ExternalWork: 12.5,
            RegularizationApplied: false,
            SupportedRegularizationModes: [],
            Sensitivity: null,
            Diagnostics: []);

        Assert.Equal(ShellAuditVerdict.Warning, report.Verdict);
        Assert.Equal(12.5, report.ExternalWork, 12);
        Assert.False(report.RegularizationApplied);
    }

    [Fact]
    public void Resolve_EverythingPasses_ReturnsPassed()
    {
        ShellAuditVerdict verdict = ShellAuditVerdictResolver.Resolve(
            new ShellAuditPreflightResult(true, []),
            [PassingStep()],
            ShellEnergyConfidence.ExternalWorkOnly,
            new ShellAuditPolicy(),
            sensitivity: null);

        Assert.Equal(ShellAuditVerdict.Passed, verdict);
    }

    [Fact]
    public void Resolve_EquilibriumFailure_ReturnsWarning()
    {
        ShellAuditVerdict verdict = ShellAuditVerdictResolver.Resolve(
            new ShellAuditPreflightResult(true, []),
            [PassingStep() with { Pass = false }],
            ShellEnergyConfidence.ExternalWorkOnly,
            new ShellAuditPolicy(),
            sensitivity: null);

        Assert.Equal(ShellAuditVerdict.Warning, verdict);
    }

    [Fact]
    public void Resolve_EnergyBelowRequirement_ReturnsWarning()
    {
        ShellAuditVerdict verdict = ShellAuditVerdictResolver.Resolve(
            new ShellAuditPreflightResult(true, []),
            [PassingStep()],
            ShellEnergyConfidence.Unavailable,
            new ShellAuditPolicy
            {
                MinEnergyConfidence = ShellEnergyConfidenceRequirement.ExternalWorkOnly
            },
            sensitivity: null);

        Assert.Equal(ShellAuditVerdict.Warning, verdict);
    }

    [Fact]
    public void Resolve_MeshDependentSensitivity_ReturnsMeshDependent()
    {
        var sensitivity = new ShellMeshSensitivityReport([], 0.5, ShellAuditVerdict.MeshDependent, []);

        ShellAuditVerdict verdict = ShellAuditVerdictResolver.Resolve(
            new ShellAuditPreflightResult(true, []), [PassingStep()],
            ShellEnergyConfidence.ExternalWorkOnly, new ShellAuditPolicy(), sensitivity);

        Assert.Equal(ShellAuditVerdict.MeshDependent, verdict);
    }

    [Fact]
    public void Resolve_BlockedSensitivity_ReturnsBlocked()
    {
        var sensitivity = new ShellMeshSensitivityReport([], 0, ShellAuditVerdict.Blocked,
            [new ShellDiagnostic(ShellDiagnosticCodes.SensitivityCaseIncomplete,
                ShellDiagnosticSeverity.Blocking, "Мало запусков.")]);

        ShellAuditVerdict verdict = ShellAuditVerdictResolver.Resolve(
            new ShellAuditPreflightResult(true, []), [PassingStep()],
            ShellEnergyConfidence.ExternalWorkOnly, new ShellAuditPolicy(), sensitivity);

        Assert.Equal(ShellAuditVerdict.Blocked, verdict);
    }

    private static ShellEquilibriumStepReport PassingStep() => new(
        1, 0, 1.0,
        new ShellResultant(0, 0, -1000, 0, 0, 0),
        new ShellResultant(0, 0, 1000, 0, 0, 0),
        new ShellResultant(0, 0, 0, 0, 0, 0),
        AbsoluteError: 0,
        RelativeError: 0,
        Pass: true);
}
