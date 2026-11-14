using CScore;
using OpenCS.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views;

/// <summary>Окно пакета проверок стального элемента по СП 16.</summary>
public partial class SteelCheckBatchResultView : UserControl
{
    readonly AppViewModel _app;
    readonly CalcTask _task;

    public SteelCheckBatchResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        InitializeComponent();
        _app = app;
        _task = task;
        DataContext = new SteelCheckBatchVM(result);
        RowsGrid.SelectionChanged += (_, _) => CreateTaskBtn.IsEnabled = RowsGrid.SelectedItem != null;
    }

    void RowsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => CreateTask();

    void CreateTask_Click(object sender, RoutedEventArgs e) => CreateTask();

    void CreateTask()
    {
        if (RowsGrid.SelectedItem is SteelCheckBatchVM.BatchRow row)
            BatchRowTask.CreateAndRun(_app, _task, row.Num);
    }
}
