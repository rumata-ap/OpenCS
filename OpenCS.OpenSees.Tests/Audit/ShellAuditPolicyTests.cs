using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tests.Fixtures;

namespace OpenCS.OpenSees.Tests.Audit;

public sealed class ShellAuditPolicyTests
{
    [Fact]
    public void AuditPolicy_DefaultsToDiagnosticOnlyWithExternalWorkEnergy()
    {
        var policy = new ShellAuditPolicy();

        Assert.Equal(ShellAuditMode.DiagnosticOnly, policy.Mode);
        Assert.Equal(["stress", "strain"], policy.RequiredResponses);
        Assert.Equal(ShellEnergyConfidenceRequirement.ExternalWorkOnly, policy.MinEnergyConfidence);
        Assert.Equal(ShellRegularizationMode.None, policy.Regularization.Mode);
        Assert.Equal(3, policy.SensitivityLevels.Count);
    }

    [Fact]
    public void AuditPolicy_FingerprintChangesWhenRegularizationChanges()
    {
        var none = new ShellAuditPolicy
        {
            Regularization = new ShellRegularizationPolicy { Mode = ShellRegularizationMode.None }
        };
        var crackBand = none with
        {
            Regularization = new ShellRegularizationPolicy { Mode = ShellRegularizationMode.CrackBand }
        };

        Assert.NotEqual(none.Fingerprint, crackBand.Fingerprint);
        Assert.NotEqual("", none.Fingerprint);
        Assert.NotEqual("", crackBand.Fingerprint);
    }

    [Fact]
    public void Preflight_StrictCrackBand_WithoutAdapter_BlocksWithRegularizationUnsupported()
    {
        var policy = new ShellAuditPolicy
        {
            Mode = ShellAuditMode.Strict,
            Regularization = new ShellRegularizationPolicy { Mode = ShellRegularizationMode.CrackBand }
        };

        ShellAuditPreflightResult result = ShellAuditPreflight.Run(
            ShellModelFixtures.Q4Elastic(), V2Catalog(), policy, new ShellRegularizationCapability([]));

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ShellDiagnosticCodes.RegularizationUnsupported &&
            diagnostic.Severity == ShellDiagnosticSeverity.Blocking);
    }

    [Fact]
    public void Preflight_DiagnosticOnlyCrackBand_WithoutAdapter_WarnsButCalculable()
    {
        var policy = new ShellAuditPolicy
        {
            Mode = ShellAuditMode.DiagnosticOnly,
            Regularization = new ShellRegularizationPolicy { Mode = ShellRegularizationMode.CrackBand }
        };

        ShellAuditPreflightResult result = ShellAuditPreflight.Run(
            ShellModelFixtures.Q4Elastic(), V2Catalog(), policy, new ShellRegularizationCapability([]));

        Assert.True(result.IsCalculable);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ShellDiagnosticCodes.RegularizationUnsupported &&
            diagnostic.Severity == ShellDiagnosticSeverity.Warning);
    }

    [Fact]
    public void Preflight_V1LegacyCatalog_BlocksWithProvenanceMissing()
    {
        var policy = new ShellAuditPolicy { Mode = ShellAuditMode.Strict };
        var legacyCatalog = new ShellStateCatalog(1, [], [], []);

        ShellAuditPreflightResult result = ShellAuditPreflight.Run(
            ShellModelFixtures.Q4Elastic(), legacyCatalog, policy, new ShellRegularizationCapability([]));

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ShellDiagnosticCodes.StateCatalogProvenanceMissing);
    }

    [Fact]
    public void Preflight_MissingCatalog_BlocksWithProvenanceMissing()
    {
        var result = ShellAuditPreflight.Run(
            ShellModelFixtures.Q4Elastic(), null, new ShellAuditPolicy(), new ShellRegularizationCapability([]));

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ShellDiagnosticCodes.StateCatalogProvenanceMissing);
    }

    [Fact]
    public void Diagnostic_CarriesCodeSeverityMessageAndOptionalContext()
    {
        var diagnostic = new ShellDiagnostic(
            ShellDiagnosticCodes.RecordingSelectionInvalid, ShellDiagnosticSeverity.Blocking,
            "Некорректный выбор IP.", ElementTag: 10, IntegrationPoint: 4);

        Assert.Equal("recording_selection_invalid", diagnostic.Code);
        Assert.Equal(ShellDiagnosticSeverity.Blocking, diagnostic.Severity);
        Assert.Equal(10, diagnostic.ElementTag);
        Assert.Equal(4, diagnostic.IntegrationPoint);
    }

    private static ShellStateCatalog V2Catalog() => new(2, [], [], []);
}
