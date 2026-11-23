using OpenCS.Utilites;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views
{
   public partial class BarForceSetPage : UserControl
   {
      public BarForceSetPage(CScore.ForceSet model, AppViewModel app)
      {
         InitializeComponent();
         DataContext = new BarForceSetVM(model, app);
         Unloaded += (_, _) => (DataContext as ForceSetEditorVM)?.Detach();
      }

      /// <summary>Новый набор стержня (в проект — по «Сохранить»).</summary>
      public BarForceSetPage(AppViewModel app)
         : this(new CScore.ForceSet { Tag = string.Format("Q: {0}", Loc.S("NewForceSetName")), Kind = "bar" }, app)
      {
      }
   }
}
