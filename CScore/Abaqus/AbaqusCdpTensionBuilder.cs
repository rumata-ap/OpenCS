using CScore;

namespace CScore.Abaqus;

/// <summary>
/// Строит CDP-таблицы растяжения по кривой StructureHelper.
/// Формулы адаптированы из ConcreteToCDPTensionConvertStrategy.cs;
/// истинная cracking strain и правила Abaqus добавлены в OpenCS.
/// </summary>
public static class AbaqusCdpTensionBuilder
{
    /// <summary>Строит таблицу растяжения для выбранного вида расчёта.</summary>
    public static IReadOnlyList<AbaqusCdpCurvePoint> Build(
        Material material,
        AbaqusCdpOptions options)
    {
        AbaqusCdpValidator.ValidateOptions(options);
        var chars = AbaqusCdpValidator.ValidateMaterial(material, options);
        double elasticModulus = 1.05 * chars.E * options.UnitSystem.StressScaleFromOpenCsKpa;
        double tensileStrength = Math.Abs(chars.Ft) * options.UnitSystem.StressScaleFromOpenCsKpa;
        return Build(tensileStrength, elasticModulus, options);
    }

    /// <summary>Строит таблицу растяжения в единицах Abaqus.</summary>
    internal static IReadOnlyList<AbaqusCdpCurvePoint> Build(
        double tensileStrength,
        double elasticModulus,
        AbaqusCdpOptions options)
    {
        if (!double.IsFinite(tensileStrength) || tensileStrength <= 0.0 ||
            !double.IsFinite(elasticModulus) || elasticModulus <= 0.0)
            throw new ArgumentException("Модуль и прочность после пересчёта должны быть конечными и положительными.",
                nameof(tensileStrength));
        double wu = 5.14 * options.FractureEnergy / tensileStrength;
        var result = new List<AbaqusCdpCurvePoint>(41);

        for (int i = 0; i <= 40; i++)
        {
            double crackWidth = wu * i / 40.0;
            double ratio = crackWidth / wu;
            double stress = tensileStrength *
                (1.0 + Math.Pow(3.0 * ratio, 3.0)) * Math.Exp(-6.93 * ratio);
            if (i == 0)
                stress = tensileStrength;

            double totalStrain = tensileStrength / elasticModulus +
                crackWidth / options.ElementLength;
            double elasticStrain = stress / elasticModulus;
            double crackingStrain = Math.Max(0.0, totalStrain - elasticStrain);
            double damage = Math.Clamp(1.0 - stress / tensileStrength, 0.0, 0.999);
            double plasticStrain = damage <= 0.0
                ? crackingStrain
                : Math.Max(0.0, crackingStrain -
                    damage * stress / ((1.0 - damage) * elasticModulus));

            result.Add(new AbaqusCdpCurvePoint(
                stress,
                totalStrain,
                i == 0 ? 0.0 : crackingStrain,
                damage,
                i == 0 ? 0.0 : plasticStrain,
                elasticStrain));
        }

        AbaqusCdpValidator.ValidateCurve(result, nameof(tensileStrength));
        return result;
    }
}
