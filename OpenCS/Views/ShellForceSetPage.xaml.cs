using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views
{
   public partial class ShellForceSetPage : UserControl
   {
      public ShellForceSetPage(CScore.ForceSet model, AppViewModel app)
      {
         InitializeComponent();
         DataContext = new ShellForceSetVM(model, app);
      }

      public ShellForceSetPage(AppViewModel app)
      {
         InitializeComponent();
         DataContext = new ShellForceSetVM(
            new CScore.ForceSet { Tag = "Новый набор", Kind = "shell" }, app);
      }
   }
}
