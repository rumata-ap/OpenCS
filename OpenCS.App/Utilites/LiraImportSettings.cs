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

      /// <summary>Инвертировать изгибающие/крутящий моменты Mx/My/Mxy для пластин.</summary>
      [JsonPropertyName("invertShellMxMyMxy")]
      public bool InvertShellBendingMoments { get; set; } = true;

      /// <summary>Свой каталог текстовых файлов схем ЛИРЫ (<c>&lt;Title&gt;.txt</c>): ищется первым, до каталогов Data
      /// установленных версий; null — только они.</summary>
      [JsonPropertyName("textFileDirectory")]
      public string? TextFileDirectory { get; set; }

      public static LiraImportSettings Default => new();

      public LiraImportSettings Clone() => new()
      {
         TonToKnFactor             = TonToKnFactor,
         InvertBarBendingMoments   = InvertBarBendingMoments,
         InvertShellBendingMoments = InvertShellBendingMoments,
         TextFileDirectory         = TextFileDirectory,
      };

      public CScore.Import.LiraImportOptions ToOptions() => new()
      {
         TonToKnFactor             = TonToKnFactor,
         InvertBarBendingMoments   = InvertBarBendingMoments,
         InvertShellBendingMoments = InvertShellBendingMoments,
      };
   }
}
