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

    public bool   ShowRebarAreaNote { get; }
    public string RebarAreaNote    { get; } = "";

    public ObservableCollection<BatchRow> Rows { get; } = [];

    /// <summary>
    /// Нижняя граница Кисп «на пределе»: запас меньше 1 %. Сверху граница — 1 по четырём
    /// знакам, как в таблице: всё, что показано больше 1,0000, — «не проходит».
    /// </summary>
    public const double NearLimitUtilization = 0.99;

    /// <summary>Вердикт строки по Кисп — для окраски строки и счётчиков шапки.</summary>
    public enum RowVerdict { Pass, NearLimit, Fail, NotConverged }

    public sealed record BatchRow(
        int Num,
        string Label,
        string NText,
        string MxText,
        string MyText,
        string NLimText,
        string MxLimText,
        string MyLimText,
        string FactorText,
        string UtilText,
        string GovText,
        string IterText,
        string StatusText,
        bool   IsConverged,
        RowVerdict Verdict,
        string EtaXText,
        string EtaYText);

    /// <summary>
    /// Вердикт по Кисп: null — Кисп = ∞ (фиксированная часть сечению не по силам) — не проходит.
    /// </summary>
    public static RowVerdict Classify(bool converged, double? utilization)
    {
        if (!converged) return RowVerdict.NotConverged;
        if (utilization is not { } u) return RowVerdict.Fail;
        u = Math.Round(u, 4);
        if (u > 1.0) return RowVerdict.Fail;
        return u >= NearLimitUtilization ? RowVerdict.NearLimit : RowVerdict.Pass;
    }

    public LimitForceBatchVM(CalcResult result, CrossSection? section = null, CalcSettings? settings = null)
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

                    string etaXText = "—", etaYText = "—";
                    if (row.TryGetProperty("eta", out var etaEl) && etaEl.ValueKind != JsonValueKind.Null)
                    {
                        bool slenderX = etaEl.TryGetProperty("slenderX", out var sxEl) && sxEl.GetBoolean();
                        bool slenderY = etaEl.TryGetProperty("slenderY", out var syEl) && syEl.GetBoolean();
                        bool stableX  = !etaEl.TryGetProperty("stableX", out var stxEl) || stxEl.GetBoolean();
                        bool stableY  = !etaEl.TryGetProperty("stableY", out var styEl) || styEl.GetBoolean();
                        double etaXv  = etaEl.TryGetProperty("etaX", out var exEl)
                            && exEl.ValueKind == JsonValueKind.Number ? exEl.GetDouble() : 1.0;
                        double etaYv  = etaEl.TryGetProperty("etaY", out var eyEl)
                            && eyEl.ValueKind == JsonValueKind.Number ? eyEl.GetDouble() : 1.0;
                        etaXText = FormatEta(etaXv, slenderX, stableX);
                        etaYText = FormatEta(etaYv, slenderY, stableY);
                    }

                    double? util = row.TryGetProperty("utilization", out var uEl)
                        && uEl.ValueKind == JsonValueKind.Number ? uEl.GetDouble() : null;

                    Rows.Add(new BatchRow(
                        Num:        BatchResultRowHelper.RowNum(row, idx),
                        Label:      Str(row, "label"),
                        NText:      Num(row, "N", 4),
                        MxText:     Num(row, "Mx", 4),
                        MyText:     Num(row, "My", 4),
                        NLimText:   conv ? Num(row, "N_limit", 4) : "—",
                        MxLimText:  conv ? Num(row, "Mx_limit", 4) : "—",
                        MyLimText:  conv ? Num(row, "My_limit", 4) : "—",
                        FactorText: Num(row, "factor", 4),
                        UtilText:   Num(row, "utilization", 4, nullText: "∞"),
                        GovText:    govT,
                        IterText:   row.TryGetProperty("iterations", out var iv) ? iv.GetInt32().ToString() : "—",
                        StatusText: conv ? Loc.S("ResultConvergedYes") : Loc.S("ResultConvergedNo"),
                        IsConverged: conv,
                        Verdict:    Classify(conv, util),
                        EtaXText: etaXText,
                        EtaYText: etaYText));
                }
            }

            int pass = 0, near = 0, fail = 0;
            foreach (var r in Rows)
            {
                if (r.Verdict == RowVerdict.Pass) pass++;
                else if (r.Verdict == RowVerdict.NearLimit) near++;
                else if (r.Verdict == RowVerdict.Fail) fail++;
            }
            // Шапка как у пакетной проверки прочности: зелёная, только если все строки сошлись
            // и прошли; «на пределе» — жёлтая; хоть одно превышение или несходимость — красная.
            StatusBrush = !allOk || fail > 0
                ? Brushes.OrangeRed
                : near > 0
                    ? new SolidColorBrush(Color.FromArgb(110, 241, 196, 15))
                    : new SolidColorBrush(Color.FromArgb(70, 80, 180, 80));
            SummaryText = string.Format(Loc.S("LimitForceBatch_SummaryFormat"),
                total, pass, near, fail, converged, total - converged);
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

    /// <summary>Число строки; null — бесконечная величина (Кисп = ∞, если фиксированная часть усилий сечению не по силам).</summary>
    static string Num(JsonElement row, string prop, int dec, string nullText = "—")
    {
        if (!row.TryGetProperty(prop, out var v)) return "—";
        if (v.ValueKind == JsonValueKind.Null) return nullText;
        return v.GetDouble().ToString($"0.{new string('0', dec)}");
    }

    /// <summary>Значение η для одной оси (п. 8.1.15 СП63.13330) — как в StrainSummaryVM.</summary>
    static string FormatEta(double eta, bool slender, bool stable)
    {
        if (!slender) return "1.000";
        if (!stable)  return Loc.S("ResultEtaInstable");
        return $"{eta:0.000}";
    }
}
