using CScore.Sp63;

namespace CScore.Sp63.Normal;

/// <summary>Результат упрощённой проверки нормального сечения.</summary>
public sealed class Sp63NormalResult
{
    /// <summary>Статус применимости и выполнения расчёта.</summary>
    public Sp63NormalStatus Status { get; set; }

    /// <summary>Вердикт прочности; null для неприменимого или ошибочного результата.</summary>
    public bool? StrengthPassed { get; set; }

    /// <summary>Выбранная нормативная ветвь.</summary>
    public string Branch { get; set; } = "";

    /// <summary>Числовые условия, непосредственно входящие в вердикт прочности.</summary>
    public List<CheckDetail> StrengthDetails { get; set; } = [];

    /// <summary>
    /// Справочные проверки минимального армирования по п. 10.3.6 (раздел 10 СП 63).
    /// Не входят в <see cref="StrengthPassed"/>.
    /// </summary>
    public List<CheckDetail> ConstructiveChecks { get; set; } = [];

    /// <summary>Причины неприменимости формульного режима.</summary>
    public List<Sp63NormalMessage> ApplicabilityMessages { get; set; } = [];

    /// <summary>Справочные сообщения, не изменяющие вердикт прочности.</summary>
    public List<Sp63NormalMessage> InformationalMessages { get; set; } = [];

    /// <summary>Переменные расчёта для прозрачного результата.</summary>
    public Dictionary<string, double> Variables { get; set; } = [];

    /// <summary>Результат существующей проверки коэффициента η.</summary>
    public EccentricityAmplifier.EtaResult? Eta { get; set; }
}
