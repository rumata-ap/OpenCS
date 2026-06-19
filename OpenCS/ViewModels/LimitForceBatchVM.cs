using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel пакетного расчёта предельного нагружения.</summary>
public sealed class LimitForceBatchVM : ViewModelBase
{
    public string TaskTag     { get; }
    public string CreatedText { get; }
    public string SummaryText { get; }
    public Brush  StatusBrush { get; }
    public bool   HasError    { get; }
    public string ErrorText   { get; } = "";
    public string SolverText  { get; }

    public ObservableCollection<BatchRow> Rows { get; } = [];

    public sealed record BatchRow(
        int Num,
        string Label,
        string NText,
        string MxText,
        string MyText,
        string FactorText,
        string UtilText,
        string GovText,
        string IterText,
        string StatusText,
        bool   IsConverged);

    public LimitForceBatchVM(CalcResult result)
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
            SolverText  = "";
            return;
        }

        try
        {
            var doc  = JsonDocument.Parse(result.DataJson);
            var root = doc.RootElement;

            int  total     = root.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
            int  converged = root.TryGetProperty("converged_count", out var c) ? c.GetInt32() : 0;
            bool allOk     = root.TryGetProperty("all_converged", out var ac) && ac.GetBoolean();
            string solver  = root.TryGetProperty("solver", out var sv) ? sv.GetString() ?? "" : "";

            SolverText  = solver == "fast" ? Loc.S("LimitForceSolver_Fast") : Loc.S("LimitForceSolver_Bisection");
            StatusBrush = allOk ? Brushes.ForestGreen : Brushes.OrangeRed;
            SummaryText = string.Format(Loc.S("LimitForceBatch_SummaryFormat"),
                total, converged, total - converged);

            if (root.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                int idx = 0;
                foreach (var row in rows.EnumerateArray())
                {
                    idx++;
                    string st   = row.TryGetProperty("status", out var sv2) ? sv2.GetString() ?? "" : "";
                    bool   conv = st == "ok";
                    string gov  = row.TryGetProperty("governing", out var gv) ? gv.GetString() ?? "" : "";
                    string govT = gov switch
                    {
                        "concrete" => Loc.S("LimitForceGov_Concrete"),
                        "rebar"    => Loc.S("LimitForceGov_Rebar"),
                        "both"     => Loc.S("LimitForceGov_Both"),
                        _          => "—"
                    };

                    Rows.Add(new BatchRow(
                        Num:        BatchResultRowHelper.RowNum(row, idx),
                        Label:      Str(row, "label"),
                        NText:      Num(row, "N", 4),
                        MxText:     Num(row, "Mx", 4),
                        MyText:     Num(row, "My", 4),
                        FactorText: Num(row, "factor", 4),
                        UtilText:   Num(row, "utilization", 4),
                        GovText:    govT,
                        IterText:   row.TryGetProperty("iterations", out var iv) ? iv.GetInt32().ToString() : "—",
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
            SolverText  = "";
        }
    }

    static string Str(JsonElement row, string prop)
        => row.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    static string Num(JsonElement row, string prop, int dec)
    {
        if (!row.TryGetProperty(prop, out var v)) return "—";
        return v.GetDouble().ToString($"0.{new string('0', dec)}");
    }
}
