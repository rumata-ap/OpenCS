using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CScore;
using CsvHelper;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views
{
public partial class ShellStrainBatchResultView : UserControl
{
    public sealed record Row(int Num, string Label, string Status, int Iterations, string Residual,
                              string Verdict, string Formula);

    private readonly AppViewModel _app;
    private readonly CalcTask _task;

    public ShellStrainBatchResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        _app = app;
        _task = task;
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
                ? new SolidColorBrush(Color.FromArgb(70, 80, 180, 80))
                : new SolidColorBrush(Color.FromArgb(60, 192, 57, 43));
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

        RowsGrid.SelectionChanged += (_, _) =>
        {
            CreateTaskBtn.IsEnabled = RowsGrid.SelectedItem != null;
        };
    }

    private Row? SelectedRow
        => RowsGrid.SelectedItem as Row;

    private void RowsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SelectedRow != null)
            CreateTask();
    }

    private void CreateTask_Click(object sender, RoutedEventArgs e)
        => CreateTask();

    private void CreateTask()
    {
        var row = SelectedRow;
        if (row == null) return;

        var section = _app.CrossSections.FirstOrDefault(s => s.Id == _task.SectionId);
        if (section == null)
        {
            MessageBox.Show(Loc.S("CalcTaskSectionNotFound"), Loc.S("Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var singleKind = _task.Kind.Replace("_batch", "");
        var tag = $"{section.Tag} — {row.Label}";

        var newTask = new CalcTask
        {
            Kind = singleKind,
            SectionId = _task.SectionId,
            CalcType = _task.CalcType,
            Tag = tag,
            ParamsJson = "{}"
        };

        newTask.Num = _app.CalcTasks.Count > 0 ? _app.CalcTasks.Max(t => t.Num) + 1 : 1;
        _app.db.SaveCalcTask(newTask);
        _app.LogService.Info(string.Format(Loc.S("CalcTaskCreated"), tag));
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var vm = DataContext as ShellStrainBatchResultVM;
        if (vm == null || vm.Rows.Count == 0) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = Loc.S("ExportCsv") + "|*.csv",
            DefaultExt = ".csv",
            FileName = $"{_task.Tag}_batch.csv"
        };
        if (dlg.ShowDialog() != true) return;

        using var writer = new StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8);
        var cfg = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        };
        using var csv = new CsvWriter(writer, cfg);

        csv.WriteField("#"); csv.WriteField("Label"); csv.WriteField("Status");
        csv.WriteField("Verdict"); csv.WriteField("Formula");
        csv.WriteField("Iter"); csv.WriteField("Residual");
        csv.NextRecord();

        foreach (var r in vm.Rows)
        {
            csv.WriteField(r.Num); csv.WriteField(r.Label); csv.WriteField(r.Status);
            csv.WriteField(r.Verdict); csv.WriteField(r.Formula);
            csv.WriteField(r.Iterations); csv.WriteField(r.Residual);
            csv.NextRecord();
        }
    }
}

public class ShellStrainBatchResultVM : ViewModelBase
{
    public string SummaryText { get; set; } = "";
    public string ErrorText { get; set; } = "";
    public Brush StatusBrush { get; set; } = Brushes.LightGray;
    public List<ShellStrainBatchResultView.Row> Rows { get; set; } = [];
}
}
