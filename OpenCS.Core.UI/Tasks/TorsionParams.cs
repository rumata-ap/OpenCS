using System.Text.Json;
using System.Text.Json.Serialization;
using CSTriangulation;

namespace OpenCS.Tasks;

/// <summary>Параметры задачи кручения Сен-Венана.</summary>
public sealed class TorsionParams
{
    /// <summary>Целевой размер элемента, м (для сетки/дискретизации).</summary>
    [JsonPropertyName("element_size")] public double ElementSize { get; set; } = 0.05;

    /// <summary>Модуль сдвига G, МПа (устарело — вычисляется из E материала).</summary>
    [JsonPropertyName("g_mpa")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double GMPa { get; set; }

    /// <summary>Крутящий момент Mk, кН·м (опционально, для пересчёта τ_max).</summary>
    [JsonPropertyName("mk_knm")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double MkKNm { get; set; }

    /// <summary>Метод триангуляции области (по умолчанию AdvancingFront).</summary>
    [JsonPropertyName("triangulation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TriangulationMethod Triangulation { get; set; } = TriangulationMethod.AdvancingFront;

    /// <summary>
    /// Автоматическая сходимость (экстраполяция Ричардсона).
    /// Если true, <see cref="ElementSize"/> игнорируется; используются <see cref="AutoH0"/> и <see cref="AutoRuns"/>.
    /// </summary>
    [JsonPropertyName("auto_converge")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AutoConverge { get; set; }

    /// <summary>Стартовая длина граничного элемента при автосходимости, м. 0 = взять MinEdgeLength.</summary>
    [JsonPropertyName("auto_h0")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double AutoH0 { get; set; }

    /// <summary>Число прогонов автосходимости (N ≥ 2). 0 = default 3.</summary>
    [JsonPropertyName("auto_runs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int AutoRuns { get; set; }

    /// <summary>
    /// Порядок конечного элемента МКЭ: "linear" (T3, по умолчанию) или "quadratic" (T6).
    /// Не используется для МГЭ.
    /// </summary>
    [JsonPropertyName("fem_order")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string FemOrder { get; set; } = "linear";

    public static TorsionParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return new TorsionParams();
        return JsonSerializer.Deserialize<TorsionParams>(json) ?? new TorsionParams();
    }

    public string ToJson() => JsonSerializer.Serialize(this);
}
