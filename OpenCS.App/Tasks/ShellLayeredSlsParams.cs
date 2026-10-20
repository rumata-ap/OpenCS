using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCS.Tasks;

/// <summary>
/// Параметры задачи ширины раскрытия трещин слоистой пластины (shell_layered_sls /
/// shell_layered_sls_batch). По умолчанию — одно состояние усилий и ручной φ1 (по образцу
/// ShellSimplParams). С <see cref="Combine827"/> (одиночная задача) — как проверка по КЭ: при CalcType.N
/// раскрытие по п. 8.2.7 с длительной частью <see cref="Long"/> или долей <see cref="LtFraction"/>,
/// при NL — продолжительное (φ1 = 1,4); φ1 не используется.
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

    /// <summary>Считать как проверка по КЭ (п. 8.2.7, уточнение слоёв при несходимости); false — ручной φ1.</summary>
    [JsonPropertyName("sp_8_2_7")] public bool Combine827 { get; set; }
    /// <summary>Длительная часть строкой NL (кН/м, кН·м/м); null — доля <see cref="LtFraction"/>.</summary>
    [JsonPropertyName("long")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShellLayeredSlsForces? Long { get; set; }
    /// <summary>Доля длительной части от полной (0…1) без строки NL; 0 — без длительной части (только acrc2).</summary>
    [JsonPropertyName("lt_fraction")] public double LtFraction { get; set; }
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

    /// <summary>Длительная часть строкой; null — не задана.</summary>
    [JsonIgnore]
    public CScore.ShellLoadItem? LongItem => Long is { } l
        ? new CScore.ShellLoadItem { Nx = l.Nx, Ny = l.Ny, Nxy = l.Nxy, Mx = l.Mx, My = l.My, Mxy = l.Mxy }
        : null;

    public string ToJson() => JsonSerializer.Serialize(this);

    public static ShellLayeredSlsParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new ShellLayeredSlsParams();
        return JsonSerializer.Deserialize<ShellLayeredSlsParams>(json) ?? new ShellLayeredSlsParams();
    }
}

/// <summary>Усилия пластины на 1 м (кН/м, кН·м/м).</summary>
public sealed class ShellLayeredSlsForces
{
    [JsonPropertyName("nx")] public double Nx { get; set; }
    [JsonPropertyName("ny")] public double Ny { get; set; }
    [JsonPropertyName("nxy")] public double Nxy { get; set; }
    [JsonPropertyName("mx")] public double Mx { get; set; }
    [JsonPropertyName("my")] public double My { get; set; }
    [JsonPropertyName("mxy")] public double Mxy { get; set; }

    public static ShellLayeredSlsForces Of(CScore.ShellLoadItem s) =>
        new() { Nx = s.Nx, Ny = s.Ny, Nxy = s.Nxy, Mx = s.Mx, My = s.My, Mxy = s.Mxy };
}
