using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CScore;
using OpenCS.Tasks;
using OpenCS.ViewModels;
using OpenCS.Views.Helpers;

namespace OpenCS.Views;

public partial class ShellLayeredSlsResultView : UserControl
{
   public ShellLayeredSlsResultView(CalcResult result, AppViewModel app, CalcTask task)
   {
      InitializeComponent();
      var plate = app.PlateSections.FirstOrDefault(s => s.Id == task.SectionId);
      DataContext = new ShellLayeredSlsResultVM(result.DataJson, task.Tag, plate?.Tag ?? "", result.Created);

      if (!TryFillProfiles(result.DataJson, plate, app))
      {
         StrainsTab.Visibility = Visibility.Collapsed;
         StressesTab.Visibility = Visibility.Collapsed;
         PrincipalTab.Visibility = Visibility.Collapsed;
      }
   }

   /// <summary>
   /// Эпюры по толщине для сохранённого НДС — на тех же диаграммах CalcType.N и
   /// с тем же учётом растяжения бетона (настройка сечения), что и в расчёте трещин.
   /// </summary>
   bool TryFillProfiles(string dataJson, PlateSection? plate, AppViewModel app)
   {
      ShellLayeredSlsResultData? data;
      try { data = JsonSerializer.Deserialize<ShellLayeredSlsResultData>(dataJson); }
      catch { return false; }
      if (data is null || !data.Converged) return false;
      if (data.StrainState is not { Length: 6 } v)
      {
         // Результат сохранён до появления эпюр — подсказать пересчитать.
         NoProfilesNote.Visibility = Visibility.Visible;
         return false;
      }
      if (plate is null) return false;

      try
      {
         var concreteMat = app.db.Materials.FirstOrDefault(m => m.Id == plate.ConcreteMaterialId);
         var rebarMat = app.db.Materials.FirstOrDefault(m => m.Id == plate.RebarMaterialId);
         if (concreteMat is null || rebarMat is null) return false;

         var (cDiag, rDiag) = ShellLayeredSlsHandler.ResolveDiagrams(plate, concreteMat, rebarMat);
         var st = new ShellStrainState(v[0], v[1], v[2], v[3], v[4], v[5]);

         ShellProfilePlots.Fill(
            new ShellProfileCanvases(EpsXCanvas, EpsYCanvas, EpsGammaCanvas,
               SigXCanvas, SigYCanvas, TauXYCanvas,
               PrEpsCanvas, PrSigCanvas, BetaCanvas, ThetaCanvas),
            plate, st, cDiag, rDiag, layerDiags: null, tensionOverride: null,
            data.ZcxSecMm / 1000.0, data.ZcySecMm / 1000.0);
         return true;
      }
      catch { return false; /* нет материалов/диаграмм — показываем только трещины */ }
   }
}
