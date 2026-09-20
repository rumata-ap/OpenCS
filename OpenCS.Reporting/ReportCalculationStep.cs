namespace OpenCS.Reporting;

/// <summary>Статус блока формульной трассировки в отчёте.</summary>
public enum ReportCalculationStatus
{
    /// <summary>Условие выполнено.</summary>
    Passed,

    /// <summary>Условие не выполнено.</summary>
    Failed,

    /// <summary>Информационный блок.</summary>
    Informational,

    /// <summary>Проверка неприменима.</summary>
    NotApplicable,

    /// <summary>Несущая способность не определена.</summary>
    NoCapacity,

    /// <summary>Ошибка расчёта.</summary>
    Error
}

/// <summary>Именованный блок одного шага расчётной трассировки.</summary>
public sealed record ReportCalculationStep : ReportBlock
{
    /// <summary>Стабильный идентификатор шага.</summary>
    public required string StepId { get; init; }

    /// <summary>Пункт нормативного документа.</summary>
    public string Reference { get; init; } = "";

    /// <summary>Заголовок шага.</summary>
    public string Title { get; init; } = "";

    /// <summary>Символическая формула.</summary>
    public ReportMathExpression Formula { get; init; }
        = ReportMathExpression.Plain("");

    /// <summary>Формула с численной подстановкой.</summary>
    public ReportMathExpression Substitution { get; init; }
        = ReportMathExpression.Plain("");

    /// <summary>Формула или число результата.</summary>
    public ReportMathExpression Result { get; init; }
        = ReportMathExpression.Plain("");

    /// <summary>Единица результата.</summary>
    public string Unit { get; init; } = "";

    /// <summary>Пояснение к шагу.</summary>
    public string? Note { get; init; }

    /// <summary>Статус шага.</summary>
    public ReportCalculationStatus Status { get; init; }

    /// <summary>Локализованная подпись статуса, если она нужна в отчёте.</summary>
    public string? StatusText { get; init; }
}
