using CScore;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System.Windows.Controls;

namespace OpenCS.Views
{
   /// <summary>Редактор стадийного сечения: части, их области и превью с подсветкой выбранной части.</summary>
   public partial class StagedSectionEditorPage : UserControl
   {
      readonly StagedSectionVM _vm;

      /// <summary>Новое стадийное сечение (одна пустая собственная часть).</summary>
      public StagedSectionEditorPage(AppViewModel app)
         : this(new StagedSection { Tag = Loc.S("NewStagedSection") }, app) { }

      public StagedSectionEditorPage(StagedSection section, AppViewModel app)
      {
         InitializeComponent();
         _vm = new StagedSectionVM(section, app);
         DataContext = _vm;
         _vm.PropertyChanged += (_, e) =>
         {
            if (e.PropertyName == nameof(StagedSectionVM.PlotElements))
               UpdatePlot();
         };
         UpdatePlot();
      }

      void UpdatePlot()
      {
         var plot = _vm.PlotData;
         if (plot.Elements.Count == 0) { preview.Clear(); return; }
         preview.Draw(plot.Elements, plot.XMin, plot.XMax, plot.YMin, plot.YMax, squareAxes: true);
      }
   }
}
