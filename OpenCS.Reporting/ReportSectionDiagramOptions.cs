namespace OpenCS.Reporting;

/// <summary>Сторона растяжения, отображаемая на схеме сечения.</summary>
public enum ReportTensionSide
{
    /// <summary>Положительная сторона выбранной оси.</summary>
    Positive,

    /// <summary>Отрицательная сторона выбранной оси.</summary>
    Negative,

    /// <summary>Для осесимметричного результата сторона не применяется.</summary>
    NotApplicable,

    /// <summary>Направление не удалось определить.</summary>
    Unknown
}

/// <summary>Расчётные аннотации схемы поперечного сечения.</summary>
public sealed record ReportSectionDiagramOptions
{
    /// <summary>Плоскость проверки, например Mx, My, Vy или Vx.</summary>
    public string? Axis { get; init; }

    /// <summary>Сторона растяжения.</summary>
    public ReportTensionSide TensionSide { get; init; } = ReportTensionSide.Unknown;

    /// <summary>Защитный слой растянутой арматуры.</summary>
    public double? A { get; init; }

    /// <summary>Защитный слой сжатой арматуры.</summary>
    public double? APrime { get; init; }

    /// <summary>Рабочая высота сечения.</summary>
    public double? H0 { get; init; }

    /// <summary>Показывать открытые линии разрезов хомутов.</summary>
    public bool ShowStirrupCuts { get; init; } = true;
}
