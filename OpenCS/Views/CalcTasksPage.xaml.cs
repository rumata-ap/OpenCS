using CScore;
using System.Windows.Controls;
using System.Windows.Input;
using OpenCS.ViewModels;

namespace OpenCS.Views
{
   public partial class CalcTasksPage : UserControl
   {
      public CalcTasksPage(AppViewModel app, CalcTask? initialTask = null)
      {
         InitializeComponent();
         var pageVm = new CalcTasksPageVM(app, this);
         DataContext = pageVm;
         if (initialTask != null)
            pageVm.SelectTask(initialTask.Id);
         // Страница закрыта (заменена другой) — VM отписывается от коллекций проекта.
         Unloaded += (_, _) => pageVm.Dispose();
      }

      void ResultGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
      {
         if (DataContext is CalcTasksPageVM vm)
            vm.ViewResult();
      }

      /// <summary>Двойной щелчок по задаче — правка (как «Изменить»).</summary>
      void TaskGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
      {
         if (DataContext is CalcTasksPageVM vm && vm.EditTaskCommand.CanExecute(null))
            vm.EditTaskCommand.Execute(null);
      }
   }
}
