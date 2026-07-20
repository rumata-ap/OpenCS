using System.Text.Json;

namespace OpenCS.Tasks;

/// <summary>Параметры запуска линейного FEM-расчёта, хранимые в FemAnalysis.ParamsJson.</summary>
public sealed class FemAnalysisParams
{
    public string? ExecutablePath { get; set; }
    public int TimeoutSeconds { get; set; } = 120;

    public string ToJson() => JsonSerializer.Serialize(this);
    public static FemAnalysisParams Parse(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize<FemAnalysisParams>(json) ?? new();
}
