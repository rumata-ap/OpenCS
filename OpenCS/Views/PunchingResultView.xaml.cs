using System.Windows;
using System.Windows.Controls;
using CScore;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Окно результата задачи «Продавливание плиты (СП 63)».</summary>
public partial class PunchingResultView : UserControl
{
    readonly AppViewModel _app;
    readonly CalcResult _result;
    readonly CalcTask? _task;

    /// <summary>Создаёт окно по сохранённому результату.</summary>
    public PunchingResultView(CalcResult result, AppViewModel app, CalcTask? task)
    {
        InitializeComponent();
        _app = app;
        _result = result;
        _task = task;
        DataContext = new PunchingResultVM(result, task);
        ParamsButton.Visibility = Visibility.Collapsed;
        ReportButton.IsEnabled = app.ExportCalcResultReportCommand.CanExecute(result);
    }

    void Report_Click(object sender, RoutedEventArgs e)
    {
        if (_app.ExportCalcResultReportCommand.CanExecute(_result))
            _app.ExportCalcResultReportCommand.Execute(_result);
    }

    void Params_Click(object sender, RoutedEventArgs e)
    {
    }
}
