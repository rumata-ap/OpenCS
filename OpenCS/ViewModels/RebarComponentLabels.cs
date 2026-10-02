using CScore.Fem;
using CScore.Import;
using CScore.PlateRebar;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>
/// Подписи компонент армирования КЭ по программе-источнику: у ЛИРЫ — AS1..AS4, AU1..AU4, ASW1/ASW2, у SCAD —
/// грани S1..S4 (угловые входят в S1/S2) и поперечная IWz/IWy.
/// </summary>
public static class RebarComponentLabels
{
   /// <summary>Источник армирования стержней — SCAD (подбор плагина или заданное из .SPR).</summary>
   public static bool IsScad(IBarRebarFieldSource? source) =>
      source is ScadSelectedBarRebarSource or ScadAssignedBarRebarSource;

   /// <summary>Источник армирования пластин — SCAD.</summary>
   public static bool IsScad(IPlateRebarFieldSource? source) =>
      source is ScadSelectedPlateRebarSource or ScadAssignedPlateRebarSource;

   /// <summary>Подпись компоненты армирования стержня.</summary>
   public static string Bar(BarRebarComponent c, bool scad) => Loc.S(scad && c is BarRebarComponent.As1
      or BarRebarComponent.As2 or BarRebarComponent.As3 or BarRebarComponent.As4
      or BarRebarComponent.Asw1 or BarRebarComponent.Asw2
      ? "MosaicBarRebarScad" + c
      : "MosaicBarRebar" + c);

   /// <summary>Подпись компоненты армирования пластины.</summary>
   public static string Plate(PlateRebarMosaicComponent c, bool scad) => Loc.S((scad ? "PlateRebarMosaicCompScad" : "PlateRebarMosaicComp") + c switch
   {
      PlateRebarMosaicComponent.BottomX => "As1",
      PlateRebarMosaicComponent.TopX => "As2",
      PlateRebarMosaicComponent.BottomY => "As3",
      PlateRebarMosaicComponent.TopY => "As4",
      _ => "Asw",
   });
}
