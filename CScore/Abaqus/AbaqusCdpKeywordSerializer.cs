using System.Globalization;
using System.Text;

namespace CScore.Abaqus;

/// <summary>Сериализует CDP безразмерные таблицы в формат keyword и TSV.</summary>
public static class AbaqusCdpKeywordSerializer
{
    /// <summary>Формирует вставляемый в Abaqus keyword-блок.</summary>
    public static string ToKeyword(AbaqusCdpMaterialData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var builder = new StringBuilder();
        AppendLine(builder, $"*Material, name={SanitizeMaterialName(data.MaterialName)}");
        AppendLine(builder, "*Elastic");
        AppendLine(builder, $"{Format(data.ElasticModulus)}, {Format(data.PoissonRatio)}");
        AppendLine(builder, "*Concrete Damaged Plasticity");
        AppendLine(builder, $"{Format(data.DilationAngleDegrees)}, {Format(data.Eccentricity)}, " +
            $"{Format(data.Fb0Fc0)}, {Format(data.Kc)}, {Format(data.Viscosity)}");
        AppendLine(builder, "*Concrete Compression Hardening");
        foreach (var point in data.Compression)
            AppendLine(builder, $"{Format(point.Stress)}, {Format(point.AbaqusStrain)}");
        AppendLine(builder, "*Concrete Compression Damage");
        foreach (var point in data.Compression)
            AppendLine(builder, $"{Format(point.Damage)}, {Format(point.AbaqusStrain)}");
        AppendLine(builder, "*Concrete Tension Stiffening, type=STRAIN");
        foreach (var point in data.Tension)
            AppendLine(builder, $"{Format(point.Stress)}, {Format(point.AbaqusStrain)}");
        AppendLine(builder, "*Concrete Tension Damage, type=STRAIN");
        foreach (var point in data.Tension)
            AppendLine(builder, $"{Format(point.Damage)}, {Format(point.AbaqusStrain)}");
        return builder.ToString();
    }

    /// <summary>Формирует диагностический TSV-блок с явными секциями и заголовками.</summary>
    public static string ToTsv(AbaqusCdpMaterialData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var builder = new StringBuilder();
        AppendLine(builder, "[Elastic]");
        AppendLine(builder, "Modulus\tPoissonRatio");
        AppendLine(builder, $"{Format(data.ElasticModulus)}\t{Format(data.PoissonRatio)}");
        AppendLine(builder, "[Concrete Damaged Plasticity]");
        AppendLine(builder, "DilationAngleDegrees\tEccentricity\tFb0Fc0\tKc\tViscosity");
        AppendLine(builder, $"{Format(data.DilationAngleDegrees)}\t{Format(data.Eccentricity)}\t" +
            $"{Format(data.Fb0Fc0)}\t{Format(data.Kc)}\t{Format(data.Viscosity)}");
        AppendLine(builder, "[Compression Hardening]");
        AppendLine(builder, "Stress\tInelasticStrain");
        foreach (var point in data.Compression)
            AppendLine(builder, $"{Format(point.Stress)}\t{Format(point.AbaqusStrain)}");
        AppendLine(builder, "[Compression Damage]");
        AppendLine(builder, "Damage\tInelasticStrain");
        foreach (var point in data.Compression)
            AppendLine(builder, $"{Format(point.Damage)}\t{Format(point.AbaqusStrain)}");
        AppendLine(builder, "[Tension Stiffening]");
        AppendLine(builder, "Stress\tCrackingStrain");
        foreach (var point in data.Tension)
            AppendLine(builder, $"{Format(point.Stress)}\t{Format(point.AbaqusStrain)}");
        AppendLine(builder, "[Tension Damage]");
        AppendLine(builder, "Damage\tCrackingStrain");
        foreach (var point in data.Tension)
            AppendLine(builder, $"{Format(point.Damage)}\t{Format(point.AbaqusStrain)}");
        return builder.ToString();
    }

    /// <summary>Очищает имя материала до безопасного для keyword идентификатора.</summary>
    public static string SanitizeMaterialName(string? name) => SanitizeMaterialName(name, "Concrete-CDP");

    /// <summary>Очищает имя материала; пустой результат заменяется на <paramref name="fallback"/>.</summary>
    internal static string SanitizeMaterialName(string? name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
            return fallback;

        var builder = new StringBuilder(name.Length);
        bool previousUnderscore = false;
        foreach (char character in name.Trim())
        {
            bool allowed = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or
                >= '0' and <= '9' or '_' or '-' or '.';
            char normalized = allowed ? character : '_';
            if (normalized == '_')
            {
                if (previousUnderscore)
                    continue;
                previousUnderscore = true;
            }
            else
            {
                previousUnderscore = false;
            }
            builder.Append(normalized);
        }

        string result = builder.ToString().Trim('_');
        return result.Length == 0 ? fallback : result;
    }

    internal static string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    static void AppendLine(StringBuilder builder, string value) => builder.Append(value).Append("\r\n");
}
