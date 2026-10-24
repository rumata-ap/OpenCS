using System.Text.Json;
using System.Text.Json.Nodes;
using CScore;
using CScore.LimitSurface;
using OpenCS.LimitSurface;

namespace OpenCS.Tasks;

/// <summary>
/// Одиночная задача по точке окна поверхности: (вид поверхности, режим луча Кисп) → вид задачи, усилия — из
/// снимка строки (набор мог измениться после расчёта), поэтому ручные усилия в параметрах, без ссылки на строку.
/// Источник (поверхность + строка) записывается в параметры: повторный выбор находит ту же задачу.
/// </summary>
public static class LimitSurfaceRowTaskFactory
{
   const string SourceProp = "surfaceSource";

   /// <summary>
   /// Вид одиночной задачи для поверхности и режима луча или null, если её нет: у ПС2 (образование трещин) есть
   /// только задача M_crc при N = const (п. 8.2.4) — режим Moment.
   /// </summary>
   public static string? SingleKind(string surfaceKind, UtilizationMode mode) => (surfaceKind, mode) switch
   {
      ("limit_surface", UtilizationMode.Moment) => "limit_moment",
      ("limit_surface", UtilizationMode.Axial) => "limit_axial",
      ("limit_surface", _) => "limit_force",
      ("crack_surface", UtilizationMode.Moment) => "cracking",
      _ => null,
   };

   /// <summary>Есть ли одиночная задача по точке для поверхности и режима.</summary>
   public static bool Supports(string surfaceKind, UtilizationMode mode) => SingleKind(surfaceKind, mode) != null;

   /// <summary>Собрать одиночную задачу (без Id и Num).</summary>
   public static CalcTask Create(CalcTask surface, LimitSurfacePointRecord p, UtilizationMode mode, string sectionTag)
   {
      string kind = SingleKind(surface.Kind, mode)
         ?? throw new InvalidOperationException($"Для поверхности «{surface.Kind}» в режиме {mode} одиночной задачи нет.");
      var obj = JsonNode.Parse(new LimitForceParams { N = p.N, Mx = p.Mx, My = p.My }.ToJson())!.AsObject();
      obj[SourceProp] = new JsonObject { ["taskId"] = surface.Id, ["num"] = p.Num };
      return new CalcTask
      {
         Kind = kind,
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
      string? kind = SingleKind(surface.Kind, mode);
      return kind == null ? null : tasks.FirstOrDefault(t => t.Kind == kind && ReadSource(t.ParamsJson) == (surface.Id, num));
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
