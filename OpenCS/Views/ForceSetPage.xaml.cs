using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views
{
   public partial class BarForceSetPage : UserControl, OpenCS.ViewModels.IContentPage
   {
      public BarForceSetPage(CScore.ForceSet model, AppViewModel app)
      {
         InitializeComponent();
         DataContext = new BarForceSetVM(model, app);
      }

      public BarForceSetPage(AppViewModel app)
      {
         InitializeComponent();
         DataContext = new BarForceSetVM(
            new CScore.ForceSet { Tag = "Новый набор", Kind = "bar" }, app);
      }
   }
}
