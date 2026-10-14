using System.Globalization;
using System.Text;
using CScore;
using CSfea.Torsion;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Источник автоматически разрешённого значения GJ.</summary>
public enum FemGjValueSource
{
    /// <summary>Оценка по геометрии сечения и материалу.</summary>
    SectionEstimate,

    /// <summary>Пользовательское глобальное значение по умолчанию.</summary>
    GlobalDefault,

    /// <summary>Встроенное значение, применённое вместо некорректной настройки.</summary>
    BuiltInFallback
}

/// <summary>Результат разрешения крутильной жёсткости стержня.</summary>
public sealed record FemGjResolution(double GjNm2, FemGjValueSource Source, string? Diagnostic);

/// <summary>
/// Разрешает GJ для нового стержня: оценивает его по сечению или возвращает
/// глобальное резервное значение. Кэш оценки принадлежит экземпляру resolver-а.
/// </summary>
public sealed class FemGjDefaultResolver
{
    const double DefaultElementSizeM = 0.05;

    readonly Func<CalcSettings> _settingsProvider;
    readonly Func<TorsionBoundary, TorsionProps> _estimator;
    readonly Dictionary<string, double> _sectionCache = new(StringComparer.Ordinal);

    /// <summary>Создаёт resolver с production-оценкой через один FEM-прогон.</summary>
    public FemGjDefaultResolver(
        Func<CalcSettings> settingsProvider,
        Func<TorsionBoundary, TorsionProps>? estimator = null)
    {
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _estimator = estimator ?? (boundary => TorsionSolver.Solve(
            boundary,
            TorsionMethod.Fem,
            DefaultElementSizeM,
            CSTriangulation.TriangulationMethod.AdvancingFront,
            FemElementOrder.Linear));
    }

    /// <summary>Возвращает GJ в Н·м² и источник полученного значения.</summary>
    public FemGjResolution Resolve(CrossSection? section)
    {
        var settings = _settingsProvider() ?? new CalcSettings();
        bool hasValidGlobalDefault = IsPositiveFinite(settings.OpenSeesDefaultGjKnm2);
        double fallbackKnm2 = hasValidGlobalDefault
            ? settings.OpenSeesDefaultGjKnm2
            : CalcSettings.DefaultOpenSeesGjKnm2;
        var fallback = new FemGjResolution(
            fallbackKnm2 * 1000.0,
            hasValidGlobalDefault ? FemGjValueSource.GlobalDefault : FemGjValueSource.BuiltInFallback,
            hasValidGlobalDefault ? null : "invalid_global_default");

        if (!settings.OpenSeesAutoGjFromSection)
            return fallback with { Diagnostic = "auto_section_disabled" };
        if (section?.Areas is not { Count: > 0 })
            return fallback with { Diagnostic = "section_missing" };

        try
        {
            var area = section.Areas[0];
            var baseMaterial = TorsionMaterialHelper.ResolveBaseMaterial(section);
            if (baseMaterial is null)
                return fallback with { Diagnostic = "material_missing" };
            double shearModulusMpa = TorsionMaterialHelper.ShearModulusMpa(baseMaterial);
            if (!IsPositiveFinite(shearModulusMpa))
                return fallback with { Diagnostic = "material_missing" };

            string cacheKey = FemGjSectionFingerprint.Compute(section, baseMaterial);
            if (!_sectionCache.TryGetValue(cacheKey, out var gj))
            {
                var boundary = area.FromMaterialArea();
                var props = _estimator(boundary);
                gj = shearModulusMpa * 1e6 * props.It;
                if (!IsPositiveFinite(gj))
                    return fallback with { Diagnostic = "section_estimation_invalid" };
                _sectionCache[cacheKey] = gj;
            }

            return new FemGjResolution(gj, FemGjValueSource.SectionEstimate, null);
        }
        catch (Exception)
        {
            return fallback with { Diagnostic = "section_estimation_failed" };
        }
    }

    static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;
}

/// <summary>Формирует стабильный ключ кэша оценки GJ по сечению.</summary>
static class FemGjSectionFingerprint
{
    public static string Compute(CrossSection section, Material material)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(section.Id).Append('|')
            .Append(material.Type).Append('|')
            .Append(material.E.ToString("R", inv));

        for (int areaIndex = 0; areaIndex < section.Areas.Count; areaIndex++)
        {
            var area = section.Areas[areaIndex];
            sb.Append("|a").Append(areaIndex).Append(':').Append(area.Category);
            for (int contourIndex = 0; contourIndex < area.Contours.Count; contourIndex++)
            {
                var contour = area.Contours[contourIndex];
                sb.Append("|c").Append(contourIndex).Append(':').Append(contour.Type);
                int count = Math.Min(contour.X.Count, contour.Y.Count);
                for (int pointIndex = 0; pointIndex < count; pointIndex++)
                {
                    sb.Append(';')
                        .Append(contour.X[pointIndex].ToString("R", inv))
                        .Append(',')
                        .Append(contour.Y[pointIndex].ToString("R", inv));
                }
            }
        }

        return sb.ToString();
    }
}
