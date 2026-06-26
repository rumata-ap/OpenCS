using System.Text.Json;

namespace CScore.Fem;

/// <summary>
/// Параметры нормативной проверки ЖБ плитного/оболочечного сечения.
/// Сериализуется в FemCheck.ParamsJson при NormCode == "rc_plate_check".
/// </summary>
public record PlateCheckParams
{
    /// <summary>
    /// Вид проверки:
    /// shell_simpl_wa_uls    — Wood–Armer, прочность (ПС1);
    /// shell_simpl_wa_sls    — Wood–Armer, трещины (ПС2);
    /// shell_simpl_capri_uls — Капра-Мори, прочность (ПС1);
    /// shell_simpl_capri_sls — Капра-Мори, трещины (ПС2);
    /// shell_layered         — слоистая модель, прочность.
    /// </summary>
    public string Kind      { get; init; } = "shell_simpl_wa_uls";
    /// <summary>Предельная ширина раскрытия трещин (мм), для SLS-методов.</summary>
    public double AcrcLimMm { get; init; } = 0.3;
    /// <summary>Шаг перебора направлений (°), для Капра-Мори.</summary>
    public double StepDeg   { get; init; } = 10.0;
    /// <summary>φ1 — коэффициент длительности нагружения (1.0 — кратковременная, 1.4 — длительная).</summary>
    public double Phi1      { get; init; } = 1.0;
    /// <summary>φ2 — коэффициент профиля арматуры (0.5 — периодический, 0.8 — гладкий).</summary>
    public double Phi2      { get; init; } = 0.5;

    /// <summary>
    /// Режим вычисления φ1:
    /// "manual" — берём значение Phi1;
    /// "auto"   — вычисляем per-row из пары N/NL наборов усилий.
    /// </summary>
    public string Phi1Mode { get; init; } = "manual";

    /// <summary>
    /// Группа нормативной проверки: "uls" — 1-я ГПС, "sls" — 2-я ГПС.
    /// Пустая строка — авто-классификация по Kind.
    /// </summary>
    public string CheckGroup    { get; init; } = "";
    /// <summary>Id явно выбранного набора усилий NL (для shell_layered SLS). 0 — не задан.</summary>
    public int    NlForceSetId  { get; init; } = 0;
    /// <summary>Доля длительности (0..1): виртуальный NL = N * LtFraction. Активен при NlForceSetId==0.</summary>
    public double LtFraction    { get; init; } = 0.0;

    public string ToJson() => JsonSerializer.Serialize(this);

    public static PlateCheckParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<PlateCheckParams>(json) ?? new(); }
        catch { return new(); }
    }
}
