using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CsvHelper;
using CsvHelper.Configuration;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Сводка пакетного расчёта наклонных сечений.</summary>
public partial class ShearInclinedBatchResultView : UserControl
{
    /// <summary>Создаёт сводку по DataJson пакетной задачи.</summary>
    public ShearInclinedBatchResultView(string dataJson)
    {
        InitializeComponent();
        DataContext = new ShearInclinedBatchVM(dataJson);
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
