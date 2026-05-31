using CScore;
using OpenCS.Services;
using OpenCS.ViewModels;

using System.Windows.Controls;

namespace OpenCS.Views
{
   /// <summary>
   /// Логика взаимодействия для RegionPage.xaml
   /// </summary>
   public partial class RCFiberRegionPage : UserControl
   {
      public RCFiberRegionPage(AppViewModel mvm)
      {
         InitializeComponent();
         var plotService = new WpfPlotService(plot);
         plotService.ApplySettings(mvm.PlotSettings);
         var vm = new RCFiberRegionVM
         {
            MVM = mvm,
            CirclesListBox = CirclesListBox,
            RebarsListBox = RebarsListBox,
            PlotService = plotService,
         };
         DataContext = vm;
      }

      public RCFiberRegionPage(RCFiberRegion region, AppViewModel mvm, bool isSaved = true)
      {
         InitializeComponent();
         var plotService = new WpfPlotService(plot);
         plotService.ApplySettings(mvm.PlotSettings);
         var vm = new RCFiberRegionVM
         {
            Region = region,
            MVM = mvm,
            CirclesListBox = CirclesListBox,
            RebarsListBox = RebarsListBox,
            PlotService = plotService,
            IsSaved = isSaved
         };
         DataContext = vm;
      }
   }
}