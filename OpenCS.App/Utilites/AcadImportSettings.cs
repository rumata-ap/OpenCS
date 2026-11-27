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

      // ── Импорт сечений из чертежа ─────────────────────────────────────────

      /// <summary>Дискретизация дуг при импорте сечений: по стрелке (по умолчанию) либо по настройкам выше.</summary>
      [JsonPropertyName("cadArcMode")]
      public CScore.Import.Cad.CadArcMode SectionArcMode { get; set; } = CScore.Import.Cad.CadArcMode.Deviation;

      /// <summary>Наибольшее отклонение хорды от дуги [мм] при импорте сечений.</summary>
      [JsonPropertyName("cadArcDeviation")]
      public double SectionArcDeviation { get; set; } = 0.1;

      /// <summary>Объединять одинаковые сечения листа.</summary>
      [JsonPropertyName("cadMerge")]
      public bool SectionMergeIdentical { get; set; } = true;

      /// <summary>Запомненные роли слоёв (имя слоя → роль).</summary>
      [JsonPropertyName("cadLayerRoles")]
      public Dictionary<string, CScore.Import.Cad.CadLayerRole> SectionLayerRoles { get; set => field = Layers(value); } = Layers<CScore.Import.Cad.CadLayerRole>(null);

      /// <summary>Запомненные материалы слоёв (имя слоя → Id материала проекта).</summary>
      [JsonPropertyName("cadLayerMaterials")]
      public Dictionary<string, int> SectionLayerMaterials { get; set => field = Layers(value); } = Layers<int>(null);

      /// <summary>Диаметр хомутов слоя по умолчанию [мм] — для линий без ширины и подписи.</summary>
      [JsonPropertyName("cadStirrupDiameters")]
      public Dictionary<string, double> SectionStirrupDiameters { get; set => field = Layers(value); } = Layers<double>(null);

      /// <summary>Шаг хомутов слоя по умолчанию [мм] — для элементов без подписи шага.</summary>
      [JsonPropertyName("cadStirrupSpacings")]
      public Dictionary<string, double> SectionStirrupSpacings { get; set => field = Layers(value); } = Layers<double>(null);

      /// <summary>Словарь по имени слоя без учёта регистра (AutoCAD имена слоёв так и сравнивает) — и после чтения из базы.</summary>
      static Dictionary<string, T> Layers<T>(Dictionary<string, T>? source) =>
         source == null ? new(StringComparer.OrdinalIgnoreCase) : new(source, StringComparer.OrdinalIgnoreCase);

      public static AcadImportSettings Default => new();

      public AcadImportSettings Clone() => SettingsCopy.Deep(this);
   }
}
