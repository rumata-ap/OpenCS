using System.Text;

namespace CScore.Abaqus;

/// <summary>Сериализует сталь в keyword-блок Abaqus (*Elastic + *Plastic) и TSV.</summary>
public static class AbaqusSteelKeywordSerializer
{
    /// <summary>Формирует вставляемый в Abaqus keyword-блок.</summary>
    public static string ToKeyword(AbaqusSteelMaterialData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var builder = new StringBuilder();
        AppendLine(builder, $"*Material, name={AbaqusCdpKeywordSerializer.SanitizeMaterialName(data.MaterialName, "Steel-Plastic")}");
        AppendLine(builder, "*Elastic");
        AppendLine(builder, $"{Format(data.ElasticModulus)}, {Format(data.PoissonRatio)}");
        AppendLine(builder, "*Plastic");
        foreach (var point in data.Plastic)
            AppendLine(builder, $"{Format(point.Stress)}, {Format(point.PlasticStrain)}");
        return builder.ToString();
    }

    /// <summary>Формирует диагностический TSV-блок с явными секциями и заголовками.</summary>
    public static string ToTsv(AbaqusSteelMaterialData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var builder = new StringBuilder();
        AppendLine(builder, "[Elastic]");
        AppendLine(builder, "Modulus\tPoissonRatio");
        AppendLine(builder, $"{Format(data.ElasticModulus)}\t{Format(data.PoissonRatio)}");
        AppendLine(builder, "[Plastic]");
        AppendLine(builder, "TrueStress\tPlasticStrain");
        foreach (var point in data.Plastic)
            AppendLine(builder, $"{Format(point.Stress)}\t{Format(point.PlasticStrain)}");
        return builder.ToString();
    }

    static string Format(double value) => AbaqusCdpKeywordSerializer.Format(value);

    static void AppendLine(StringBuilder builder, string value) => builder.Append(value).Append("\r\n");
}
