using System.Text.Json.Serialization;

namespace CScore.CalculationTrace;

/// <summary>Структурированный шаг расчёта для построения человекочитаемого отчёта.</summary>
public sealed record CalculationTraceStep
{
    /// <summary>Стабильный идентификатор шага.</summary>
    [JsonPropertyName("stepId")]
    public required string StepId { get; init; }

    /// <summary>Пункт или формула нормативного документа.</summary>
    [JsonPropertyName("codeReference")]
    public string? CodeReference { get; init; }

    /// <summary>Ключ заголовка шага для локализации в слое отчёта.</summary>
    [JsonPropertyName("titleKey")]
    public string? TitleKey { get; init; }

    /// <summary>Ключ пояснения шага для локализации в слое отчёта.</summary>
    [JsonPropertyName("explanationKey")]
    public string? ExplanationKey { get; init; }

    /// <summary>Символическое математическое выражение.</summary>
    [JsonPropertyName("formulaLatex")]
    public string? FormulaLatex { get; init; }

    /// <summary>Шаблон подстановки с токенами числовых значений.</summary>
    [JsonPropertyName("substitutionLatexTemplate")]
    public string? SubstitutionLatexTemplate { get; init; }

    /// <summary>Шаблон выражения результата.</summary>
    [JsonPropertyName("resultLatexTemplate")]
    public string? ResultLatexTemplate { get; init; }

    /// <summary>Исходные значения для подстановки и форматирования.</summary>
    [JsonPropertyName("values")]
    public IReadOnlyDictionary<string, TraceValue> Values { get; init; }
        = new Dictionary<string, TraceValue>(StringComparer.Ordinal);

    /// <summary>Статус шага.</summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CalculationTraceStatus Status { get; init; } = CalculationTraceStatus.Informational;

    /// <summary>Индекс стоянки наклонного сечения, если шаг с ней связан.</summary>
    [JsonPropertyName("stationIndex")]
    public int? StationIndex { get; init; }

    /// <summary>Плоскость наклонного сечения.</summary>
    [JsonPropertyName("plane")]
    public string? Plane { get; init; }

    /// <summary>Код формулы наклонного сечения.</summary>
    [JsonPropertyName("formulaCode")]
    public string? FormulaCode { get; init; }

    /// <summary>Положение стоянки вдоль расчётного профиля.</summary>
    [JsonPropertyName("stationS")]
    public TraceValue? StationS { get; init; }

    /// <summary>Критическая длина проекции наклонного сечения.</summary>
    [JsonPropertyName("criticalC")]
    public TraceValue? CriticalC { get; init; }
}
