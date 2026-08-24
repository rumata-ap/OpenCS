using System.Globalization;
using System.Text.Json;
using System.Windows.Media;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка сводки пакетного расчёта наклонных сечений.</summary>
public sealed class ShearInclinedBatchRowVM
{
    /// <summary>Номер строки усилий.</summary>
    public int Num { get; init; }
    /// <summary>Метка строки усилий.</summary>
    public string Label { get; init; } = "";
    /// <summary>Поперечная сила Vy, кН.</summary>
    public double Vy { get; init; }
    /// <summary>Поперечная сила Vx, кН.</summary>
    public double Vx { get; init; }
    /// <summary>
    /// Коэффициент использования; NaN — расчёт строки завершился ошибкой,
    /// +∞ — нулевая несущая способность (отказ, а не отсутствие значения).
    /// </summary>
    public double Utilization { get; init; }
    /// <summary>Формула худшей проверки.</summary>
    public string WorstFormula { get; init; } = "";
    /// <summary>Статус строки: ok | failed | error.</summary>
    public string Status { get; init; } = "";

    /// <summary>Текст коэффициента использования.</summary>
    public string UtilizationText => double.IsNaN(Utilization)
        ? "—"
        : double.IsInfinity(Utilization) ? "∞" : Utilization.ToString("F3", CultureInfo.CurrentCulture);

    /// <summary>Текст результата.</summary>
    public string StatusText => Status switch
    {
        "ok" => Loc.S("ShearInclinedPassed"),
        "failed" => Loc.S("ShearInclinedFailed"),
        _ => "—"
    };

    /// <summary>Цвет коэффициента использования.</summary>
    public Brush UtilizationBrush => double.IsNaN(Utilization)
        ? Brushes.Gray
        : !double.IsFinite(Utilization) || Utilization >= 1.0
            ? Brushes.Red
            : Utilization < 0.8 ? Brushes.Green : Brushes.DarkOrange;
}

/// <summary>ViewModel сводки пакетного расчёта наклонных сечений.</summary>
public sealed class ShearInclinedBatchVM
{
    readonly List<string> _cautions = [];

    /// <summary>Разбирает DataJson пакетной задачи.</summary>
    public ShearInclinedBatchVM(string dataJson)
    {
        using var doc = JsonDocument.Parse(dataJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            ErrorText = error.GetString() ?? "";
            return;
        }

        SectionTag = root.GetProperty("sectionTag").GetString() ?? "";
        ForceSetTag = root.TryGetProperty("forceSetTag", out var tag) ? tag.GetString() ?? "" : "";
        Utilization = Number(root, "utilization", double.PositiveInfinity);

        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            string status = row.GetProperty("status").GetString() ?? "";
            Rows.Add(new ShearInclinedBatchRowVM
            {
                Num = row.GetProperty("num").GetInt32(),
                Label = row.GetProperty("label").GetString() ?? "",
                Vy = row.GetProperty("vy").GetDouble(),
                Vx = row.GetProperty("vx").GetDouble(),
                // null при status = error — «нет значения»; null при failed — нулевая
                // несущая способность, то есть +∞
                Utilization = Number(row, "utilization",
                    status == "error" ? double.NaN : double.PositiveInfinity),
                WorstFormula = row.GetProperty("worstFormula").GetString() ?? "",
                Status = status
            });
        }

        foreach (var warning in root.GetProperty("warnings").EnumerateArray())
            if (warning.GetString() is { } text) _cautions.Add(text);
    }

    /// <summary>Числовое поле; null или отсутствие поля дают значение по умолчанию.</summary>
    static double Number(JsonElement owner, string name, double fallback) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : fallback;

    /// <summary>Метка сечения.</summary>
    public string SectionTag { get; } = "";
    /// <summary>Метка набора усилий.</summary>
    public string ForceSetTag { get; } = "";
    /// <summary>Наибольший коэффициент использования по набору.</summary>
    public double Utilization { get; }
    /// <summary>Текст ошибки, если задача завершилась неуспешно.</summary>
    public string ErrorText { get; } = "";
    /// <summary>Строки сводки.</summary>
    public List<ShearInclinedBatchRowVM> Rows { get; } = [];
    /// <summary>Оговорки расчёта.</summary>
    public IReadOnlyList<string> Cautions => _cautions;

    /// <summary>Итоговый вердикт по набору.</summary>
    public string VerdictText => !double.IsFinite(Utilization)
        ? $"{Loc.S("ShearInclinedFailed")} — ∞"
        : Utilization <= 1.0
            ? $"{Loc.S("ShearInclinedPassed")} — {Utilization:F3}"
            : $"{Loc.S("ShearInclinedFailed")} — {Utilization:F3}";

    /// <summary>Цвет итогового вердикта.</summary>
    public Brush VerdictBrush => double.IsFinite(Utilization) && Utilization <= 1.0
        ? Brushes.Green
        : Brushes.Red;

    /// <summary>Видимость блока оговорок.</summary>
    public System.Windows.Visibility CautionsVisibility => _cautions.Count > 0
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;
}
