using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenCS.Views;

public partial class FemCheckResultView : UserControl
{
    public FemCheckResultView(CScore.CalcResult result)
    {
        InitializeComponent();
        DataContext = new FemCheckResultVM(result.DataJson);
    }
}

public class FemCheckResultVM
{
    public string  SummaryText  { get; }
    public Brush   SummaryBrush { get; }
    public List<FemCheckRowVM> Rows { get; }

    public FemCheckResultVM(string dataJson)
    {
        Rows = [];
        try
        {
            using var doc  = JsonDocument.Parse(dataJson);
            var root = doc.RootElement;

            int total  = root.TryGetProperty("totalRows",  out var t) ? t.GetInt32() : 0;
            int passed = root.TryGetProperty("passedRows", out var p) ? p.GetInt32() : 0;
            int failed = root.TryGetProperty("failedRows", out var f) ? f.GetInt32() : 0;

            SummaryText  = string.Format(Utilites.Loc.S("FemCheckResultSummary"), passed, failed, total);
            SummaryBrush = failed == 0
                ? new SolidColorBrush(Color.FromArgb(60, 46, 122, 62))
                : new SolidColorBrush(Color.FromArgb(60, 192, 57, 43));

            if (root.TryGetProperty("rows", out var rowsArr))
            {
                foreach (var r in rowsArr.EnumerateArray())
                {
                    Rows.Add(new FemCheckRowVM
                    {
                        Label        = r.TryGetProperty("label",            out var l)  ? l.GetString()  ?? "" : "",
                        ForceSetTag  = r.TryGetProperty("forceSetTag",      out var fs) ? fs.GetString() ?? "" : "",
                        CalcType     = r.TryGetProperty("calcType",         out var ct) ? ct.GetString() ?? "" : "",
                        Utilization  = r.TryGetProperty("utilization",      out var u)  ? u.GetDouble()  : 0,
                        Passed       = r.TryGetProperty("passed",           out var ps) && ps.GetBoolean(),
                        WorstFormula = r.TryGetProperty("worstFormula",     out var wf) ? wf.GetString() ?? "" : "",
                        WorstDesc    = r.TryGetProperty("worstDescription", out var wd) ? wd.GetString() ?? "" : "",
                    });
                }
                Rows = [.. Rows.OrderByDescending(r => r.Utilization)];
            }
        }
        catch
        {
            SummaryText  = "Ошибка чтения результата";
            SummaryBrush = Brushes.LightGray;
        }
    }
}

public class FemCheckRowVM
{
    public string Label        { get; init; } = "";
    public string ForceSetTag  { get; init; } = "";
    public string CalcType     { get; init; } = "";
    public double Utilization  { get; init; }
    public bool   Passed       { get; init; }
    public string WorstFormula { get; init; } = "";
    public string WorstDesc    { get; init; } = "";

    public string UtilText       => Utilization.ToString("F3");
    public string WorstCheckText => string.IsNullOrEmpty(WorstFormula) ? WorstDesc : $"{WorstFormula} {WorstDesc}";
    public string StatusText     => Passed ? "✓" : "✗";
}
