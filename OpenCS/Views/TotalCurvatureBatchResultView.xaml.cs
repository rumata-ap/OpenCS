using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CScore;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class TotalCurvatureBatchResultView : UserControl
{
    readonly AppViewModel _app;
    readonly CalcTask _task;

    public TotalCurvatureBatchResultView(
        CalcResult result, AppViewModel app, CalcTask task)
    {
        _app = app;
        _task = task;
        InitializeComponent();
        DataContext = new TotalCurvatureBatchVM(result);
        RowsGrid.SelectionChanged += (_, _) =>
            CreateTaskBtn.IsEnabled = RowsGrid.SelectedItem != null;
    }

    TotalCurvatureBatchVM.BatchRow? SelectedRow =>
        RowsGrid.SelectedItem as TotalCurvatureBatchVM.BatchRow;

    void RowsGrid_MouseDoubleClick(
        object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SelectedRow != null)
            CreateTask();
    }

    void CreateTask_Click(object sender, RoutedEventArgs e) => CreateTask();

    void CreateTask()
    {
        if (SelectedRow is { } row)
            BatchRowTask.CreateAndRun(_app, _task, row.Num);
    }
}
