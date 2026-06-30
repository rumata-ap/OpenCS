using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCS.Tasks;

/// <summary>Параметры задачи кручения Сен-Венана.</summary>
public sealed class TorsionParams
{
    /// <summary>Целевой размер элемента, м (для сетки/дискретизации).</summary>
    [JsonPropertyName("element_size")] public double ElementSize { get; set; } = 0.05;

    /// <summary>Модуль сдвига G, МПа (опционально, для пересчёта τ_max).</summary>
    [JsonPropertyName("g_mpa")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double GMPa { get; set; }

    /// <summary>Крутящий момент Mk, кН·м (опционально, для пересчёта τ_max).</summary>
    [JsonPropertyName("mk_knm")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double MkKNm { get; set; }

    public static TorsionParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return new TorsionParams();
        return JsonSerializer.Deserialize<TorsionParams>(json) ?? new TorsionParams();
    }
}
