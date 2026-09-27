using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CScore.Sp16;

namespace CScore.ParametricSteel;

/// <summary>Устойчивый отпечаток сформированной геометрии параметрического стального сечения.</summary>
public static class ParametricSteelSectionFingerprint
{
    /// <summary>SHA-256 по материалу и контурам областей (без расчётного состояния и сетки).</summary>
    public static string Compute(CrossSection section, int generatorVersion)
    {
        ArgumentNullException.ThrowIfNull(section);
        var sb = new StringBuilder("parametric-steel|").Append(generatorVersion.ToString(CultureInfo.InvariantCulture));
        foreach (var area in section.Areas)
        {
            sb.Append("\narea|").Append(area.Category).Append('|').Append(area.MaterialId.ToString(CultureInfo.InvariantCulture));
            foreach (var contour in area.Contours)
            {
                sb.Append("\ncontour|").Append(contour.Type);
                for (int i = 0; i < Math.Min(contour.X.Count, contour.Y.Count); i++)
                    sb.Append("\np|").Append(F(contour.X[i])).Append('|').Append(F(contour.Y[i]));
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}

/// <summary>Привязка сечения к параметрическому стальному источнику (runtime, не сохраняется).</summary>
/// <param name="Profile">Явный профиль СП 16.</param>
/// <param name="Fingerprint">Отпечаток геометрии, для которой профиль действителен.</param>
/// <param name="GeneratorVersion">Версия генератора, которой вычислен отпечаток.</param>
public sealed record ParametricSteelBinding(SteelProfile Profile, string Fingerprint, int GeneratorVersion);

/// <summary>Доступ к профилю параметрического стального сечения.</summary>
public static class ParametricSteelSectionExtensions
{
    /// <summary>
    /// Профиль параметрического сечения, если текущая геометрия совпадает с привязкой; иначе null
    /// (сечение правили вручную — профиль распознаётся по контуру).
    /// </summary>
    public static SteelProfile? TryGetParametricSteelProfile(this CrossSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var binding = section.ParametricSteel;
        if (binding is null) return null;
        string actual = ParametricSteelSectionFingerprint.Compute(section, binding.GeneratorVersion);
        return string.Equals(actual, binding.Fingerprint, StringComparison.Ordinal) ? binding.Profile : null;
    }
}
