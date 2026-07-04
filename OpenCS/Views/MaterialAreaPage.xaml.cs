using CScore;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views
{
   public partial class MaterialAreaPage : UserControl
   {
      MaterialAreaVM _vm;

      public MaterialAreaPage(MaterialArea area, AppViewModel app)
      {
         InitializeComponent();
         _vm = new MaterialAreaVM(area, app);
         DataContext = _vm;
         preview.ApplySettings(app.PlotSettings);
         diagramTypeCombo.ItemsSource = MaterialAreaVM.DiagramTypeValues;
         _vm.PropertyChanged += (_, e) =>
         {
            if (e.PropertyName == nameof(MaterialAreaVM.PlotElements))
               UpdatePlot();
            if (e.PropertyName is nameof(MaterialAreaVM.IsCustomMaterial)
                               or nameof(MaterialAreaVM.Material))
               diagramTypePanel.Visibility = _vm.IsCustomMaterial
                   ? System.Windows.Visibility.Collapsed
                   : System.Windows.Visibility.Visible;
         };
         // Установить начальную видимость
         diagramTypePanel.Visibility = _vm.IsCustomMaterial
             ? System.Windows.Visibility.Collapsed
             : System.Windows.Visibility.Visible;
         _vm.RefreshPlot();
      }

      void UpdatePlot()
      {
         var elements = _vm.PlotElements;
         if (elements.Count == 0) { preview.Clear(); return; }

         // Вычислить границы из Hull и point-волокон
         double xMin = double.MaxValue, xMax = double.MinValue;
         double yMin = double.MaxValue, yMax = double.MinValue;

         var hull = _vm.Model.Hull;
         if (hull != null && hull.X.Count > 0)
         {
            for (int i = 0; i < hull.X.Count; i++)
            {
               if (hull.X[i] < xMin) xMin = hull.X[i];
               if (hull.X[i] > xMax) xMax = hull.X[i];
               if (hull.Y[i] < yMin) yMin = hull.Y[i];
               if (hull.Y[i] > yMax) yMax = hull.Y[i];
            }
         }

         foreach (var f in _vm.Model.Fibers)
         {
            if (f.X - f.Diameter / 2 < xMin) xMin = f.X - f.Diameter / 2;
            if (f.X + f.Diameter / 2 > xMax) xMax = f.X + f.Diameter / 2;
            if (f.Y - f.Diameter / 2 < yMin) yMin = f.Y - f.Diameter / 2;
            if (f.Y + f.Diameter / 2 > yMax) yMax = f.Y + f.Diameter / 2;
         }

         if (xMin > xMax) { preview.Clear(); return; }

         // Небольшой отступ, если сечение вырожденное
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
