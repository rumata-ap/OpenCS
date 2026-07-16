namespace CScore.Import
{
   /// <summary>Правила переноса усилий LIRA → OpenCS.</summary>
   public class LiraImportOptions
   {
      /// <summary>Множитель т → кН (и т/м → кН/м, т·м → кН·м).</summary>
      public double TonToKnFactor { get; set; } = 9.80665;

      /// <summary>Инвертировать знаки изгибающих моментов Mx/My для стержней.</summary>
      public bool InvertBarBendingMoments { get; set; } = true;

      /// <summary>Инвертировать знаки изгибающих/крутящего моментов Mx/My/Mxy для пластин.</summary>
      public bool InvertShellBendingMoments { get; set; } = true;

      public static LiraImportOptions Default => new();
   }
}
