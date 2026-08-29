using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CsvHelper;
using CsvHelper.Configuration;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Отчёт по расчёту наклонных сечений СП 63.13330.</summary>
public partial class ShearInclinedResultView : UserControl
{
    /// <summary>Создаёт отчёт по DataJson результата задачи.</summary>
    public ShearInclinedResultView(string dataJson)
    {
        InitializeComponent();
        var vm = new ShearInclinedResultVM(dataJson);
        DataContext = vm;
        EtaCanvas.Stations = vm.Stations;
    }

    /// <summary>Открывает диаграммы по проекции отдельно для каждой плоскости.</summary>
    void ShowProjection_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShearInclinedResultVM vm) return;

        var charts = vm.BuildProjectionCharts();
        if (charts.Count == 0) return;

        new ShearInclinedProjectionDialog(charts)
        {
            Owner = Window.GetWindow(this)
        }.ShowDialog();
    }

    /// <summary>Выгружает проверки и стоянки в CSV.</summary>
    void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShearInclinedResultVM vm || vm.Groups.Count == 0) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = Loc.S("ExportCsv") + "|*.csv",
            DefaultExt = ".csv",
            FileName = $"SP63_inclined_{vm.SectionTag}.csv"
        };
        if (dlg.ShowDialog() != true) return;

        using var writer = new StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8);
        using var csv = new CsvWriter(writer,
            new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" });

        csv.WriteField("Плоскость"); csv.WriteField("Формула"); csv.WriteField("Проверка");
        csv.WriteField("Норма"); csv.WriteField("Действующее"); csv.WriteField("Предельное");
        csv.WriteField("Коэфф."); csv.WriteField("Результат"); csv.WriteField("Трассировка");
        csv.NextRecord();

        foreach (var group in vm.Groups)
            foreach (var item in group.Items)
            {
                csv.WriteField(item.Plane); csv.WriteField(item.Formula);
                csv.WriteField(item.Description); csv.WriteField(item.NormRef);
                csv.WriteField(item.AppliedText); csv.WriteField(item.AllowableText);
                csv.WriteField(item.RatioText); csv.WriteField(item.PassedText);
                csv.WriteField(item.Trace);
                csv.NextRecord();
            }

        csv.NextRecord();
        csv.WriteField("Плоскость"); csv.WriteField("s"); csv.WriteField("Растянутая грань");
        csv.WriteField("Q"); csv.WriteField("Qb"); csv.WriteField("Qsw");
        csv.WriteField("C"); csv.WriteField("eta");
        csv.WriteField("M(точка 0)"); csv.WriteField("Ms"); csv.WriteField("Msw");
        csv.WriteField("C по M"); csv.WriteField("etaM");
        csv.NextRecord();

        foreach (var station in vm.Stations)
        {
            csv.WriteField(station.Plane); csv.WriteField(station.S);
            csv.WriteField(station.TensionSideText);
            csv.WriteField(station.Q); csv.WriteField(station.Qb); csv.WriteField(station.Qsw);
            csv.WriteField(station.CriticalC); csv.WriteField(station.Eta);
            csv.WriteField(station.MomentApplied); csv.WriteField(station.Ms);
            csv.WriteField(station.Msw); csv.WriteField(station.CriticalCMoment);
            csv.WriteField(station.EtaM);
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
