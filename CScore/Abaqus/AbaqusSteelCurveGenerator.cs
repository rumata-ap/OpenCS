using CScore;

using CSmath;

namespace CScore.Abaqus;

/// <summary>
/// Строит таблицу *Plastic Abaqus из кусочно-линейной диаграммы стали OpenCS:
/// СП 16 для конструкционной стали, двухлинейную для арматуры с физическим
/// пределом текучести и трёхлинейную — с условным.
/// </summary>
public static class AbaqusSteelCurveGenerator
{
    /// <summary>Строит данные *Elastic + *Plastic из материала OpenCS.</summary>
    public static AbaqusSteelMaterialData Generate(Material material, AbaqusSteelOptions options)
    {
        ValidateOptions(options);
        var chars = ValidateMaterial(material, options);
        var diagram = chars.Type switch
        {
            MatType.Steel => chars.DSP16(options.HasYieldPlateau),
            MatType.ReSteelF => chars.D2L(),
            _ => chars.D3L()
        };

        double scale = options.UnitSystem.StressScaleFromOpenCsKpa;
        double elasticModulus = chars.E * scale;
        if (!double.IsFinite(elasticModulus) || elasticModulus <= 0.0)
            throw new ArgumentException("Модуль упругости после пересчёта должен быть конечным и положительным.",
                nameof(material));

        var warnings = new List<string>();
        var tension = (LSpline)diagram.It;
        var compression = (LSpline)diagram.Ic;

        // Первая точка ветви — начало координат, вторая — конец упругого участка (предел пропорциональности).
        var plastic = new List<AbaqusSteelPlasticPoint>();
        for (int i = 1; i < tension.X.Length; i++)
        {
            if (i > 1 && tension.Y[i] < tension.Y[i - 1])
            {
                warnings.Add("Нисходящая ветвь диаграммы отброшена: таблица *Plastic обрывается на σu, " +
                    "дальше Abaqus продолжает идеально пластически.");
                break;
            }

            double strain = tension.X[i];
            double stress = tension.Y[i] * scale;
            double trueStress = stress * (1.0 + strain);
            double plasticStrain = plastic.Count == 0
                ? 0.0
                : Math.Log(1.0 + strain) - trueStress / elasticModulus;
            plastic.Add(new AbaqusSteelPlasticPoint(trueStress, plasticStrain, strain, stress));
        }

        double tensionMax = tension.Y.Max(Math.Abs);
        double compressionMax = compression.Y.Max(Math.Abs);
        if (Math.Abs(tensionMax - compressionMax) > 1e-9 * tensionMax)
            warnings.Add("Прочность при сжатии и растяжении различается; *Plastic симметричен (Мизес), " +
                "экспортирована ветвь растяжения.");

        ValidatePlastic(plastic);
        return new AbaqusSteelMaterialData
        {
            MaterialName = AbaqusCdpKeywordSerializer.SanitizeMaterialName(
                string.IsNullOrWhiteSpace(options.MaterialName) ? material.Tag : options.MaterialName,
                "Steel-Plastic"),
            UnitSystem = options.UnitSystem,
            ElasticModulus = elasticModulus,
            PoissonRatio = options.PoissonRatio,
            Plastic = plastic,
            Warnings = warnings
        };
    }

    static void ValidateOptions(AbaqusSteelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.UnitSystem);

        if (!Enum.IsDefined(options.CalcType))
            throw new ArgumentException("Указан неизвестный вид расчёта.", nameof(options.CalcType));
        double scale = options.UnitSystem.StressScaleFromOpenCsKpa;
        if (!double.IsFinite(scale) || scale <= 0.0)
            throw new ArgumentException("Масштаб напряжений должен быть конечным и положительным.",
                nameof(options.UnitSystem));
        if (!double.IsFinite(options.PoissonRatio) || options.PoissonRatio <= 0.0 || options.PoissonRatio >= 0.5)
            throw new ArgumentException("Коэффициент Пуассона должен быть в диапазоне (0; 0.5).",
                nameof(options.PoissonRatio));
    }

    static MaterialChars ValidateMaterial(Material material, AbaqusSteelOptions options)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (material.Type is not (MatType.Steel or MatType.ReSteelF or MatType.ReSteelU))
            throw new ArgumentException("Экспорт *Plastic поддерживает только сталь и арматуру.", nameof(material));

        var chars = material.GetChars(options.CalcType)
            ?? throw new ArgumentException("Для выбранного вида расчёта нет характеристик материала.",
                nameof(material));
        if (chars.Type != material.Type)
            throw new ArgumentException("Тип характеристик выбранного расчёта не совпадает с типом материала.",
                nameof(material));
        if (!double.IsFinite(chars.E) || chars.E <= 0.0)
            throw new ArgumentException("Для экспорта нужен положительный модуль упругости.", nameof(material));

        return chars;
    }

    static void ValidatePlastic(IReadOnlyList<AbaqusSteelPlasticPoint> points)
    {
        if (points.Count == 0)
            throw new ArgumentException("Таблица *Plastic не может быть пустой.");

        double previous = double.NegativeInfinity;
        foreach (var point in points)
        {
            if (!double.IsFinite(point.Stress) || !double.IsFinite(point.PlasticStrain))
                throw new ArgumentException("Значения таблицы *Plastic должны быть конечными.");
            if (point.Stress <= 0.0)
                throw new ArgumentException("Напряжение текучести в *Plastic должно быть положительным.");
            if (point.PlasticStrain <= previous)
                throw new ArgumentException("Пластическая деформация в *Plastic должна строго возрастать.");
            previous = point.PlasticStrain;
        }
    }
}
