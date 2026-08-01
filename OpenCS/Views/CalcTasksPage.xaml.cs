using CScore;
using System.Windows.Controls;
using System.Windows.Input;
using OpenCS.ViewModels;

namespace OpenCS.Views
{
   public partial class CalcTasksPage : UserControl, OpenCS.ViewModels.IContentPage
   {
      public CalcTasksPage(AppViewModel app, CalcTask? initialTask = null)
      {
         InitializeComponent();
         var pageVm = new CalcTasksPageVM(app, this);
         DataContext = pageVm;
         if (initialTask != null)
            pageVm.SelectTask(initialTask.Id);
      }

      void ResultGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
      {
         if (DataContext is CalcTasksPageVM vm)
            vm.ViewResult();
      }
   }
}
