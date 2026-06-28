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
    /// <summary>0 — брать из глобальных настроек (NewtonMaxIter).</summary>
    [JsonPropertyName("max_iter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int MaxIter { get; set; }

    public static ShellStrainParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new ShellStrainParams();
        var p = JsonSerializer.Deserialize<ShellStrainParams>(json) ?? new ShellStrainParams();
        // Раньше в JSON попадало max_iter=50 из дефолта класса — не переопределение настроек.
        if (p.MaxIter == 50)
            p.MaxIter = 0;
        return p;
    }
}
