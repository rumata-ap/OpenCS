using CScore;

namespace CScore.Abaqus;

/// <summary>Собирает параметры CDP и две кривые в единый экспортируемый набор.</summary>
public static class AbaqusCdpCurveGenerator
{
    /// <summary>Строит полный набор данных Abaqus CDP из материала OpenCS.</summary>
    public static AbaqusCdpMaterialData Generate(Material material, AbaqusCdpOptions options)
    {
        AbaqusCdpValidator.ValidateOptions(options);
        var chars = AbaqusCdpValidator.ValidateMaterial(material, options);
        double elasticModulus = 1.05 * chars.E * options.UnitSystem.StressScaleFromOpenCsKpa;
        if (!double.IsFinite(elasticModulus) || elasticModulus <= 0.0)
            throw new ArgumentException("Начальный модуль после пересчёта должен быть конечным и положительным.",
                nameof(material));

        var compression = AbaqusCdpCompressionBuilder.Build(chars, options, elasticModulus);
        double tensileStrength = Math.Abs(chars.Ft) * options.UnitSystem.StressScaleFromOpenCsKpa;
        var tension = AbaqusCdpTensionBuilder.Build(tensileStrength, elasticModulus, options);

        var result = new AbaqusCdpMaterialData
        {
            MaterialName = AbaqusCdpKeywordSerializer.SanitizeMaterialName(
                string.IsNullOrWhiteSpace(options.MaterialName) ? material.Tag : options.MaterialName),
            UnitSystem = options.UnitSystem,
            ElasticModulus = elasticModulus,
            PoissonRatio = options.PoissonRatio,
            DilationAngleDegrees = options.DilationAngleDegrees,
            Eccentricity = options.Eccentricity,
            Fb0Fc0 = options.Fb0Fc0,
            Kc = options.Kc,
            Viscosity = options.Viscosity,
            Compression = compression,
            Tension = tension
        };

        AbaqusCdpValidator.ValidateCurve(result.Compression, nameof(result.Compression));
        AbaqusCdpValidator.ValidateCurve(result.Tension, nameof(result.Tension));
        return result;
    }
}
