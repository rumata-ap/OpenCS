using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel пакетного расчёта состояния деформаций.</summary>
public sealed class StrainStateBatchVM : ViewModelBase
{
    public string TaskTag     { get; }
    public string CreatedText { get; }
    public string SummaryText { get; }
    public Brush  StatusBrush { get; }
    public bool   HasError    { get; }
    public string ErrorText   { get; } = "";

    public bool   ShowRebarAreaNote { get; }
    public string RebarAreaNote    { get; } = "";

    public ObservableCollection<BatchRow> Rows { get; } = [];

    public sealed record BatchRow(
        int Num,
        string Label,
        string NText,
        string MxText,
        string MyText,
        string E0Text,
        string KyText,
        string KzText,
        string IterText,
        string ResText,
        string StatusText,
        bool   IsConverged);

    public StrainStateBatchVM(CalcResult result, CrossSection? section = null, CalcSettings? settings = null)
    {
        TaskTag     = result.TaskTag;
        CreatedText = result.Created;
        ShowRebarAreaNote = section != null && StrainSummaryVM.ShouldShowRebarAreaNote(section, settings);
        RebarAreaNote     = ShowRebarAreaNote ? Loc.S("ResultRebarAreaReductionNote") : "";

        if (result.Status == "error")
        {
            HasError = true;
            try
            {
                var doc = JsonDocument.Parse(result.DataJson);
                ErrorText = doc.RootElement.TryGetProperty("error", out var e)
                    ? e.GetString() ?? "" : result.DataJson;
            }
            catch { ErrorText = result.DataJson; }
            SummaryText = Loc.S("CalcResultErrorLabel");
            StatusBrush = Brushes.DarkRed;
            return;
        }

        try
        {
            var doc  = JsonDocument.Parse(result.DataJson);
            var root = doc.RootElement;

            int  total     = root.TryGetProperty("total",           out var t)  ? t.GetInt32()     : 0;
            int  converged = root.TryGetProperty("converged_count", out var c)  ? c.GetInt32()     : 0;
            bool allOk     = root.TryGetProperty("all_converged",   out var ac) && ac.GetBoolean();

            StatusBrush = allOk
                ? new SolidColorBrush(Color.FromArgb(70, 80, 180, 80))
                : Brushes.OrangeRed;
            SummaryText = string.Format(Loc.S("StrainStateBatch_SummaryFormat"),
                total, converged, total - converged);

            if (root.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                int idx = 0;
                foreach (var row in rows.EnumerateArray())
                {
                    idx++;
                    string st   = row.TryGetProperty("status", out var sv) ? sv.GetString() ?? "" : "";
                    bool   conv = st == "ok";
                    int    iter = row.TryGetProperty("iterations", out var iv) ? iv.GetInt32() : 0;

                    Rows.Add(new BatchRow(
                        Num:        BatchResultRowHelper.RowNum(row, idx),
                        Label:      Str(row, "label"),
                        NText:      Num(row, "N",        4),
                        MxText:     Num(row, "Mx",       4),
                        MyText:     Num(row, "My",       4),
                        E0Text:     Num(row, "e0",       8),
                        KyText:     Num(row, "ky",       8),
                        KzText:     Num(row, "kz",       8),
                        IterText:   iter.ToString(),
                        ResText:    Num(row, "residual", 3),
                        StatusText: conv ? Loc.S("StrainStateBatch_StatusOk")
                                        : Loc.S("StrainStateBatch_StatusNotConverged"),
                        IsConverged: conv));
                }
            }
        }
        catch (Exception ex)
        {
            HasError    = true;
            ErrorText   = ex.Message;
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
