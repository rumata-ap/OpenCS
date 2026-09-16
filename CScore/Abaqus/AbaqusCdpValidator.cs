using CScore;

namespace CScore.Abaqus;

/// <summary>Проверяет входные данные и инварианты экспортируемого Abaqus CDP.</summary>
public static class AbaqusCdpValidator
{
    /// <summary>Проверяет настройки экспорта.</summary>
    public static void ValidateOptions(AbaqusCdpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.UnitSystem);

        RequireFinite(options.DilationAngleDegrees, nameof(options.DilationAngleDegrees));
        RequireFinite(options.Eccentricity, nameof(options.Eccentricity));
        RequireFinite(options.Fb0Fc0, nameof(options.Fb0Fc0));
        RequireFinite(options.Kc, nameof(options.Kc));
        RequireFinite(options.Viscosity, nameof(options.Viscosity));
        RequireFinite(options.PoissonRatio, nameof(options.PoissonRatio));
        RequireFinite(options.FractureEnergy, nameof(options.FractureEnergy));
        RequireFinite(options.ElementLength, nameof(options.ElementLength));
        RequireFinite(options.InitialCompressionStressRatio,
            nameof(options.InitialCompressionStressRatio));
        RequireFinite(options.CompressionEtaMin, nameof(options.CompressionEtaMin));
        RequireFinite(options.UnitSystem.StressScaleFromOpenCsKpa,
            nameof(options.UnitSystem.StressScaleFromOpenCsKpa));

        if (!Enum.IsDefined(options.CalcType))
            throw new ArgumentException("Указан неизвестный вид расчёта.", nameof(options.CalcType));
        if (!Enum.IsDefined(options.UnitSystem.Profile))
            throw new ArgumentException("Указан неизвестный профиль единиц.", nameof(options.UnitSystem));
        if (options.UnitSystem.Profile == AbaqusCdpUnitProfile.Custom &&
            (string.IsNullOrWhiteSpace(options.UnitSystem.StressUnit) ||
             string.IsNullOrWhiteSpace(options.UnitSystem.LengthUnit) ||
             string.IsNullOrWhiteSpace(options.UnitSystem.ForceUnit)))
            throw new ArgumentException("Для пользовательской системы единиц нужны все подписи единиц.",
                nameof(options.UnitSystem));
        if (options.UnitSystem.StressScaleFromOpenCsKpa <= 0.0)
            throw new ArgumentException("Масштаб напряжений должен быть положительным.",
                nameof(options.UnitSystem));
        if (options.DilationAngleDegrees <= 0.0 || options.DilationAngleDegrees >= 90.0)
            throw new ArgumentException("Угол дилатации должен быть в диапазоне (0; 90).",
                nameof(options.DilationAngleDegrees));
        if (options.Eccentricity <= 0.0 || options.Eccentricity >= 1.0)
            throw new ArgumentException("Эксцентриситет должен быть в диапазоне (0; 1).",
                nameof(options.Eccentricity));
        if (options.Fb0Fc0 < 1.0)
            throw new ArgumentException("Отношение fb0/fc0 не может быть меньше единицы.",
                nameof(options.Fb0Fc0));
        if (options.Kc <= 0.5 || options.Kc > 1.0)
            throw new ArgumentException("Kc должен быть больше 0.5 и не больше 1.0.",
                nameof(options.Kc));
        if (options.Viscosity < 0.0)
            throw new ArgumentException("Вязкость не может быть отрицательной.",
                nameof(options.Viscosity));
        if (options.PoissonRatio <= 0.0 || options.PoissonRatio >= 0.5)
            throw new ArgumentException("Коэффициент Пуассона должен быть в диапазоне (0; 0.5).",
                nameof(options.PoissonRatio));
        if (options.FractureEnergy <= 0.0)
            throw new ArgumentException("Энергия разрушения должна быть положительной.",
                nameof(options.FractureEnergy));
        if (options.ElementLength <= 0.0)
            throw new ArgumentException("Размер элемента должен быть положительным.",
                nameof(options.ElementLength));
        if (options.InitialCompressionStressRatio <= 0.0 ||
            options.InitialCompressionStressRatio >= 1.0)
            throw new ArgumentException("Начальный уровень сжатия должен быть в диапазоне (0; 1).",
                nameof(options.InitialCompressionStressRatio));
        if (options.CompressionEtaMin <= 0.0 || options.CompressionEtaMin >= 1.0)
            throw new ArgumentException("Минимальный уровень сжатия должен быть в диапазоне (0; 1).",
                nameof(options.CompressionEtaMin));
    }

    /// <summary>Проверяет, что материал является бетоном с характеристиками нужного расчёта.</summary>
    public static MaterialChars ValidateMaterial(Material material, AbaqusCdpOptions options)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(options);

        if (material.Type != MatType.Concrete)
            throw new ArgumentException("Экспорт Abaqus CDP поддерживает только бетон.", nameof(material));

        var chars = material.GetChars(options.CalcType)
            ?? throw new ArgumentException("Для выбранного вида расчёта нет характеристик бетона.",
                nameof(material));
        if (chars.Type != MatType.Concrete)
            throw new ArgumentException("Характеристики выбранного расчёта не относятся к бетону.",
                nameof(material));

        RequireFinite(chars.E, nameof(chars.E));
        RequireFinite(chars.Fc, nameof(chars.Fc));
        RequireFinite(chars.Ft, nameof(chars.Ft));
        RequireFinite(chars.Ec0, nameof(chars.Ec0));
        if (chars.E <= 0.0 || Math.Abs(chars.Fc) <= 0.0 || Math.Abs(chars.Ft) <= 0.0)
            throw new ArgumentException("Для CDP нужны положительный E и ненулевые Fc/Ft.",
                nameof(material));
        if (chars.Ec0 == 0.0)
            throw new ArgumentException("Для построения ЕКБ-диаграммы нужна ненулевая Ec0.",
                nameof(material));

        return chars;
    }

    /// <summary>Проверяет точки таблицы напряжение-деформация Abaqus.</summary>
    public static void ValidateCurve(IReadOnlyList<AbaqusCdpCurvePoint> points, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
            throw new ArgumentException("Таблица материала не может быть пустой.", parameterName);

        double previousStrain = double.NegativeInfinity;
        foreach (var point in points)
        {
            RequireFinite(point.Stress, nameof(point.Stress));
            RequireFinite(point.TotalStrain, nameof(point.TotalStrain));
            RequireFinite(point.AbaqusStrain, nameof(point.AbaqusStrain));
            RequireFinite(point.Damage, nameof(point.Damage));
            RequireFinite(point.PlasticStrain, nameof(point.PlasticStrain));
            RequireFinite(point.ElasticStrain, nameof(point.ElasticStrain));

            if (point.Stress <= 0.0)
                throw new ArgumentException("В экспортируемой таблице напряжение должно быть положительным.",
                    parameterName);
            if (point.AbaqusStrain <= previousStrain)
                throw new ArgumentException("Абсцисса таблицы должна строго возрастать.", parameterName);
            if (point.Damage < 0.0 || point.Damage >= 1.0)
                throw new ArgumentException("Повреждение должно принадлежать диапазону [0; 1).", parameterName);
            if (point.AbaqusStrain < -1e-12 || point.PlasticStrain < -1e-12 ||
                point.ElasticStrain < -1e-12)
                throw new ArgumentException("Деформации Abaqus CDP не могут быть отрицательными.",
                    parameterName);

            previousStrain = point.AbaqusStrain;
        }
    }

    static void RequireFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("Значение должно быть конечным.", parameterName);
    }
}
