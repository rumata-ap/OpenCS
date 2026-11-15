using System.Windows;
using System.Windows.Controls;
using CScore;
using CScore.Fem;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Окно результата проверки продавливания по МКЭ (<c>punching_check</c>).</summary>
public partial class PunchingFemResultView : UserControl
{
    readonly AppViewModel? _app;
    readonly FemCheck? _check;
    readonly CalcResult _result;
    readonly PunchingFemResultVM _vm;

    public PunchingFemResultView(CalcResult result, AppViewModel app, FemCheck? check)
    {
        InitializeComponent();
        _app = app;
        _check = check;
        _result = result;
        _vm = new PunchingFemResultVM(result, check, app.db.Materials);
        DataContext = _vm;
        Plan.NodeClicked += node =>
        {
            _vm.SelectNode(node);
            if (_vm.SelectedJoint != null) JointsGrid.ScrollIntoView(_vm.SelectedJoint);
        };
        ReportButton.IsEnabled = app.ExportCalcResultReportCommand.CanExecute(result);
        ParamsButton.IsEnabled = RunButton.IsEnabled = check is not null;
        UpdateCreateTask();
        _vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PunchingFemResultVM.SelectedRow)) UpdateCreateTask(); };
    }

    /// <summary>Окно по готовой VM без приложения — проверка разметки в тестах (кнопки выключены).</summary>
    public PunchingFemResultView(PunchingFemResultVM vm, ResourceDictionary resources)
    {
        Resources = resources;
        InitializeComponent();
        _result = vm.Result;
        _vm = vm;
        DataContext = vm;
        CreateTaskBtn.IsEnabled = ReportButton.IsEnabled = ParamsButton.IsEnabled = RunButton.IsEnabled = false;
    }

    void UpdateCreateTask() => CreateTaskBtn.IsEnabled = _app is not null && _check is not null && _vm.SelectedRowParams is not null;

    void CreateTask_Click(object sender, RoutedEventArgs e) => CreateTask();

    void RowsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => CreateTask();

    /// <summary>Одиночная задача «Продавливание плиты» по выбранной строке: создаётся, считается и открывается.</summary>
    void CreateTask()
    {
        if (_app is not null && _check is not null && _vm.SelectedJoint is { } joint && _vm.SelectedRow is { } row)
            BatchRowTask.CreateAndRun(_app, _check, joint.Data, row.Row);
    }

    void Report_Click(object sender, RoutedEventArgs e)
    {
        if (_app is not null && _app.ExportCalcResultReportCommand.CanExecute(_result))
            _app.ExportCalcResultReportCommand.Execute(_result);
    }

    void Params_Click(object sender, RoutedEventArgs e) => _app?.EditFemCheckCommand.Execute(_check);

    void Run_Click(object sender, RoutedEventArgs e) => _app?.RunFemCheckCommand.Execute(_check);
}
