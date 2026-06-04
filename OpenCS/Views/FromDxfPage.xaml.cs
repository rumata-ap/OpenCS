using OpenCS.ViewModels;

using System.Windows.Controls;

namespace OpenCS.Views
{
   /// <summary>
   /// Страница импорта геометрии из DXF. Связывает <see cref="FromDxfVM"/> с
   /// <see cref="DxfInteractiveView"/> через колбэки (без code-behind ссылок на ListBox).
   /// </summary>
   public partial class FromDxfPage : UserControl
   {
      public FromDxfPage(AppViewModel mvm)
      {
         InitializeComponent();
         var vm = new FromDxfVM { mvm = mvm };
         DataContext = vm;
         vm.CanvasLoader = (prims, layers) => InteractiveCanvas.Load(prims, layers);
         InteractiveCanvas.SelectionChanged = vm.HandleSelectionChanged;
      }
   }
}
