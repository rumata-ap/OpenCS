using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCS.Tasks;

/// <summary>Параметры задачи температурной кривизны в <see cref="CScore.CalcTask.ParamsJson"/>.</summary>
public sealed class FireThermalCurvatureParams
{
   /// <summary>Идентификатор огневого сечения.</summary>
   [JsonPropertyName("fire_section_id")]
   public int FireSectionId { get; set; }

   /// <summary>Идентификатор сохранённого теплового результата.</summary>
   [JsonPropertyName("thermal_result_id")]
   public int ThermalResultId { get; set; }

   /// <summary>Индекс снимка температурного поля; -1 означает последний снимок.</summary>
   [JsonPropertyName("snapshot_index")]
   public int SnapshotIndex { get; set; } = -1;

   /// <summary>Нормируемый предел огнестойкости R, мин.</summary>
   [JsonPropertyName("normalized_limit_min")]
   public double NormalizedLimitMin { get; set; } = 120.0;

   /// <summary>Арматура у нагреваемой грани принимается растянутой.</summary>
   [JsonPropertyName("tension_rebar_at_heated_face")]
   public bool TensionRebarAtHeatedFace { get; set; } = true;

   /// <summary>Метод определения высоты сжатой зоны.</summary>
   [JsonPropertyName("compression_zone_method")]
   public string CompressionZoneMethod { get; set; } = "auto";

   /// <summary>Разобрать сохранённый JSON с безопасными значениями по умолчанию.</summary>
   public static FireThermalCurvatureParams Parse(string? json)
   {
      if (string.IsNullOrWhiteSpace(json) || json == "{}")
         return new FireThermalCurvatureParams();

      return JsonSerializer.Deserialize<FireThermalCurvatureParams>(json)
         ?? new FireThermalCurvatureParams();
   }

   /// <summary>Сериализовать контракт в JSON.</summary>
   public string ToJson() => JsonSerializer.Serialize(this);
}
