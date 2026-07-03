namespace CSfea.Thermal.Solvers;

/// <summary>
/// Запись лога сходимости Пикар-итераций для одного шага по времени.
/// </summary>
public sealed class PicardRecord
{
    /// <summary>Текущее физическое время после шага, с.</summary>
    public double Time_s { get; init; }

    /// <summary>Число выполненных Пикар-итераций.</summary>
    public int NPicardIter { get; init; }

    /// <summary>Максимальная невязка по температуре между итерациями, °C.</summary>
    public double MaxResidualCelsius { get; init; }
}
