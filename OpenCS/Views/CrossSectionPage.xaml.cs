using CScore;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views
{
   public partial class CrossSectionPage : UserControl
   {
      CrossSectionVM _vm = null!;

      public CrossSectionPage(AppViewModel app)
      {
         InitializeComponent();
         var section = new CrossSection { Tag = "Новое сечение" };
         _vm = new CrossSectionVM(section, app);
         DataContext = _vm;
         preview.ApplySettings(app.PlotSettings);
         _vm.PropertyChanged += (_, e) =>
         {
            if (e.PropertyName == nameof(CrossSectionVM.PlotElements))
               UpdatePlot();
         };
      }

      public CrossSectionPage(CrossSection section, AppViewModel app)
      {
         InitializeComponent();
         _vm = new CrossSectionVM(section, app);
         DataContext = _vm;
         preview.ApplySettings(app.PlotSettings);
         _vm.PropertyChanged += (_, e) =>
         {
            if (e.PropertyName == nameof(CrossSectionVM.PlotElements))
               UpdatePlot();
         };
         _vm.RefreshPlot();
      }

      void UpdatePlot()
      {
         var plot = _vm.PlotData;
         var elements = plot.Elements;
         if (elements.Count == 0) { preview.Clear(); return; }

         preview.Draw(elements, plot.XMin, plot.XMax, plot.YMin, plot.YMax, squareAxes: true);
      }

      public void RefreshPlotSettings()
      {
         preview.ApplySettings(_vm.App.PlotSettings);
         UpdatePlot();
      }
   }
}
