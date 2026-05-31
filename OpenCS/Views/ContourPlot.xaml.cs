using CScore;

using OpenCS.Services;
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

         var plotService = new WpfPlotService(ViewPl);
         plotService.ApplySettings(mvm.PlotSettings);

         if (isSaved)
         {
            mvm.CurrentContour.Contour.PointsToXYs();
            plotService.AddScatter(mvm.CurrentContour.Contour.X.ToArray(), mvm.CurrentContour.Contour.Y.ToArray(), lineWidth: 2);
            plotService.EnableSquareAxes();
            plotService.AutoScale();
            plotService.Refresh();
         }

         if(!isSaved) mvm.CurrentContour.IsEdit = true;

         mvm.CurrentContour.PlotService = plotService;
         DataContext = mvm.CurrentContour;
      }
   }
}