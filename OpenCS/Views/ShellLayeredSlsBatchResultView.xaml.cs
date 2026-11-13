using System.Windows;
using System.Windows.Controls;
using CScore;
using OpenCS.ViewModels;

namespace OpenCS.Views;

public partial class ShellLayeredSlsBatchResultView : UserControl
{
   readonly AppViewModel _app;
   readonly CalcTask _task;

   public ShellLayeredSlsBatchResultView(CalcResult result, AppViewModel app, CalcTask task)
   {
      InitializeComponent();
      _app = app;
      _task = task;
      DataContext = new ShellLayeredSlsBatchResultVM(result.DataJson);
      RowsGrid.SelectionChanged += (_, _) => CreateTaskBtn.IsEnabled = RowsGrid.SelectedItem != null;
   }

   void RowsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => CreateTask();

   void CreateTask_Click(object sender, RoutedEventArgs e) => CreateTask();

   void CreateTask()
   {
      if (RowsGrid.SelectedItem is ShellLayeredSlsBatchRowVM row)
         BatchRowTask.CreateAndRun(_app, _task, row.Num);
   }
}
