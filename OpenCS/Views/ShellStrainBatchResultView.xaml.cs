using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Windows.Controls;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Views
{
    public partial class ShellStrainBatchResultView : UserControl
    {
        public sealed record Row(int Num, string Label, string Status, int Iterations, string Residual);

        public ShellStrainBatchResultView(CalcResult result)
        {
            InitializeComponent();
            var rows = new List<Row>();
            try
            {
                var root = JsonDocument.Parse(result.DataJson).RootElement;
                if (root.TryGetProperty("error", out var err))
                {
                    SummaryText.Text = err.GetString();
                    return;
                }
                int cc = root.GetProperty("converged_count").GetInt32();
                int total = root.GetProperty("total").GetInt32();
                SummaryText.Text = string.Format(Loc.S("ShellStrainBatchSummary"), cc, total);
                int idx = 0;
                foreach (var r in root.GetProperty("rows").EnumerateArray())
                {
                    idx++;
                    rows.Add(new Row(
                        r.TryGetProperty("num", out var nv) && nv.ValueKind == JsonValueKind.Number
                            ? nv.GetInt32() : idx,
                        r.GetProperty("label").GetString() ?? "",
                        r.GetProperty("status").GetString() ?? "",
                        r.GetProperty("iterations").GetInt32(),
                        r.GetProperty("residual").GetDouble().ToString("E3", CultureInfo.InvariantCulture)));
                }
            }
            catch { SummaryText.Text = "—"; }
            RowsGrid.ItemsSource = rows;
        }
    }
}
