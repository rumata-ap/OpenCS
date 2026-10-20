using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CsvHelper;
using CsvHelper.Configuration;
using CScore;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Сводка пакетного расчёта наклонных сечений.</summary>
public partial class ShearInclinedBatchResultView : UserControl
{
    readonly AppViewModel _app;
    readonly CalcTask _task;

    /// <summary>Создаёт сводку по DataJson пакетной задачи.</summary>
    public ShearInclinedBatchResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        InitializeComponent();
        _app = app;
        _task = task;
        DataContext = new ShearInclinedBatchVM(result.DataJson);
        RowsGrid.SelectionChanged += (_, _) => CreateTaskBtn.IsEnabled = RowsGrid.SelectedItem != null;
    }

    void RowsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => CreateTask();

    void CreateTask_Click(object sender, RoutedEventArgs e) => CreateTask();

    void CreateTask()
    {
        if (RowsGrid.SelectedItem is ShearInclinedBatchRowVM row)
            BatchRowTask.CreateAndRun(_app, _task, row.Num);
    }

    /// <summary>Выгружает сводку в CSV.</summary>
    void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShearInclinedBatchVM vm || vm.Rows.Count == 0) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = Loc.S("ExportCsv") + "|*.csv",
            DefaultExt = ".csv",
            FileName = $"SP63_inclined_batch_{vm.SectionTag}.csv"
        };
        if (dlg.ShowDialog() != true) return;

        using var writer = new StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8);
        using var csv = new CsvWriter(writer,
            new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" });

        csv.WriteField("#"); csv.WriteField("Строка усилий");
        csv.WriteField("Vy"); csv.WriteField("Vx");
        csv.WriteField("Коэфф."); csv.WriteField("Худшая проверка"); csv.WriteField("Результат");
        csv.NextRecord();

        foreach (var row in vm.Rows)
        {
            csv.WriteField(row.Num); csv.WriteField(row.Label);
            csv.WriteField(row.Vy); csv.WriteField(row.Vx);
            csv.WriteField(row.UtilizationText); csv.WriteField(row.WorstFormula);
            csv.WriteField(row.StatusText);
            csv.NextRecord();
        }

        csv.NextRecord();
        foreach (var caution in vm.Cautions)
        {
            csv.WriteField(caution);
            csv.NextRecord();
        }
    }
}
