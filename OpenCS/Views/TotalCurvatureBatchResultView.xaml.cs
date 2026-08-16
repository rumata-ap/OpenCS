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
        var row = SelectedRow;
        if (row == null)
            return;

        var section = _app.CrossSections.FirstOrDefault(s => s.Id == _task.SectionId);
        if (section == null)
        {
            MessageBox.Show(Loc.S("CalcTaskSectionNotFound"), Loc.S("Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string tag = $"{section.Tag} — {row.Label}";
        var parameters = new TotalCurvatureTaskParams
        {
            N = row.N,
            Mx = row.MxTotal,
            My = row.MyTotal,
            ForcesMode = "manual",
            MxLongManual = row.MxLong,
            MyLongManual = row.MyLong
        };
        var newTask = new CalcTask
        {
            Kind = "total_curvature",
            SectionId = _task.SectionId,
            CalcType = _task.CalcType,
            Tag = tag,
            ParamsJson = parameters.ToJson()
        };

        newTask.Num = _app.CalcTasks.Count > 0
            ? _app.CalcTasks.Max(task => task.Num) + 1
            : 1;
        _app.db.SaveCalcTask(newTask);
        _app.LogService.Info(string.Format(Loc.S("CalcTaskCreated"), tag));
    }
}
