using OpenCS.Utilites;
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
         Unloaded += (_, _) => (DataContext as ForceSetEditorVM)?.Detach();
      }

      /// <summary>Новый набор пластины (в проект — по «Сохранить»).</summary>
      public ShellForceSetPage(AppViewModel app)
         : this(new CScore.ForceSet { Tag = string.Format("Q: {0}", Loc.S("NewForceSetName")), Kind = "shell" }, app)
      {
      }
   }
}
