using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCS.Tasks;

/// <summary>
/// Параметры задачи проверки стального сечения по СП 16.13330.2017.
/// Хранятся в CalcTask.ParamsJson.
/// </summary>
public record SteelCheckParams
{
    public double DesignLengthX { get; init; } = 3.0;
    public double DesignLengthY { get; init; } = 3.0;
    public double MuX { get; init; } = 1.0;
    public double MuY { get; init; } = 1.0;
    public double BetaM { get; init; } = 1.0;
    public double GammaM { get; init; } = 1.025;
    /// <summary>Расстояние между точками боковой связи lbit [м]. 0 = не задано.</summary>
    public double DesignLengthBit { get; init; }
    public SteelManualForces? ManualForces { get; init; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    public static SteelCheckParams Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new SteelCheckParams();
        return JsonSerializer.Deserialize<SteelCheckParams>(json, JsonOpts) ?? new SteelCheckParams();
    }
}

/// <summary>
/// Ручной ввод усилий для стальной задачи.
/// </summary>
public record SteelManualForces
{
    public double N { get; init; }
    public double Mx { get; init; }
    public double My { get; init; }
    public double Mz { get; init; }
    public double Qy { get; init; }
    public double Qz { get; init; }
}
