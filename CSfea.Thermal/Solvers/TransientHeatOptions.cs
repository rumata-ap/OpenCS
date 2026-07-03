namespace CSfea.Thermal.Solvers;

/// <summary>
/// Параметры нестационарного теплового решателя (θ-схема + Пикар-итерации).
/// </summary>
public sealed class TransientHeatOptions
{
    /// <summary>Продолжительность расчёта, с.</summary>
    public double Duration_s { get; init; }

    /// <summary>Базовый шаг по времени, с.</summary>
    public double TimeStep_s { get; init; }

    /// <summary>Интервал сохранения снапшотов, с.</summary>
    public double SnapshotStep_s { get; init; }

    /// <summary>Параметр θ-схемы (1.0 = полностью неявная схема).</summary>
    public double Theta { get; init; } = 1.0;

    /// <summary>Максимум итераций Пикара на шаге времени.</summary>
    public int PicardMaxIter { get; init; } = 20;

    /// <summary>Критерий сходимости Пикара по температуре, °C.</summary>
    public double PicardTolCelsius { get; init; } = 0.5;

    /// <summary>Начальная температура во всех узлах, °C.</summary>
    public double TInitCelsius { get; init; } = 20.0;

    /// <summary>Использовать адаптивный sub-stepping в первую минуту.</summary>
    public bool AdaptiveFirstMinute { get; init; } = true;

    /// <summary>
    /// Принудительная численная рефакторизация оператора каждые N Пикар-итераций
    /// (помимо рефакторизации по стагнации невязки). Большое значение ≈ один раз на шаг.
    /// </summary>
    public int RefactorEveryNIter { get; init; } = 1000;
}
