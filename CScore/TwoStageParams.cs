using System.Text.Json;
using System.Text.Json.Serialization;

namespace CScore;

/// <summary>Описание усилия одного этапа двухстадийной задачи.</summary>
public sealed class StageForce
{
   /// <summary>Режим усилия: "manual" (ручные N/Mx/My), "item" (строка набора), "set" (весь набор).</summary>
   public string Mode { get; set; } = "item";

   /// <summary>Ручное усилие N (кН) при Mode == "manual".</summary>
   public double N { get; set; }
   /// <summary>Ручное усилие Mx (кН·м) при Mode == "manual".</summary>
   public double Mx { get; set; }
   /// <summary>Ручное усилие My (кН·м) при Mode == "manual".</summary>
   public double My { get; set; }

   /// <summary>Id набора усилий при Mode == "item"/"set".</summary>
   public int ForceSetId { get; set; }
   /// <summary>Id строки набора при Mode == "item".</summary>
   public int ForceItemId { get; set; }
}

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
