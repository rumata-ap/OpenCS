using OpenCS.ViewModels;

using System.Windows.Controls;
using System.Windows.Threading;

namespace OpenCS.Views
{
   /// <summary>
   /// Страница импорта геометрии из DXF. Связывает <see cref="FromDxfVM"/> с
   /// <see cref="DxfInteractiveView"/> через колбэки (без code-behind ссылок на ListBox).
   /// </summary>
   public partial class FromDxfPage : UserControl
   {
      public FromDxfPage(AppViewModel mvm, string fileName)
      {
         InitializeComponent();
         var vm = new FromDxfVM { mvm = mvm };
         DataContext = vm;
         vm.CanvasLoader = (prims, layers) => InteractiveCanvas.Load(prims, layers);
         InteractiveCanvas.PrimitiveClicked = vm.HandlePrimitiveClicked;
         InteractiveCanvas.SetBackground(mvm.PlotSettings.DxfCanvasBackground);
         mvm.DxfBgApplied = bg => InteractiveCanvas.SetBackground(bg);

         // Загружаем после первого рендера, чтобы канвас знал свои размеры
         Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => vm.LoadFile(fileName));
      }
   }
}
