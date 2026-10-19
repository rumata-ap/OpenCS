using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CScore;
using CsvHelper;
using System.Globalization;
using OpenCS.Utilites;
using OpenCS.Services;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Просмотр результата пакетного расчёта состояния деформаций.</summary>
public partial class StrainStateBatchResultView : UserControl
{
    private readonly AppViewModel _app;
    private readonly CalcTask _task;

    public StrainStateBatchResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        _app = app;
        _task = task;
        InitializeComponent();
        var section = app.CrossSections.FirstOrDefault(s => s.Id == task.SectionId);
        DataContext = new StrainStateBatchVM(result, section, app.CalcSettings);
        RowsGrid.SelectionChanged += (_, _) =>
        {
            CreateTaskBtn.IsEnabled = RowsGrid.SelectedItem != null;
        };
    }

    private StrainStateBatchVM.BatchRow? SelectedRow
        => RowsGrid.SelectedItem as StrainStateBatchVM.BatchRow;

    private void RowsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SelectedRow != null)
            CreateTask();
    }

    private void CreateTask_Click(object sender, RoutedEventArgs e)
        => CreateTask();

    private void CreateTask()
    {
        if (SelectedRow is { } row)
            BatchRowTask.CreateAndRun(_app, _task, row.Num);
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var vm = DataContext as StrainStateBatchVM;
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
        csv.WriteField("Mx"); csv.WriteField("My"); csv.WriteField("e0");
        csv.WriteField("ky"); csv.WriteField("kz"); csv.WriteField("Iter");
        csv.WriteField("Residual"); csv.WriteField("Status");
        csv.WriteField("Mx,eff"); csv.WriteField("My,eff");
        csv.WriteField("etaX"); csv.WriteField("etaY");
        csv.NextRecord();

        foreach (var r in vm.Rows)
        {
            csv.WriteField(r.Num); csv.WriteField(r.Label);
            csv.WriteField(r.NText); csv.WriteField(r.MxText); csv.WriteField(r.MyText);
            csv.WriteField(r.E0Text); csv.WriteField(r.KyText); csv.WriteField(r.KzText);
            csv.WriteField(r.IterText); csv.WriteField(r.ResText); csv.WriteField(r.StatusText);
            csv.WriteField(r.MxEffText); csv.WriteField(r.MyEffText);
            csv.WriteField(r.EtaXText); csv.WriteField(r.EtaYText);
            csv.NextRecord();
        }
    }
}
