using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Views
{
public partial class ShellStrainBatchResultView : UserControl
{
    public sealed record Row(int Num, string Label, string Status, int Iterations, string Residual,
                              string Verdict, string Formula);

    public ShellStrainBatchResultView(CalcResult result)
    {
        InitializeComponent();
        var rows = new List<Row>();
        var vm = new ShellStrainBatchResultVM();
        try
        {
            var root = JsonDocument.Parse(result.DataJson).RootElement;
            if (root.TryGetProperty("error", out var err))
            {
                vm.ErrorText = err.GetString() ?? "";
                DataContext = vm;
                return;
            }
            int cc = root.GetProperty("converged_count").GetInt32();
            int total = root.GetProperty("total").GetInt32();
            bool allOk = cc == total;
            vm.StatusBrush = allOk
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(70, 80, 180, 80))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 192, 57, 43));
            vm.SummaryText = string.Format(Loc.S("ShellStrainBatchSummary"), cc, total);
            int idx = 0;
            foreach (var r in root.GetProperty("rows").EnumerateArray())
            {
                idx++;
                string verdict = "";
                if (r.TryGetProperty("status", out var stEl))
                {
                    string s = stEl.GetString() ?? "";
                    verdict = s switch
                    {
                        "ok"            => Loc.S("ShellStrainCheckVerdictOk"),
                        "fail"          => Loc.S("ShellStrainCheckVerdictFail"),
                        "not_converged" => Loc.S("ShellStrainCheckNotConverged"),
                        _               => s,
                    };
                }
                string formula = r.TryGetProperty("formula", out var fe) ? fe.GetString() ?? "" : "";
                rows.Add(new Row(
                    r.TryGetProperty("num", out var nv) && nv.ValueKind == JsonValueKind.Number
                        ? nv.GetInt32() : idx,
                    r.GetProperty("label").GetString() ?? "",
                    r.GetProperty("status").GetString() ?? "",
                    r.GetProperty("iterations").GetInt32(),
                    r.GetProperty("residual").GetDouble().ToString("E3", CultureInfo.InvariantCulture),
                    verdict, formula));
            }
        }
        catch { vm.SummaryText = "—"; }
        vm.Rows = rows;
        DataContext = vm;
    }
}

public class ShellStrainBatchResultVM : ViewModelBase
{
    public string SummaryText { get; set; } = "";
    public string ErrorText { get; set; } = "";
    public System.Windows.Media.Brush StatusBrush { get; set; } = System.Windows.Media.Brushes.LightGray;
    public List<ShellStrainBatchResultView.Row> Rows { get; set; } = [];
}
}
