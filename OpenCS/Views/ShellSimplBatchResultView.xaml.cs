using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CScore;
using CsvHelper;
using System.Globalization;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class ShellSimplBatchResultView : System.Windows.Controls.UserControl
{
    private readonly AppViewModel _app;
    private readonly CalcTask _task;

    public ShellSimplBatchResultView(CalcResult result, CalcTask task, AppViewModel app)
    {
        _app = app;
        _task = task;
        InitializeComponent();
        DataContext = new ShellSimplBatchResultVM(result, task);
        RowsGrid.SelectionChanged += (_, _) =>
        {
            CreateTaskBtn.IsEnabled = RowsGrid.SelectedItem != null;
        };
    }

    private ShellSimplBatchRow? SelectedRow
        => RowsGrid.SelectedItem as ShellSimplBatchRow;

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

        var forceSet = _app.ShellForceSets.FirstOrDefault(f => f.Id == _task.ForceSetId)
                    ?? _app.BarForceSets.FirstOrDefault(f => f.Id == _task.ForceSetId);
        if (forceSet == null)
        {
            MessageBox.Show(Loc.S("CalcTaskForceItemNotFound"), Loc.S("Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var shellItem = forceSet.ShellItems.FirstOrDefault(si => si.Num == row.Num);
        if (shellItem == null)
        {
            MessageBox.Show(Loc.S("CalcTaskForceItemNotFound"), Loc.S("Error"),
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
            ForceSetId = _task.ForceSetId,
            ParamsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                nx = shellItem.Nx, ny = shellItem.Ny, nxy = shellItem.Nxy,
                mx = shellItem.Mx, my = shellItem.My, mxy = shellItem.Mxy,
                step_deg = 10.0, acrc_lim_mm = 0.3, phi1 = 1.0, phi2 = 0.5
            })
        };

        newTask.Num = _app.CalcTasks.Count > 0 ? _app.CalcTasks.Max(t => t.Num) + 1 : 1;
        _app.db.SaveCalcTask(newTask);
        _app.LogService.Info(string.Format(Loc.S("CalcTaskCreated"), tag));
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var vm = DataContext as ShellSimplBatchResultVM;
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

        csv.WriteField("#"); csv.WriteField("Label"); csv.WriteField("η_max"); csv.WriteField("Status");
        csv.NextRecord();

        foreach (var r in vm.Rows)
        {
            csv.WriteField(r.Num); csv.WriteField(r.Label); csv.WriteField(r.EtaMaxDisplay); csv.WriteField(r.Status);
            csv.NextRecord();
        }
    }
}

public class ShellSimplBatchRow : ViewModelBase
{
    public int Num { get; set; }
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
    public Brush StatusBrush { get; }
    public ObservableCollection<ShellSimplBatchRow> Rows { get; } = [];

    public ShellSimplBatchResultVM(CalcResult result, CalcTask task)
    {
        Title = task.Tag;
        var doc = JsonSerializer.Deserialize<JsonElement>(result.DataJson);

        int total = doc.GetProperty("total").GetInt32();
        int okCount = doc.GetProperty("converged_count").GetInt32();
        bool allOk = okCount == total;

        StatusBrush = allOk
            ? new SolidColorBrush(Color.FromArgb(70, 80, 180, 80))
            : new SolidColorBrush(Color.FromArgb(60, 192, 57, 43));

        int idx = 0;
        foreach (var r in doc.GetProperty("rows").EnumerateArray())
        {
            idx++;
            var row = new ShellSimplBatchRow
            {
                Num = r.TryGetProperty("num", out var nv) && nv.ValueKind == JsonValueKind.Number
                    ? nv.GetInt32() : idx,
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
