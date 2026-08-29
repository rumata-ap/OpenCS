using CScore;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views
{
   public partial class TwoStageSectionEditorPage : UserControl
   {
      TwoStageSectionVM _vm = null!;

      public TwoStageSectionEditorPage(AppViewModel app)
      {
         InitializeComponent();
         var section = new TwoStageSection { Tag = "Новое усиление" };
         _vm = new TwoStageSectionVM(section, app);
         DataContext = _vm;
         _vm.PropertyChanged += (_, e) =>
         {
            if (e.PropertyName == nameof(TwoStageSectionVM.PlotElements))
               UpdatePlot();
         };
      }

      public TwoStageSectionEditorPage(TwoStageSection section, AppViewModel app)
      {
         InitializeComponent();
         _vm = new TwoStageSectionVM(section, app);
         DataContext = _vm;
         _vm.PropertyChanged += (_, e) =>
         {
            if (e.PropertyName == nameof(TwoStageSectionVM.PlotElements))
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
   }
}
