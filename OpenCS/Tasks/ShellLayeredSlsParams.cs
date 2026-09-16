using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCS.Tasks;

/// <summary>
/// Параметры задачи ширины раскрытия трещин слоистой пластины (shell_layered_sls /
/// shell_layered_sls_batch). Одно состояние усилий, ручной φ1 — по образцу
/// ShellSimplParams; комбинация длительного/непродолжительного раскрытия (п. 8.2.7)
/// здесь не реализуется.
/// </summary>
public sealed class ShellLayeredSlsParams
{
    [JsonPropertyName("nx")] public double Nx { get; set; }
    [JsonPropertyName("ny")] public double Ny { get; set; }
    [JsonPropertyName("nxy")] public double Nxy { get; set; }
    [JsonPropertyName("mx")] public double Mx { get; set; }
    [JsonPropertyName("my")] public double My { get; set; }
    [JsonPropertyName("mxy")] public double Mxy { get; set; }

    /// <summary>Только для batch: si.ResolveN(plate.H) вместо si.Nx/Ny/Nxy при импорте по напряжениям.</summary>
    [JsonPropertyName("auto_stress_to_force")]
    public bool AutoStressToForce { get; set; } = true;

    [JsonPropertyName("acrc_lim_mm")] public double AcrcLimMm { get; set; } = 0.3;
    [JsonPropertyName("phi1")] public double Phi1 { get; set; } = 1.0;
    [JsonPropertyName("phi2")] public double Phi2 { get; set; } = 0.5;

    /// <summary>«stress» — ф. 8.137; «moment» — ф. 8.138.</summary>
    [JsonPropertyName("sigma_s_crc_method")]
    public string SigmaSCrcMethod { get; set; } = "stress";

    /// <summary>«sp63» — по умолчанию; также поддерживаются «snip» и «radaykin».</summary>
    [JsonPropertyName("wpl_gamma")]
    public string WplGammaMethod { get; set; } = "sp63";

    [JsonIgnore]
    public CScore.SigmaSCrcMethod SigmaSCrc =>
        string.Equals(SigmaSCrcMethod?.Trim(), "moment", StringComparison.OrdinalIgnoreCase)
            ? CScore.SigmaSCrcMethod.CrackingMoment8138
            : CScore.SigmaSCrcMethod.ReleasedConcrete8137;

    [JsonIgnore]
    public CScore.WplGammaMethod WplGamma => WplGammaMethod?.Trim().ToLowerInvariant() switch
    {
        "snip" => CScore.WplGammaMethod.Snip2030184,
        "radaykin" => CScore.WplGammaMethod.Radaykin2018,
        _ => CScore.WplGammaMethod.Sp63,
    };

    public string ToJson() => JsonSerializer.Serialize(this);

    public static ShellLayeredSlsParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new ShellLayeredSlsParams();
        return JsonSerializer.Deserialize<ShellLayeredSlsParams>(json) ?? new ShellLayeredSlsParams();
    }
}
