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
        ParamsButton.IsEnabled = task is not null;
        ReportButton.IsEnabled = app.ExportCalcResultReportCommand.CanExecute(result);
    }

    void Report_Click(object sender, RoutedEventArgs e)
    {
        if (_app.ExportCalcResultReportCommand.CanExecute(_result))
            _app.ExportCalcResultReportCommand.Execute(_result);
    }

    /// <summary>Окно параметров; «Сохранить и рассчитать» откроет новый результат.</summary>
    void Params_Click(object sender, RoutedEventArgs e)
    {
        if (_task is not null) PunchingParamsDialog.Edit(_app, _task, Window.GetWindow(this));
    }
}
