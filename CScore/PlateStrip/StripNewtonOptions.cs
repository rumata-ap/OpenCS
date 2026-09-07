namespace CScore.PlateStrip;

/// <summary>Настройки нелинейного решателя производной балки полосы (Срез 7).
///
/// Пошаговое нагружение здесь — способ довести Ньютона до сходимости, а <b>не</b> носитель
/// истории: PlateSection не хранит состояния (см. PlateSectionLiveResponse). При
/// монотонно-упрочняющемся отклике результат от числа шагов не зависит; при немонотонном —
/// может зависеть, и это не дефект решателя.</summary>
/// <param name="LoadSteps">Число шагов нагружения.</param>
/// <param name="MaxIterations">Максимум итераций Ньютона на шаг.</param>
/// <param name="ResidualTolerance">Относительный допуск по масштабированной невязке.</param>
/// <param name="IncrementTolerance">Относительный допуск по масштабированному приращению.</param>
/// <param name="MaxStepHalvings">Сколько раз шаг можно поделить пополам при несходимости.</param>
/// <param name="ModifiedNewton">Собирать касательную один раз на шаг, а не на каждой итерации.</param>
public sealed record StripNewtonOptions(
    int LoadSteps = 1,
    int MaxIterations = 30,
    double ResidualTolerance = 1e-8,
    double IncrementTolerance = 1e-8,
    int MaxStepHalvings = 4,
    bool ModifiedNewton = false)
{
    /// <summary>Настройки по умолчанию.</summary>
    public static StripNewtonOptions Default => new();

    /// <summary>Проверить допустимость настроек.</summary>
    public void Validate()
    {
        if (LoadSteps < 1)
            throw new ArgumentOutOfRangeException(nameof(LoadSteps),
                "Число шагов нагружения должно быть положительным.");
        if (MaxIterations < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxIterations),
                "Число итераций должно быть положительным.");
        if (!(ResidualTolerance > 0.0) || !double.IsFinite(ResidualTolerance))
            throw new ArgumentOutOfRangeException(nameof(ResidualTolerance),
                "Допуск по невязке должен быть конечным и положительным.");
        if (!(IncrementTolerance > 0.0) || !double.IsFinite(IncrementTolerance))
            throw new ArgumentOutOfRangeException(nameof(IncrementTolerance),
                "Допуск по приращению должен быть конечным и положительным.");
        if (MaxStepHalvings < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxStepHalvings),
                "Число делений шага не может быть отрицательным.");
    }
}
