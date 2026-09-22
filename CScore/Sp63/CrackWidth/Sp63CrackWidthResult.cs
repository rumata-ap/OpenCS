namespace CScore.Sp63.CrackWidth;

/// <summary>Результат упрощённой проверки ширины раскрытия трещин.</summary>
public sealed class Sp63CrackWidthResult
{
    /// <summary>Статус применимости и выполнения расчёта.</summary>
    public Sp63CrackWidthStatus Status { get; set; }

    /// <summary>Вердикт acrc ≤ acrc,lim; null для неприменимого или ошибочного результата.</summary>
    public bool? LimitPassed { get; set; }

    /// <summary>Образуются ли трещины (M > Mcrc); null, если расчёт не выполнен.</summary>
    public bool? Cracked { get; set; }

    /// <summary>Выбранная нормативная ветвь.</summary>
    public string Branch { get; set; } = "";

    /// <summary>Числовое условие acrc ≤ acrc,lim, определяющее вердикт.</summary>
    public List<CheckDetail> Details { get; set; } = [];

    /// <summary>Причины неприменимости формульного режима.</summary>
    public List<Sp63CrackWidthMessage> ApplicabilityMessages { get; set; } = [];

    /// <summary>Справочные сообщения, не изменяющие вердикт.</summary>
    public List<Sp63CrackWidthMessage> InformationalMessages { get; set; } = [];

    /// <summary>
    /// Полная кривизна по пп. 8.2.23–8.2.30 (режим LongAndShort); <see langword="null"/> —
    /// не вычислялась. Справочно, в вердикт LimitPassed не входит.
    /// </summary>
    public Sp63CurvatureResult? Curvature { get; set; }

    /// <summary>Переменные расчёта для прозрачного результата.</summary>
    public Dictionary<string, double> Variables { get; set; } = [];
}
