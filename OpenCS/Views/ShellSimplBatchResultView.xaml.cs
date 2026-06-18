using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Views;

public partial class ShellSimplBatchResultView : System.Windows.Controls.UserControl
{
    public ShellSimplBatchResultView(CalcResult result, CalcTask task)
    {
        InitializeComponent();
        DataContext = new ShellSimplBatchResultVM(result, task);
    }
}

public class ShellSimplBatchRow : ViewModelBase
{
    public string Label { get; set; } = "";
    public double EtaMax { get; set; }
    public string EtaMaxDisplay => EtaMax >= 1e9 ? "∞" : Math.Round(EtaMax, 2).ToString("F2");
    public string Status { get; set; } = "";
    public bool Ok { get; set; }
}

public class ShellSimplBatchResultVM : ViewModelBase
{
    public string Title { get; }
    public string Summary { get; }
    public ObservableCollection<ShellSimplBatchRow> Rows { get; } = [];

    public ShellSimplBatchResultVM(CalcResult result, CalcTask task)
    {
        Title = task.Tag;
        var doc = JsonSerializer.Deserialize<JsonElement>(result.DataJson);

        int total = doc.GetProperty("total").GetInt32();
        int okCount = doc.GetProperty("converged_count").GetInt32();

        foreach (var r in doc.GetProperty("rows").EnumerateArray())
        {
            var row = new ShellSimplBatchRow
            {
                Label = r.GetProperty("label").GetString() ?? "",
                Status = r.GetProperty("status").GetString() == "ok" ? "Выполняется" : "Не выполняется",
                Ok = r.GetProperty("status").GetString() == "ok",
            };
            if (r.TryGetProperty("eta_max", out var em) && em.ValueKind == System.Text.Json.JsonValueKind.Number)
                row.EtaMax = em.GetDouble();
            Rows.Add(row);
        }

        Summary = $"Всего: {total}, выполнено: {okCount}, нарушений: {total - okCount}";
    }
}
