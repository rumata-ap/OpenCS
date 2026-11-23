using OpenCS.ViewModels;

using System.Windows.Controls;

namespace OpenCS.Views
{
   /// <summary>
   /// Логика взаимодействия для ContourPlot.xaml
   /// </summary>
   public partial class ContourPlot : UserControl
   {
      public ContourPlot(AppViewModel mvm, bool isSaved = true)
      {
         InitializeComponent();

         var vm = mvm.CurrentContour!;
         ViewCanvas.SetVM(vm, mvm.PlotSettings);

         if (isSaved && vm.Contour.Points.Count > 0)
         {
            if (vm.Contour.Points.Count >= 4)
               vm.Contour.PointsToXYs();
            vm.DrawingPhase = ContourDrawingPhase.Draw;
            vm.FitViewToPoints();
         }
         else
         {
            vm.IsEdit = true;
            vm.DrawingPhase = ContourDrawingPhase.Setup;
         }

         DataContext = vm;
      }

      /// <summary>Правка X/Y в таблице: после применения привязки — перерисовка и «*».</summary>
      void PointsGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
      {
         if (e.EditAction != DataGridEditAction.Commit || DataContext is not ContourVM vm) return;
         Dispatcher.InvokeAsync(vm.CommitPointMove, System.Windows.Threading.DispatcherPriority.Background);
      }
   }
}
