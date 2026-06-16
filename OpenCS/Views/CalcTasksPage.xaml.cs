using System.Windows.Controls;
using System.Windows.Input;
using OpenCS.ViewModels;

namespace OpenCS.Views
{
   public partial class CalcTasksPage : UserControl
   {
      public CalcTasksPage(AppViewModel app)
      {
         InitializeComponent();
         DataContext = new CalcTasksPageVM(app, this);
      }

      void ResultGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
      {
         if (DataContext is CalcTasksPageVM vm)
            vm.ViewResult();
      }
   }
}
