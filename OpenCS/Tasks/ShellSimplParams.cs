using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCS.Tasks;

public sealed class ShellSimplParams
{
    [JsonPropertyName("nx")]   public double Nx { get; set; }
    [JsonPropertyName("ny")]   public double Ny { get; set; }
    [JsonPropertyName("nxy")]  public double Nxy { get; set; }
    [JsonPropertyName("mx")]   public double Mx { get; set; }
    [JsonPropertyName("my")]   public double My { get; set; }
    [JsonPropertyName("mxy")]  public double Mxy { get; set; }
    [JsonPropertyName("auto_stress_to_force")]
    public bool AutoStressToForce { get; set; } = true;
    [JsonPropertyName("step_deg")]    public double StepDeg { get; set; } = 10.0;
    [JsonPropertyName("acrc_lim_mm")] public double AcrcLimMm { get; set; } = 0.3;
    [JsonPropertyName("phi1")] public double Phi1 { get; set; } = 1.0;
    [JsonPropertyName("phi2")] public double Phi2 { get; set; } = 0.5;

    /// <summary>
    /// Способ получения σs,crc в формуле ψs (п. 8.2.18): "stress" — по напряжениям,
    /// ф. (8.137) (по умолчанию); "moment" — через Mcrc, ф. (8.138).
    /// </summary>
    [JsonPropertyName("sigma_s_crc_method")]
    public string SigmaSCrcMethod { get; set; } = "stress";

    /// <summary>Разобранное значение <see cref="SigmaSCrcMethod"/>; при неизвестном — по умолчанию.</summary>
    [JsonIgnore]
    public CScore.SigmaSCrcMethod SigmaSCrc =>
        string.Equals(SigmaSCrcMethod?.Trim(), "moment", System.StringComparison.OrdinalIgnoreCase)
            ? CScore.SigmaSCrcMethod.CrackingMoment8138
            : CScore.SigmaSCrcMethod.ReleasedConcrete8137;

    public static ShellSimplParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new ShellSimplParams();
        return JsonSerializer.Deserialize<ShellSimplParams>(json) ?? new ShellSimplParams();
    }
}
