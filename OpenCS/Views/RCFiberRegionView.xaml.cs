using CScore;
using OpenCS.Services;
using OpenCS.ViewModels;

using System.Windows.Controls;

namespace OpenCS.Views
{
   /// <summary>
   /// Логика взаимодействия для RCFiberRegionView.xaml
   /// </summary>
   public partial class RCFiberRegionView : UserControl
   {
      public RCFiberRegionView(AppViewModel mvm)
      {
         InitializeComponent();
         var plotService = new WpfPlotService(plot);
         plotService.ApplySettings(mvm.PlotSettings);
         var vm = new RCFiberRegionVM
         {
            MVM = mvm,
            PlotService = plotService,
         };
         DataContext = vm;
      }

      public RCFiberRegionView(RCFiberRegion region, AppViewModel mvm, bool isSaved = true)
      {
         InitializeComponent();
         var plotService = new WpfPlotService(plot);
         plotService.ApplySettings(mvm.PlotSettings);
         var vm = new RCFiberRegionVM
         {
            PlotService = plotService,
            Region = region,
            MVM = mvm,
            IsSaved = isSaved
         };
         DataContext = vm;
      }
   }
}