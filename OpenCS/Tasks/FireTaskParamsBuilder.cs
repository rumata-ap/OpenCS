using System.Text.Json;

namespace OpenCS.Tasks;

/// <summary>
/// Сборка и разбор <c>params_json</c> огневых задач.
/// Вынесено из диалога свойств задачи, чтобы контракт параметров можно было
/// проверять тестами без запуска WPF.
/// </summary>
public static class FireTaskParamsBuilder
{
   /// <summary>Все виды огневых задач.</summary>
   public static bool IsFireKind(string? kind)
      => kind is "fire_r_check" or "fire_r_check_batch"
              or "fire_r_time" or "fire_thermal_curvature";

   /// <summary>Задача требует выбранного набора усилий.</summary>
   public static bool NeedsForceSet(string? kind)
      => kind is "fire_r_check" or "fire_r_check_batch" or "fire_r_time";

   /// <summary>Задача требует конкретной строки набора усилий.</summary>
   public static bool NeedsForceItem(string? kind)
      => kind is "fire_r_check" or "fire_r_time";

   /// <summary>
   /// Собрать <c>params_json</c>. Для <c>fire_r_time</c> индекс снапшота
   /// принудительно сбрасывается: задача перебирает все снапшоты сама.
   /// </summary>
   public static string Build(
      string kind,
      int fireSectionId,
      int thermalResultId,
      int snapshotIndex,
      string method)
   {
      var p = new FireRCheckParams
      {
         FireSectionId = fireSectionId,
         ThermalResultId = thermalResultId,
         SnapshotIndex = kind == "fire_r_time" ? -1 : snapshotIndex,
         Method = string.IsNullOrWhiteSpace(method) ? "fiber" : method
      };
      return JsonSerializer.Serialize(p);
   }

   /// <summary>Разобрать <c>params_json</c> огневой задачи.</summary>
   public static FireRCheckParams Parse(string kind, string? json)
   {
      var p = FireRCheckParams.Parse(json);
      if (kind == "fire_r_time")
         p.SnapshotIndex = -1;
      return p;
   }
}
