using System.Text.Json;
using CScore.LimitSurface;

namespace OpenCS.LimitSurface;

/// <summary>Параметры задачи поверхности предельных усилий (ParamsJson): единственный пользовательский — точность.</summary>
public sealed class LimitSurfaceTaskParams
{
   /// <summary>«normal» — умолчания ядра; «high» — вдвое меньший допуск адаптации.</summary>
   public string Accuracy { get; set; } = "normal";

   /// <summary>Разбор ParamsJson; пустой или битый — умолчания.</summary>
   public static LimitSurfaceTaskParams Parse(string? json)
   {
      if (string.IsNullOrWhiteSpace(json)) return new();
      try { return JsonSerializer.Deserialize<LimitSurfaceTaskParams>(json) ?? new(); }
      catch (JsonException) { return new(); }
   }

   /// <summary>Сериализация в ParamsJson.</summary>
   public string ToJson() => JsonSerializer.Serialize(this);

   /// <summary>Параметры построителя сетки для выбранной точности.</summary>
   public LimitSurfaceOptions ToOptions() => Accuracy == "high"
      ? new LimitSurfaceOptions { RefineTol = 0.001, MaxMeridians = 1152 }
      : new LimitSurfaceOptions();
}
