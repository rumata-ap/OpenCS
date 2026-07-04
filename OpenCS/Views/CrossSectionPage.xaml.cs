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
         var elements = _vm.PlotElements;
         if (elements.Count == 0) { preview.Clear(); return; }

         double xMin = double.MaxValue, xMax = double.MinValue;
         double yMin = double.MaxValue, yMax = double.MinValue;

         foreach (var el in elements)
         {
            if (el is PolygonElement poly)
            {
               foreach (double x in poly.Xs) { if (x < xMin) xMin = x; if (x > xMax) xMax = x; }
               foreach (double y in poly.Ys) { if (y < yMin) yMin = y; if (y > yMax) yMax = y; }
            }
            else if (el is CircleElement circ)
            {
               double r = circ.Radius;
               if (circ.X - r < xMin) xMin = circ.X - r; if (circ.X + r > xMax) xMax = circ.X + r;
               if (circ.Y - r < yMin) yMin = circ.Y - r; if (circ.Y + r > yMax) yMax = circ.Y + r;
            }
         }

         if (xMin > xMax) { preview.Clear(); return; }
         if (xMax - xMin < 1e-9) { xMin -= 0.1; xMax += 0.1; }
         if (yMax - yMin < 1e-9) { yMin -= 0.1; yMax += 0.1; }

         preview.Draw(elements, xMin, xMax, yMin, yMax, squareAxes: true);
      }

      public void RefreshPlotSettings()
      {
         preview.ApplySettings(_vm.App.PlotSettings);
         UpdatePlot();
      }
   }
}
