using System.Text.Json.Serialization;

namespace CScore.CalculationTrace;

/// <summary>Исходное числовое значение шага без заранее отформатированного текста.</summary>
public sealed record TraceValue(
    [property: JsonPropertyName("value")] double Value,
    [property: JsonPropertyName("unit")]
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    CalculationUnit Unit,
    [property: JsonPropertyName("numberProfile")] string? NumberProfile = null);
