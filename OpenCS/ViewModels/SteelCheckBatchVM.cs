using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Окно пакета проверок стального элемента по СП 16: строки набора с Кисп по группам проверок.</summary>
public sealed class SteelCheckBatchVM : ViewModelBase
{
    public string SummaryText { get; }
    public string HeaderText { get; } = "";
    public string NotesText { get; } = "";
    public bool HasNotes => NotesText.Length > 0;
    public Brush StatusBrush { get; }

    public ObservableCollection<BatchRow> Rows { get; } = [];

    /// <summary>Строка пакета: усилия, вид проверки, Кисп по группам (пункт — в подсказке) и причина.</summary>
    public sealed record BatchRow(int Num, string Label, string NText, string MxText, string MyText, string VyText,
        string VxText, string KindText, string StrengthText, string StrengthClause, string StabilityText,
        string StabilityClause, string LocalText, string LocalClause, string SlendernessText, string SlendernessClause)
        : IBatchVerdictRow
    {
        public BatchVerdictView Reason { get; init; } = BatchVerdictView.Empty;
    }

    public SteelCheckBatchVM(CalcResult result)
    {
        if (result.Status == "error")
        {
            SummaryText = Loc.S("CalcResultErrorLabel") + ": " + Error(result.DataJson);
            StatusBrush = Brushes.DarkRed;
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(result.DataJson);
            var root = doc.RootElement;
            string section = Str(root, "sectionTag"), steel = Str(root, "steelTag");
            if (root.TryGetProperty("context", out var c) && c.ValueKind == JsonValueKind.Object)
                HeaderText = string.Format(Loc.S("SteelBatch_HeaderFormat"), section, steel,
                    Num(c, "lefX"), Num(c, "lefY"), Num(c, "lefB"), Num(c, "gammaC"));
            else
                HeaderText = $"{section}, {steel}";
            if (root.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Array)
                NotesText = string.Join("\n", notes.EnumerateArray().Select(n => n.GetString()).Where(n => !string.IsNullOrEmpty(n)));

            int idx = 0;
            foreach (var row in root.GetProperty("rows").EnumerateArray())
            {
                idx++;
                string kind = Str(row, "effective_kind");
                Rows.Add(new BatchRow(BatchResultRowHelper.RowNum(row, idx), Str(row, "label"),
                    Num(row, "N"), Num(row, "Mx"), Num(row, "My"), Num(row, "vy"), Num(row, "vx"),
                    kind.Length > 0 ? Loc.S("CalcTaskKind_" + kind, kind) : "—",
                    Util(row, "util_strength"), Str(row, "clause_strength"),
                    Util(row, "util_stability"), Str(row, "clause_stability"),
                    Util(row, "util_local"), Str(row, "clause_local"),
                    Util(row, "util_constructive"), Str(row, "clause_constructive"))
                { Reason = BatchResultRowHelper.Verdict(result, row) });
            }
            var (passed, notPassed, other) = BatchResultRowHelper.Counts(Rows.Select(r => r.Reason));
            SummaryText = string.Format(Loc.S("SteelBatch_SummaryFormat"), Rows.Count, passed, notPassed, other);
            StatusBrush = notPassed == 0 && other == 0
                ? new SolidColorBrush(Color.FromArgb(70, 80, 180, 80))
                : Brushes.OrangeRed;
        }
        catch (Exception ex)
        {
            SummaryText = Loc.S("CalcResultErrorLabel") + ": " + ex.Message;
            StatusBrush = Brushes.DarkRed;
        }
    }

    static string Error(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? "" : json;
        }
        catch (JsonException) { return json; }
    }

    static string Str(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static string Num(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble().ToString("G5", CultureInfo.CurrentCulture) : "—";

    static string Util(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble().ToString("F3", CultureInfo.CurrentCulture) : "—";
}
