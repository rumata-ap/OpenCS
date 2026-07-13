using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel пакетного расчёта ширины раскрытия трещин.</summary>
public sealed class CrackWidthBatchVM : ViewModelBase
{
    public string TaskTag { get; }
    public string CreatedText { get; }
    public string SummaryText { get; }
    public Brush StatusBrush { get; }
    public bool HasError { get; }
    public string ErrorText { get; } = "";

    public ObservableCollection<BatchRow> Rows { get; } = [];

    public sealed record BatchRow(
        int Num, string Label, string NText, string MxLongText, string MxTotalText,
        string AcrcLongText, string AcrcShortText,
        string PassedLongText, string PassedShortText, bool IsPassed, bool IsCracked);

    public CrackWidthBatchVM(CalcResult result)
    {
        TaskTag = result.TaskTag;
        CreatedText = result.Created;

        if (result.Status == "error")
        {
            HasError = true;
            try
            {
                var doc = JsonDocument.Parse(result.DataJson);
                ErrorText = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? "" : result.DataJson;
            }
            catch { ErrorText = result.DataJson; }
            SummaryText = Loc.S("CalcResultErrorLabel");
            StatusBrush = Brushes.DarkRed;
            return;
        }

        try
        {
            var doc = JsonDocument.Parse(result.DataJson);
            var root = doc.RootElement;

            int total = root.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
            int passed = root.TryGetProperty("passed_count", out var pc) ? pc.GetInt32() : 0;
            bool allPassed = root.TryGetProperty("all_passed", out var ap) && ap.GetBoolean();

            StatusBrush = allPassed
                ? new SolidColorBrush(Color.FromArgb(70, 80, 180, 80))
                : Brushes.OrangeRed;
            SummaryText = string.Format(Loc.S("CrackWidthBatch_SummaryFormat"), total, passed, total - passed);

            if (root.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                int idx = 0;
                foreach (var row in rows.EnumerateArray())
                {
                    idx++;
                    bool passedLong = row.TryGetProperty("passed_long", out var pl) && pl.GetBoolean();
                    bool passedShort = row.TryGetProperty("passed_short", out var ps) && ps.GetBoolean();
                    bool cracked = row.TryGetProperty("cracked", out var cr) && cr.GetBoolean();

                    Rows.Add(new BatchRow(
                        Num: BatchResultRowHelper.RowNum(row, idx),
                        Label: Str(row, "label"),
                        NText: Num(row, "N", 4),
                        MxLongText: Num(row, "Mx_long", 4),
                        MxTotalText: Num(row, "Mx_total", 4),
                        AcrcLongText: Num(row, "acrc_long", 4),
                        AcrcShortText: Num(row, "acrc_short", 4),
                        PassedLongText: passedLong ? Loc.S("StrengthNDM_Passed") : Loc.S("StrengthNDM_NotPassed"),
                        PassedShortText: passedShort ? Loc.S("StrengthNDM_Passed") : Loc.S("StrengthNDM_NotPassed"),
                        IsPassed: passedLong && passedShort,
                        IsCracked: cracked));
                }
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorText = ex.Message;
            SummaryText = Loc.S("CalcResultErrorLabel");
            StatusBrush = Brushes.DarkRed;
        }
    }

    static string Str(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) ? v.GetString() ?? "—" : "—";

    static string Num(JsonElement el, string key, int sig) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble().ToString($"G{sig}")
            : "—";
}
