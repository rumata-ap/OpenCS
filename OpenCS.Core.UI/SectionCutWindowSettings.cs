using System.Text.Json.Serialization;

namespace OpenCS.Utilites;

/// <summary>Параметры окна эпюры разреза сечения (глобальные настройки).</summary>
public sealed class SectionCutWindowSettings
{
    [JsonPropertyName("width")]
    public double Width { get; set; } = 900;

    [JsonPropertyName("height")]
    public double Height { get; set; } = 500;

    [JsonPropertyName("left")]
    public double? Left { get; set; }

    [JsonPropertyName("top")]
    public double? Top { get; set; }

    [JsonPropertyName("scaleS")]
    public double ScaleS { get; set; } = 1.0;

    [JsonPropertyName("scaleV")]
    public double ScaleV { get; set; } = 1.0;

    public SectionCutWindowSettings Clone() => new()
    {
        Width = Width,
        Height = Height,
        Left = Left,
        Top = Top,
        ScaleS = ScaleS,
        ScaleV = ScaleV
    };
}
