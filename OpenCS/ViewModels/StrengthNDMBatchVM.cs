using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel пакетной проверки прочности по НДМ.</summary>
public sealed class StrengthNDMBatchVM : ViewModelBase
{
    public string TaskTag     { get; }
    public string CreatedText { get; }
    public string SummaryText { get; }
    public Brush  StatusBrush { get; }
    public bool   HasError    { get; }
    public string ErrorText   { get; } = "";

    public ObservableCollection<BatchRow> Rows { get; } = [];

    public sealed record BatchRow(
        string Label,
        string NText,
        string MxText,
        string MyText,
        string EpsConcreteCompText,
        string EpsConcreteUltText,
        string EpsRebarTensionText,
        string EpsRebarUltText,
        string ConcreteOkText,
        string RebarOkText,
        string StrengthOkText,
        string IterText,
        string StatusText,
        bool   IsPassed,
        bool   IsConverged);

    public StrengthNDMBatchVM(CalcResult result)
    {
        TaskTag     = result.TaskTag;
        CreatedText = result.Created;

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
            int  passed    = root.TryGetProperty("passed_count",    out var pc) ? pc.GetInt32()     : 0;
            bool allOk     = root.TryGetProperty("all_passed",     out var ap) && ap.GetBoolean();

            StatusBrush = allOk ? Brushes.ForestGreen : Brushes.OrangeRed;
            SummaryText = string.Format(Loc.S("StrengthNDMBatch_SummaryFormat"),
                total, passed, total - passed, converged);

            if (root.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rows.EnumerateArray())
                {
                    string st     = row.TryGetProperty("status",        out var sv)  ? sv.GetString() ?? ""  : "";
                    bool   conv   = st == "ok";
                    bool   passedRow = row.TryGetProperty("strength_ok", out var sk) && sk.GetBoolean();
                    bool   cOk    = row.TryGetProperty("concrete_ok",   out var ck) && ck.GetBoolean();
                    bool   rOk    = row.TryGetProperty("rebar_ok",      out var rk) && rk.GetBoolean();

                    Rows.Add(new BatchRow(
                        Label:            Str(row, "label"),
                        NText:            Num(row, "N", 4),
                        MxText:           Num(row, "Mx", 4),
                        MyText:           Num(row, "My", 4),
                        EpsConcreteCompText:  Num(row, "eps_concrete_compression", 6),
                        EpsConcreteUltText: Num(row, "eps_concrete_ult", 6),
                        EpsRebarTensionText: Num(row, "eps_rebar_tension", 6),
                        EpsRebarUltText:  Num(row, "eps_rebar_ult", 6),
                        ConcreteOkText:   cOk ? Loc.S("StrengthNDM_Passed") : Loc.S("StrengthNDM_NotPassed"),
                        RebarOkText:      rOk ? Loc.S("StrengthNDM_Passed") : Loc.S("StrengthNDM_NotPassed"),
                        StrengthOkText:   passedRow ? Loc.S("StrengthNDM_Passed") : Loc.S("StrengthNDM_NotPassed"),
                        IterText:         row.TryGetProperty("iterations", out var iv) ? iv.GetInt32().ToString() : "—",
                        StatusText:       conv ? Loc.S("ResultConvergedYes") : Loc.S("ResultConvergedNo"),
                        IsPassed:         passedRow,
                        IsConverged:      conv));
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
