using System.Text.Json;
using System.Text.Json.Nodes;
using CScore;
using CScore.LimitSurface;
using OpenCS.LimitSurface;

namespace OpenCS.Tasks;

/// <summary>
/// Одиночная задача предельного усилия по точке окна поверхности: режим луча Кисп → вид задачи, усилия — из
/// снимка строки (набор мог измениться после расчёта), поэтому ручные усилия в параметрах, без ссылки на строку.
/// Источник (поверхность + строка) записывается в параметры: повторный выбор находит ту же задачу.
/// </summary>
public static class LimitSurfaceRowTaskFactory
{
   const string SourceProp = "surfaceSource";

   /// <summary>Вид одиночной задачи для режима луча.</summary>
   public static string SingleKind(UtilizationMode mode) => mode switch
   {
      UtilizationMode.Moment => "limit_moment",
      UtilizationMode.Axial => "limit_axial",
      _ => "limit_force",
   };

   /// <summary>Поверхности, у которых есть одиночная задача по точке (ПС2 — позже).</summary>
   public static bool Supports(string surfaceKind) => surfaceKind == "limit_surface";

   /// <summary>Собрать одиночную задачу (без Id и Num).</summary>
   public static CalcTask Create(CalcTask surface, LimitSurfacePointRecord p, UtilizationMode mode, string sectionTag)
   {
      var obj = JsonNode.Parse(new LimitForceParams { N = p.N, Mx = p.Mx, My = p.My }.ToJson())!.AsObject();
      obj[SourceProp] = new JsonObject { ["taskId"] = surface.Id, ["num"] = p.Num };
      return new CalcTask
      {
         Kind = SingleKind(mode),
         SectionId = surface.SectionId,
         CalcType = surface.CalcType,
         ForceSetId = 0,
         ForceItemId = 0,
         Tag = $"{sectionTag} — {(string.IsNullOrEmpty(p.Label) ? p.Num.ToString() : p.Label)}",
         ParamsJson = obj.ToJsonString(),
      };
   }

   /// <summary>Ранее созданная задача по той же строке той же поверхности в том же режиме или null.</summary>
   public static CalcTask? FindExisting(IEnumerable<CalcTask> tasks, CalcTask surface, int num, UtilizationMode mode)
   {
      string kind = SingleKind(mode);
      return tasks.FirstOrDefault(t => t.Kind == kind && ReadSource(t.ParamsJson) == (surface.Id, num));
   }

   static (int TaskId, int Num)? ReadSource(string? json)
   {
      if (string.IsNullOrWhiteSpace(json)) return null;
      try
      {
         using var doc = JsonDocument.Parse(json);
         return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(SourceProp, out var src)
                && src.TryGetProperty("taskId", out var id) && src.TryGetProperty("num", out var n)
            ? (id.GetInt32(), n.GetInt32())
            : null;
      }
      catch (JsonException) { return null; }
   }
}
