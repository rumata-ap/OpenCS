using System.Text.Json.Serialization;

namespace OpenCS.Utilites
{
   /// <summary>Настройки импорта усилий из LIRA SAPR (HTML).</summary>
   public class LiraImportSettings
   {
      /// <summary>Множитель перевода т → кН.</summary>
      [JsonPropertyName("tonToKn")]
      public double TonToKnFactor { get; set; } = 9.80665;

      /// <summary>Инвертировать изгибающие моменты Mx/My для стержней.</summary>
      [JsonPropertyName("invertBarMxMy")]
      public bool InvertBarBendingMoments { get; set; } = true;

      public static LiraImportSettings Default => new();

      public LiraImportSettings Clone() => new()
      {
         TonToKnFactor           = TonToKnFactor,
         InvertBarBendingMoments = InvertBarBendingMoments,
      };

      public CScore.Import.LiraImportOptions ToOptions() => new()
      {
         TonToKnFactor           = TonToKnFactor,
         InvertBarBendingMoments = InvertBarBendingMoments,
      };
   }
}
