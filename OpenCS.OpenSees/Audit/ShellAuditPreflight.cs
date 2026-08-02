using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Audit;

/// <summary>Результат preflight: можно ли считать audit и какие диагностики обнаружены.</summary>
public sealed record ShellAuditPreflightResult(
    bool IsCalculable,
    IReadOnlyList<ShellDiagnostic> Diagnostics);

/// <summary>Preflight provenance catalog, regularization capability и обязательных response.</summary>
public static class ShellAuditPreflight
{
    /// <summary>Выполняет проверки до запуска OpenSees.</summary>
    public static ShellAuditPreflightResult Run(
        ShellOpenSeesModel model,
        ShellStateCatalog? catalog,
        ShellAuditPolicy policy,
        ShellRegularizationCapability regularization)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(regularization);

        var diagnostics = new List<ShellDiagnostic>();
        bool calculable = true;

        if (catalog is null)
        {
            diagnostics.Add(new ShellDiagnostic(
                ShellDiagnosticCodes.StateCatalogProvenanceMissing,
                ShellDiagnosticSeverity.Blocking,
                "Отсутствует material-state catalog — provenance недоступен."));
            calculable = false;
        }
        else if (catalog.ProvenanceKind == ShellStateCatalogProvenanceKind.V1LegacyMissing)
        {
            diagnostics.Add(new ShellDiagnostic(
                ShellDiagnosticCodes.StateCatalogProvenanceMissing,
                ShellDiagnosticSeverity.Blocking,
                "Material-state catalog v1 без provenance; строгий audit невозможен."));
            calculable = false;
        }

        if (policy.Regularization.Mode != ShellRegularizationMode.None &&
            !regularization.CanApply(policy.Regularization.Mode))
        {
            bool blocking = policy.Mode == ShellAuditMode.Strict;
            diagnostics.Add(new ShellDiagnostic(
                ShellDiagnosticCodes.RegularizationUnsupported,
                blocking ? ShellDiagnosticSeverity.Blocking : ShellDiagnosticSeverity.Warning,
                $"Режим regularization «{policy.Regularization.Mode}» не поддерживается native adapter-ом; " +
                "regularization_applied=false; результат не называется mesh-independent."));
            if (blocking) calculable = false;
        }

        foreach (string response in policy.RequiredResponses)
        {
            if (model.Materials.Any(material => !material.Spec.HasResponse(response)))
            {
                diagnostics.Add(new ShellDiagnostic(
                    ShellDiagnosticCodes.UnsupportedShellResponse,
                    ShellDiagnosticSeverity.Blocking,
                    $"Обязательный response «{response}» не поддерживается всеми материалами модели."));
                calculable = false;
            }
        }

        return new ShellAuditPreflightResult(calculable, diagnostics);
    }
}
