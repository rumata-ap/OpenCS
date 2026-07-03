using System.IO;
using System.Windows;
using System.Windows.Controls;
using CScore;
using CsvHelper;
using System.Globalization;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Просмотр результата пакетной R-проверки.</summary>
public partial class FireRCheckBatchResultView : UserControl
{
    private readonly AppViewModel _app;
    private readonly CalcTask _task;

    public FireRCheckBatchResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        _app = app;
        _task = task;
        InitializeComponent();
        DataContext = new FireRCheckBatchVM(result);
    }

    private void RowsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var vm = DataContext as FireRCheckBatchVM;
        if (vm == null || vm.AllRows.Count == 0) return;

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

        csv.WriteField("#"); csv.WriteField("Label"); csv.WriteField("Passed");
        csv.WriteField("k"); csv.WriteField("Margin"); csv.WriteField("Governing");
        csv.NextRecord();

        foreach (var r in vm.AllRows)
        {
            csv.WriteField(r.Num); csv.WriteField(r.Label); csv.WriteField(r.PassedText);
            csv.WriteField(r.FactorText); csv.WriteField(r.MarginText); csv.WriteField(r.GoverningText);
            csv.NextRecord();
        }
    }
}
