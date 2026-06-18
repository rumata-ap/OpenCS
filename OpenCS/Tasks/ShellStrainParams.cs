using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCS.Tasks;

/// <summary>Параметры задачи поиска плоскости деформаций пластины (одиночной).</summary>
public sealed class ShellStrainParams
{
    [JsonPropertyName("nx")]  public double Nx { get; set; }
    [JsonPropertyName("ny")]  public double Ny { get; set; }
    [JsonPropertyName("nxy")] public double Nxy { get; set; }
    [JsonPropertyName("mx")]  public double Mx { get; set; }
    [JsonPropertyName("my")]  public double My { get; set; }
    [JsonPropertyName("mxy")] public double Mxy { get; set; }
    [JsonPropertyName("tol_res")] public double TolRes { get; set; } = 1e-3;
    [JsonPropertyName("max_iter")] public int MaxIter { get; set; } = 50;

    public static ShellStrainParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new ShellStrainParams();
        return JsonSerializer.Deserialize<ShellStrainParams>(json) ?? new ShellStrainParams();
    }
}
