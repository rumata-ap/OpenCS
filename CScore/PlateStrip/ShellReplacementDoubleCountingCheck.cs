using CScore.Fem;

namespace CScore.PlateStrip;

public sealed record ShellReplacementCheckResult(
    bool IsCalculable,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Проверки двойного учёта между PlateStripBeamAnalogy и её shell-регионом источника —
/// чисто доменные диагностики над уже существующими данными (геометрия полосы Среза 1,
/// StripLoadSet Среза 4), без реальной сборки shell+beam (появится в Срезе 7). См.
/// docs/superpowers/specs/2026-08-13-plate-strip-shell-replacement-policy-design.md.</summary>
public static class ShellReplacementDoubleCountingCheck
{
    /// <summary>Сравнивает StripLoadSourceTags манифеста с явно переданным списком тегов, ещё
    /// активных на shell-регионе. Обязательная конвенция для корректности (см. спеку): если
    /// Surface-нагрузка покрывает регион шире коридора полосы, вызывающий код обязан задать её
    /// как две раздельные PlanarLoad с разными тегами — иначе пересечение множеств тегов не
    /// отличимо от легитимного частичного покрытия.</summary>
    public static ShellReplacementCheckResult CheckLoads(
        ShellReplacementManifest manifest,
        IReadOnlyList<string> loadsStillActiveOnShell)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(loadsStillActiveOnShell);

        var shellTags = new HashSet<string>(loadsStillActiveOnShell);
        var diagnostics = new List<FemValidationDiagnostic>();

        switch (manifest.Policy)
        {
            case ShellReplacementPolicy.ReplaceShellRegion:
                foreach (string tag in manifest.StripLoadSourceTags)
                {
                    if (shellTags.Contains(tag))
                        diagnostics.Add(new("plate_strip_shell_replacement_load_double_count",
                            $"Нагрузка «{tag}» полосы «{manifest.StripId}» с политикой ReplaceShellRegion " +
                            $"всё ещё активна на исходном shell-регионе {manifest.SourceRegionId} — двойной учёт."));
                }
                break;

            case ShellReplacementPolicy.DiagnosticOnly:
                foreach (string tag in manifest.StripLoadSourceTags)
                {
                    if (!shellTags.Contains(tag))
                        diagnostics.Add(new("plate_strip_shell_replacement_diagnostic_incomplete",
                            $"Нагрузка «{tag}» полосы «{manifest.StripId}» с политикой DiagnosticOnly " +
                            $"отсутствует на исходном shell-регионе {manifest.SourceRegionId} — shell перестал " +
                            "быть единственным владельцем без объявленной ReplaceShellRegion."));
                }
                break;
        }

        return new(diagnostics.All(d => !d.IsError), diagnostics);
    }
}
