using CScore;
using OpenCS.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views;

public partial class CrackingBatchResultView : UserControl
{
    readonly AppViewModel _app;
    readonly CalcTask _task;

    public CrackingBatchResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        InitializeComponent();
        _app = app;
        _task = task;
        DataContext = new CrackingBatchVM(result);
        RowsGrid.SelectionChanged += (_, _) => CreateTaskBtn.IsEnabled = RowsGrid.SelectedItem != null;
    }

    void RowsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => CreateTask();

    void CreateTask_Click(object sender, RoutedEventArgs e) => CreateTask();

    void CreateTask()
    {
        if (RowsGrid.SelectedItem is CrackingBatchVM.BatchRow row)
            BatchRowTask.CreateAndRun(_app, _task, row.Num);
    }
}
