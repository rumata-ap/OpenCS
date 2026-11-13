using System.Globalization;
using System.Text.Json;
using CScore.Batch;

namespace OpenCS.Reporting;

/// <summary>Строка пакета для отчёта: номер, метка, усилия из строки результата, причина.</summary>
public sealed record BatchReportRow(int Num, string Label, IReadOnlyDictionary<string, double> Forces, BatchRowVerdict Verdict);

/// <summary>Разбор результата пакета для отчёта: строки с причинами, счётчики, определяющая строка.</summary>
public sealed class BatchReportData
{
    /// <summary>Усилия, которые пакеты пишут в строки результата, в порядке столбцов отчёта.</summary>
    static readonly string[] KnownForces = ["N", "Mx", "My", "Mx_long", "Mx_total", "My_long", "My_total", "vy", "vx"];

    /// <summary>Сообщение об ошибке расчёта пакета; пусто — ошибки нет.</summary>
    public string Error { get; private init; } = "";

    /// <summary>Строки пакета в порядке результата.</summary>
    public IReadOnlyList<BatchReportRow> Rows { get; private init; } = [];

    /// <summary>Номер определяющей строки (наибольший Кисп); null — Кисп нет ни у одной.</summary>
    public int? GoverningNum { get; private init; }

    /// <summary>Ключи усилий, найденные в строках (подмножество известных, в их порядке).</summary>
    public IReadOnlyList<string> ForceKeys { get; private init; } = [];

    /// <summary>Число строк со статусом причины.</summary>
    public int Count(string status) => Rows.Count(r => r.Verdict.Status == status);

    /// <summary>Разбирает DataJson пакета вида kind.</summary>
    public static BatchReportData Parse(string kind, string dataJson)
    {
        using var doc = JsonDocument.Parse(dataJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
            return new BatchReportData { Error = err.GetString() ?? "" };
        if (!root.TryGetProperty("rows", out var rowsEl) && !root.TryGetProperty("Rows", out rowsEl)
            || rowsEl.ValueKind != JsonValueKind.Array)
            return new BatchReportData();

        var rows = new List<BatchReportRow>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (var r in rowsEl.EnumerateArray())
        {
            i++;
            int num = Int(r, "num") ?? Int(r, "Num") ?? i;
            string label = Str(r, "label") is { Length: > 0 } l ? l : Str(r, "Label");
            var forces = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var k in KnownForces)
                if (r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number)
                {
                    forces[k] = v.GetDouble();
                    keys.Add(k);
                }
            rows.Add(new BatchReportRow(num, label, forces, BatchRowVerdictReader.Read(kind, r)));
        }
        int? g = BatchVerdictSummary.GoverningIndex(rows.Select(r => r.Verdict).ToList());
        return new BatchReportData
        {
            Rows = rows,
            GoverningNum = g is { } gi ? rows[gi].Num : null,
            ForceKeys = KnownForces.Where(keys.Contains).ToList(),
        };
    }

    static int? Int(JsonElement e, string k)
        => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    static string Str(JsonElement e, string k)
        => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>Число для таблицы отчёта: до 4 значащих цифр, запятая.</summary>
    internal static string F(double v) => v.ToString("G4", CultureInfo.GetCultureInfo("ru-RU"));
}
