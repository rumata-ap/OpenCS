using System.Text.Json.Serialization;

namespace OpenCS.Utilites
{
   /// <summary>Способ дискретизации дуговых сегментов полилиний AutoCAD.</summary>
   public enum ArcDiscretization { ChordLength = 0, FixedSegments = 1 }

   /// <summary>Настройки прямого импорта из AutoCAD через COM.</summary>
   public class AcadImportSettings
   {
      /// <summary>Множитель пересчёта единиц чертежа в метры. 0.001 = мм→м, 1 = м→м.</summary>
      [JsonPropertyName("scale")]
      public double ScaleFactor { get; set; } = 0.001;

      /// <summary>Фильтр слоя по умолчанию (пусто = все слои).</summary>
      [JsonPropertyName("layerFilter")]
      public string DefaultLayerFilter { get; set; } = "";

      /// <summary>Способ дискретизации дуговых сегментов.</summary>
      [JsonPropertyName("arcMode")]
      public ArcDiscretization ArcDiscretizationMode { get; set; } = ArcDiscretization.ChordLength;

      /// <summary>Характерная длина хорды для дискретизации дуги [мм] (при ArcDiscretizationMode = ChordLength).</summary>
      [JsonPropertyName("arcChord")]
      public double ArcChordLength { get; set; } = 10.0;

      /// <summary>Фиксированное число сегментов на дугу (при ArcDiscretizationMode = FixedSegments).</summary>
      [JsonPropertyName("arcSegs")]
      public int ArcSegments { get; set; } = 16;

      public static AcadImportSettings Default => new();

      public AcadImportSettings Clone() => new()
      {
         ScaleFactor = ScaleFactor,
         DefaultLayerFilter = DefaultLayerFilter,
         ArcDiscretizationMode = ArcDiscretizationMode,
         ArcChordLength = ArcChordLength,
         ArcSegments = ArcSegments,
      };
   }
}
