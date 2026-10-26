using System.Text.Json;
using System.Text.Json.Serialization;

namespace CScore;

/// <summary>
/// Параметры двухстадийной задачи (хранятся в CalcTask.ParamsJson).
/// Этап 1 → замороженная кривизна κ1, этап 2 → состояние составного сечения с учётом κ1.
/// </summary>
public sealed class TwoStageParams
{
   /// <summary>Усилие первого этапа (до усиления / омоноличивания).</summary>
   public StageForce Stage1 { get; set; } = new();

   /// <summary>Усилие второго этапа (полное усилие на составном сечении).</summary>
   public StageForce Stage2 { get; set; } = new();

   static readonly JsonSerializerOptions Opts = new()
   {
      DefaultIgnoreCondition = JsonIgnoreCondition.Never,
      PropertyNameCaseInsensitive = true
   };

   /// <summary>Разобрать JSON-параметры; при ошибке вернуть значения по умолчанию.</summary>
   public static TwoStageParams Parse(string? json)
   {
      if (string.IsNullOrWhiteSpace(json) || json == "{}")
         return new TwoStageParams();
      try
      {
         return JsonSerializer.Deserialize<TwoStageParams>(json, Opts) ?? new TwoStageParams();
      }
      catch
      {
         return new TwoStageParams();
      }
   }

   /// <summary>Сериализовать в JSON.</summary>
   public string ToJson() => JsonSerializer.Serialize(this, Opts);
}
