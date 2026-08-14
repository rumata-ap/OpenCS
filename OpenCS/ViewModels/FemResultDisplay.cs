namespace OpenCS.ViewModels;

/// <summary>Единица отображения линейных результатов, хранящихся в метрах.</summary>
public enum FemLengthUnit
{
    /// <summary>Миллиметры.</summary>
    Millimeters,
    /// <summary>Сантиметры.</summary>
    Centimeters,
    /// <summary>Метры.</summary>
    Meters
}

/// <summary>Коэффициент отображения углов поворота, хранящихся в радианах.</summary>
public enum FemRotationScale
{
    /// <summary>Радианы без дополнительного коэффициента.</summary>
    One = 1,
    /// <summary>Радианы, умноженные на 100.</summary>
    OneHundred = 100,
    /// <summary>Радианы, умноженные на 1000.</summary>
    OneThousand = 1000
}

/// <summary>Режим состава узловой таблицы результатов.</summary>
public enum FemDisplacementDisplayMode
{
    /// <summary>Показывать все доступные узловые результаты.</summary>
    AllNodes,
    /// <summary>Показывать только экстремальные узлы по стержням.</summary>
    ExtremesOnly
}

/// <summary>Чистые преобразования результатов FEM для отображения.</summary>
public static class FemResultDisplayConverter
{
    /// <summary>Переводит длину из метров в выбранную единицу.</summary>
    public static double ConvertLength(double meters, FemLengthUnit unit) =>
        meters * (unit switch
        {
            FemLengthUnit.Millimeters => 1000.0,
            FemLengthUnit.Centimeters => 100.0,
            FemLengthUnit.Meters => 1.0,
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null)
        });

    /// <summary>Переводит радианное значение поворота в выбранный масштаб.</summary>
    public static double ConvertRotation(double radians, FemRotationScale scale) =>
        radians * (int)scale;
}

/// <summary>Рассчитывает масштаб ленты одной ненулевой эпюры усилия.</summary>
public static class FemForceScaleCalculator
{
    /// <summary>
    /// Возвращает масштаб в метрах на кН для одной компоненты усилия.
    /// Нулевые и нечисловые значения не участвуют в расчёте.
    /// </summary>
    public static double Suggest(double geometryDiagonalM, IReadOnlyList<double> values)
    {
        if (!double.IsFinite(geometryDiagonalM) || geometryDiagonalM <= 0)
            return 1.0;

        double maxValue = 0.0;
        foreach (double value in values)
        {
            if (double.IsFinite(value))
                maxValue = Math.Max(maxValue, Math.Abs(value));
        }

        if (maxValue <= 1e-12)
            return 1.0;

        double maxValueKN = maxValue / 1000.0;
        double result = 0.1 * geometryDiagonalM / maxValueKN;
        return double.IsFinite(result) && result > 0 ? result : 1.0;
    }
}

/// <summary>Хранит ручные переопределения и автоматические масштабы по компонентам.</summary>
public sealed class FemForceScaleState
{
    readonly Dictionary<FemForceComponent, ScaleEntry> _values = [];

    /// <summary>Возвращает масштаб компоненты, вычисляя его при первом обращении.</summary>
    public double Get(FemForceComponent component, Func<double> automaticFactory)
    {
        if (!_values.TryGetValue(component, out ScaleEntry entry))
        {
            entry = new ScaleEntry(Normalize(automaticFactory()), false);
            _values[component] = entry;
        }

        return entry.Value;
    }

    /// <summary>Устанавливает ручной масштаб, который не заменяется автообновлением.</summary>
    public void SetManual(FemForceComponent component, double value) =>
        _values[component] = new ScaleEntry(Normalize(value), true);

    /// <summary>Сбрасывает ручное значение и записывает новый автоматический масштаб.</summary>
    public void Reset(FemForceComponent component, Func<double> automaticFactory) =>
        _values[component] = new ScaleEntry(Normalize(automaticFactory()), false);

    /// <summary>Обновляет только компоненты без ручного переопределения.</summary>
    public void RefreshAutomatic(params (FemForceComponent Component, double Value)[] values)
    {
        foreach ((FemForceComponent component, double value) in values)
        {
            if (!_values.TryGetValue(component, out ScaleEntry entry) || !entry.IsManual)
                _values[component] = new ScaleEntry(Normalize(value), false);
        }
    }

    /// <summary>Показывает, задан ли для компоненты ручной масштаб.</summary>
    public bool IsManual(FemForceComponent component) =>
        _values.TryGetValue(component, out ScaleEntry entry) && entry.IsManual;

    static double Normalize(double value) =>
        double.IsFinite(value) && value > 0 ? value : 1.0;

    readonly record struct ScaleEntry(double Value, bool IsManual);
}
