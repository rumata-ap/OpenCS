using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views
{
   public partial class ForceSetPage : UserControl
   {
      public ForceSetPage(CScore.ForceSet model, AppViewModel app)
      {
         InitializeComponent();
         DataContext = new ForceSetVM(model, app);
      }

      public ForceSetPage(AppViewModel app)
      {
         InitializeComponent();
         DataContext = new ForceSetVM(new CScore.ForceSet { Tag = "Новый набор" }, app);
      }
   }
}
