namespace CScore.CalculationTrace;

/// <summary>Статус отдельного шага расчётной трассировки.</summary>
public enum CalculationTraceStatus
{
    /// <summary>Условие выполнено.</summary>
    Passed,

    /// <summary>Условие не выполнено.</summary>
    Failed,

    /// <summary>Информационный шаг без нормативного решения.</summary>
    Informational,

    /// <summary>Шаг не применим к текущей ветви расчёта.</summary>
    NotApplicable,

    /// <summary>Несущая способность не определена.</summary>
    NoCapacity,

    /// <summary>Расчёт завершился ошибкой.</summary>
    Error
}
