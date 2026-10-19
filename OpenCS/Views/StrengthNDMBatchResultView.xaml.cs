using CScore;
using OpenCS.ViewModels;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OpenCS.Views;

/// <summary>Просмотр результата пакетной проверки прочности по НДМ.</summary>
public partial class StrengthNDMBatchResultView : UserControl
{
    readonly AppViewModel _app;
    readonly CalcTask _task;

    public StrengthNDMBatchResultView(CalcResult result, AppViewModel app, CalcTask task)
    {
        InitializeComponent();
        _app = app;
        _task = task;
        var section = app.CrossSections.FirstOrDefault(s => s.Id == task.SectionId);
        DataContext = new StrengthNDMBatchVM(result, section, app.CalcSettings);
        RowsGrid.SelectionChanged += (_, _) => CreateTaskBtn.IsEnabled = RowsGrid.SelectedItem != null;
    }

    void RowsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => CreateTask();

    void CreateTask_Click(object sender, RoutedEventArgs e) => CreateTask();

    // Строка разворачивается в задачу НДС (одиночной проверки прочности по НДМ нет), см. BatchRowTaskFactory.
    void CreateTask()
    {
        if (RowsGrid.SelectedItem is StrengthNDMBatchVM.BatchRow row)
            BatchRowTask.CreateAndRun(_app, _task, row.Num);
    }
}
