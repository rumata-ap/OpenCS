using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CScore;
using CsvHelper;
using System.Globalization;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class LimitForceBatchResultView : UserControl
{
    private readonly AppViewModel _app;
    private readonly CalcTask _task;
    private readonly string _singleKind;

    public LimitForceBatchResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        _app = app;
        _task = task;
        _singleKind = task.Kind.Replace("_batch", "");
        InitializeComponent();
        var section = app.CrossSections.FirstOrDefault(s => s.Id == task.SectionId);
        DataContext = new LimitForceBatchVM(result, section, app.CalcSettings);
        RowsGrid.SelectionChanged += (_, _) =>
        {
            CreateTaskBtn.IsEnabled = RowsGrid.SelectedItem != null;
        };
    }

    private LimitForceBatchVM.BatchRow? SelectedRow
        => RowsGrid.SelectedItem as LimitForceBatchVM.BatchRow;

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

        var tag = $"{section.Tag} — {row.Label}";

        var newTask = new CalcTask
        {
            Kind = _singleKind,
            SectionId = _task.SectionId,
            CalcType = _task.CalcType,
            Tag = tag,
            ParamsJson = $"{{\"N\":{row.NText.Replace(',', '.')},\"Mx\":{row.MxText.Replace(',', '.')},\"My\":{row.MyText.Replace(',', '.')},\"solver\":\"bisection\"}}"
        };

        newTask.Num = _app.CalcTasks.Count > 0 ? _app.CalcTasks.Max(t => t.Num) + 1 : 1;
        _app.db.SaveCalcTask(newTask);
        _app.LogService.Info(string.Format(Loc.S("CalcTaskCreated"), tag));
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var vm = DataContext as LimitForceBatchVM;
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

        csv.WriteField("#"); csv.WriteField("Label"); csv.WriteField("N");
        csv.WriteField("Mx"); csv.WriteField("My"); csv.WriteField("k");
        csv.WriteField("Utilization"); csv.WriteField("Governing");
        csv.WriteField("Iter"); csv.WriteField("Status");
        csv.WriteField("etaX"); csv.WriteField("etaY");
        csv.NextRecord();

        foreach (var r in vm.Rows)
        {
            csv.WriteField(r.Num); csv.WriteField(r.Label);
            csv.WriteField(r.NText); csv.WriteField(r.MxText); csv.WriteField(r.MyText);
            csv.WriteField(r.FactorText); csv.WriteField(r.UtilText); csv.WriteField(r.GovText);
            csv.WriteField(r.IterText); csv.WriteField(r.StatusText);
            csv.WriteField(r.EtaXText); csv.WriteField(r.EtaYText);
            csv.NextRecord();
        }
    }
}
