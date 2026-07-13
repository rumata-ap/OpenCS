using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel пакетного расчёта момента трещинообразования.</summary>
public sealed class CrackingBatchVM : ViewModelBase
{
    public string TaskTag { get; }
    public string CreatedText { get; }
    public string SummaryText { get; }
    public Brush StatusBrush { get; }
    public bool HasError { get; }
    public string ErrorText { get; } = "";

    public ObservableCollection<BatchRow> Rows { get; } = [];

    public sealed record BatchRow(
        int Num, string Label, string NText, string MxCrcText, string MyCrcText,
        string McrcText, string StatusText, bool IsConverged);

    public CrackingBatchVM(CalcResult result)
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
            int converged = root.TryGetProperty("converged_count", out var c) ? c.GetInt32() : 0;
            bool allConverged = root.TryGetProperty("all_converged", out var ac) && ac.GetBoolean();

            StatusBrush = allConverged
                ? new SolidColorBrush(Color.FromArgb(70, 80, 180, 80))
                : Brushes.OrangeRed;
            SummaryText = string.Format(Loc.S("CrackingBatch_SummaryFormat"), total, converged, total - converged);

            if (root.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                int idx = 0;
                foreach (var row in rows.EnumerateArray())
                {
                    idx++;
                    bool conv = row.TryGetProperty("converged", out var cv) && cv.GetBoolean();
                    Rows.Add(new BatchRow(
                        Num: BatchResultRowHelper.RowNum(row, idx),
                        Label: Str(row, "label"),
                        NText: Num(row, "N", 4),
                        MxCrcText: Num(row, "Mx_crc", 4),
                        MyCrcText: Num(row, "My_crc", 4),
                        McrcText: Num(row, "Mcrc", 4),
                        StatusText: conv ? Loc.S("ResultConvergedYes") : Loc.S("ResultConvergedNo"),
                        IsConverged: conv));
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
