using CScore;
using OpenCS.ViewModels;

using System.Collections.Generic;
using System.Windows;

namespace OpenCS.Views
{
   public partial class MeshDialog : Window
   {
      MeshDialogVM _vm;

      public MeshDialog(MaterialArea area, AppViewModel app)
      {
         InitializeComponent();
         _vm = new MeshDialogVM(area, app, this);
         DataContext = _vm;
         _vm.PropertyChanged += (_, e) =>
         {
            if (e.PropertyName == nameof(MeshDialogVM.PlotElements))
               UpdatePreview();
         };
         UpdatePreview();
      }

      void UpdatePreview()
      {
         var elements = _vm.PlotElements;
         if (elements.Count == 0) { preview.Clear(); return; }

         double xMin = double.MaxValue, xMax = double.MinValue;
         double yMin = double.MaxValue, yMax = double.MinValue;

         foreach (var el in elements)
         {
            if (el is PolygonElement p)
            {
               for (int i = 0; i < p.Xs.Length; i++)
               {
                  if (p.Xs[i] < xMin) xMin = p.Xs[i];
                  if (p.Xs[i] > xMax) xMax = p.Xs[i];
                  if (p.Ys[i] < yMin) yMin = p.Ys[i];
                  if (p.Ys[i] > yMax) yMax = p.Ys[i];
               }
            }
         }

         if (xMin > xMax) { preview.Clear(); return; }
         if (xMax - xMin < 1e-9) { xMin -= 0.1; xMax += 0.1; }
         if (yMax - yMin < 1e-9) { yMin -= 0.1; yMax += 0.1; }

         preview.Draw(elements, xMin, xMax, yMin, yMax, squareAxes: true);
      }
   }
}
