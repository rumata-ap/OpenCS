using System.Text.Json;

namespace CScore;

/// <summary>
/// Параметры задачи «Ширина раскрытия трещин» (хранятся в CalcTask.ParamsJson).
/// Используется и одиночной (`crack_width`), и пакетной (`crack_width_batch`) задачей —
/// каждая читает только относящиеся к ней поля.
/// </summary>
public sealed class CrackWidthTaskParams
{
    /// <summary>Ручной ввод N total (кН) — только для одиночной задачи.</summary>
    public double? N { get; set; }
    /// <summary>Ручной ввод Mx total (кН·м) — только для одиночной задачи.</summary>
    public double? Mx { get; set; }
    /// <summary>Ручной ввод My total (кН·м) — только для одиночной задачи.</summary>
    public double? My { get; set; }

    public double AcrcUltLong { get; set; } = 0.3;
    public double AcrcUltShort { get; set; } = 0.4;

    /// <summary>"total_only" | "share" | "two_sets" (batch) | "manual" | "force_item_long" (single).</summary>
    public string ForcesMode { get; set; } = "total_only";

    /// <summary>Доля длительной нагрузки от полной (режим "share").</summary>
    public double LongShare { get; set; } = 0.7;

    /// <summary>
    /// Использовать CalcType.NL (вместо N) при расчёте длительной части раскрытия трещин
    /// (acrcLong). Кратковременная часть (acrc2) не затрагивается. Момент трещинообразования
    /// (calcCrc) на этот флаг не влияет — выбирается отдельно через CalcTask.CalcType (N/NL).
    /// </summary>
    public bool LongPartUseNL { get; set; } = false;

    /// <summary>FK → ForceSet длительных нагрузок (режим "two_sets", batch).</summary>
    public int? ForceSetLongId { get; set; }

    /// <summary>FK → конкретная строка LoadItem длительной нагрузки (режим "force_item_long", single).</summary>
    public int? ForceItemLongId { get; set; }

    /// <summary>Длительная нагрузка вручную (режим "manual", single), кН/кН·м.</summary>
    public double? NLongManual { get; set; }
    public double? MxLongManual { get; set; }
    public double? MyLongManual { get; set; }

    public static CrackWidthTaskParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new CrackWidthTaskParams();
        try
        {
            return JsonSerializer.Deserialize<CrackWidthTaskParams>(json) ?? new CrackWidthTaskParams();
        }
        catch
        {
            return new CrackWidthTaskParams();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this);
}
