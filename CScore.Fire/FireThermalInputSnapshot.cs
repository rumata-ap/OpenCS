using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CScore;
using CScore.Fire.Entities;

namespace CScore.Fire;

/// <summary>Канонический снимок входных данных теплового расчёта и его хеш.</summary>
/// <param name="Json">Каноническая JSON-строка снимка.</param>
/// <param name="Hash">SHA-256 канонической строки, усечённый до 16 hex-символов.</param>
public readonly record struct FireThermalInput(string Json, string Hash);

/// <summary>
/// Построение снимка входных данных теплового расчёта: параметры огневого сечения,
/// геометрия связанного сечения, арматура, материалы и эффективный тип заполнителя.
/// </summary>
/// <remarks>
/// Метки и порядковые номера в снимок не входят: переименование огневого сечения
/// не меняет физику. Порядок рёбер нормализуется, числа пишутся в инвариантной
/// культуре, нечисловые значения — строками, чтобы хеш не зависел от окружения.
/// </remarks>
public static class FireThermalInputSnapshot
{
   /// <summary>Версия формата снимка. Увеличивать при изменении состава полей.</summary>
   public const int SchemaVersion = 1;

   static readonly JsonSerializerOptions WriteOptions = new()
   {
      WriteIndented = false,
      Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
   };

   /// <summary>Построить снимок и хеш.</summary>
   public static FireThermalInput Build(FireSectionDef def, CrossSection section, string effectiveAggregate)
   {
      ArgumentNullException.ThrowIfNull(def);
      ArgumentNullException.ThrowIfNull(section);

      var root = new JsonObject
      {
         ["schema"] = SchemaVersion,
         ["fire"] = new JsonObject
         {
            ["duration_min"] = Num(def.FireDurationMin),
            ["curve"] = def.FireCurve ?? "",
            ["aggregate_effective"] = (effectiveAggregate ?? "").Trim().ToLowerInvariant()
         },
         ["bc"] = BuildEdges(def),
         ["mesh"] = new JsonObject
         {
            ["step_m"] = Num(def.MeshStepM),
            ["algorithm"] = def.Algorithm ?? "",
            ["smooth_iter"] = def.SmoothIterTri,
            ["element_type"] = def.MeshElementType ?? "",
            ["bc_preset"] = def.BcPreset ?? "",
            ["hole_bc_preset"] = def.HoleBcPreset ?? ""
         },
         ["time"] = new JsonObject
         {
            ["step_s"] = Num(def.TimeStepS),
            ["theta"] = Num(def.Theta),
            ["picard_tol"] = Num(def.PicardTolCelsius),
            ["picard_max_iter"] = def.PicardMaxIter,
            ["snapshot_step_min"] = Num(def.SnapshotStepMin)
         },
         ["geometry"] = BuildGeometry(section),
         ["rebars"] = BuildRebars(section),
         ["materials"] = BuildMaterials(section)
      };

      string json = root.ToJsonString(WriteOptions);
      return new FireThermalInput(json, Hash(json));
   }

   /// <summary>
   /// Первое различие двух снимков в порядке значимости.
   /// Возвращает ключ строки локализации или <c>null</c>, если снимки совпадают.
   /// </summary>
   public static string? FirstDifference(string? oldJson, string? newJson)
   {
      if (string.IsNullOrWhiteSpace(oldJson) || string.IsNullOrWhiteSpace(newJson))
         return "FireStale_Unknown";
      if (oldJson == newJson) return null;

      JsonNode? a, b;
      try { a = JsonNode.Parse(oldJson); b = JsonNode.Parse(newJson); }
      catch { return "FireStale_Unknown"; }

      (string Section, string Key)[] order =
      [
         ("fire", "FireStale_Fire"),
         ("bc", "FireStale_Bc"),
         ("mesh", "FireStale_Mesh"),
         ("time", "FireStale_Time"),
         ("geometry", "FireStale_Geometry"),
         ("rebars", "FireStale_Rebars"),
         ("materials", "FireStale_Materials")
      ];

      foreach (var (name, key) in order)
      {
         string sa = a?[name]?.ToJsonString(WriteOptions) ?? "";
         string sb = b?[name]?.ToJsonString(WriteOptions) ?? "";
         if (sa != sb) return key;
      }

      return "FireStale_Unknown";
   }

   static JsonArray BuildEdges(FireSectionDef def)
   {
      var arr = new JsonArray();
      var ordered = def.Edges
         .OrderBy(e => e.ContourType ?? "", StringComparer.Ordinal)
         .ThenBy(e => e.HoleIndex ?? -1)
         .ThenBy(e => e.EdgeIndex);

      foreach (var e in ordered)
      {
         arr.Add(new JsonObject
         {
            ["contour"] = e.ContourType ?? "",
            ["hole"] = e.HoleIndex ?? -1,
            ["edge"] = e.EdgeIndex,
            ["type"] = e.BcType ?? "",
            ["alpha"] = Num(e.AlphaConv),
            ["emissivity"] = Num(e.Emissivity),
            ["t_ambient"] = Num(e.TAmbientCelsius)
         });
      }

      return arr;
   }

   static JsonObject BuildGeometry(CrossSection section)
   {
      var concreteArea = section.Areas.FirstOrDefault(a =>
         a.Hull != null &&
         !IsPointOnly(a) &&
         (a.Category == AreaCategory.Region || a.Material?.Type == MatType.Concrete));
      concreteArea ??= section.Areas.FirstOrDefault(a => a.Hull != null && !IsPointOnly(a));
      if (concreteArea?.Hull is null)
         throw new InvalidOperationException("Не найдена основная область сечения с внешним контуром Hull.");

      var holes = concreteArea.Holes
         .Select(BuildContour)
         .OrderBy(c => c.ToJsonString(WriteOptions), StringComparer.Ordinal)
         .ToList();
      var holesJson = new JsonArray();
      foreach (var hole in holes)
         holesJson.Add(hole);

      return new JsonObject
      {
         ["hull"] = BuildContour(concreteArea.Hull),
         ["holes"] = holesJson
      };
   }

   static JsonObject BuildContour(Contour contour)
   {
      int count = Math.Min(contour.X.Count, contour.Y.Count);
      if (count > 1 && NearlyEqual(contour.X[0], contour.X[count - 1]) &&
          NearlyEqual(contour.Y[0], contour.Y[count - 1]))
         count--;

      var points = new JsonArray();
      for (int i = 0; i < count; i++)
      {
         points.Add(new JsonObject
         {
            ["x"] = Num(contour.X[i]),
            ["y"] = Num(contour.Y[i])
         });
      }

      return new JsonObject { ["points"] = points };
   }

   static bool IsPointOnly(MaterialArea area)
      => area.Fibers.Count > 0 && area.Fibers.All(f => f.TypeFiber == FiberType.point);

   static bool NearlyEqual(double a, double b)
      => Math.Abs(a - b) <= 1e-12;

   static JsonArray BuildRebars(CrossSection section)
   {
      var arr = new JsonArray();
      foreach (var area in section.Areas.OrderBy(a => a.Id))
      {
         foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point)
                                      .OrderBy(f => f.X).ThenBy(f => f.Y))
         {
            arr.Add(new JsonObject
            {
               ["x"] = Num(f.X),
               ["y"] = Num(f.Y),
               ["d"] = Num(f.Diameter),
               ["a"] = Num(f.Area),
               ["material"] = area.MaterialId
            });
         }
      }
      return arr;
   }

   static JsonArray BuildMaterials(CrossSection section)
   {
      var arr = new JsonArray();
      foreach (var area in section.Areas.OrderBy(a => a.Id))
      {
         var m = area.Material;
         if (m is null) continue;
         arr.Add(new JsonObject
         {
            ["id"] = m.Id,
            ["type"] = (int)m.Type,
            ["aggregate"] = m.AggregateType ?? "",
            ["fire_rebar_class"] = m.FireRebarClass ?? ""
         });
      }
      return arr;
   }

   /// <summary>
   /// Число с округлением до 1e-9 и записью в инвариантной культуре.
   /// NaN и бесконечности пишутся строками, иначе снимок нельзя было бы разобрать.
   /// </summary>
   static JsonNode Num(double value)
   {
      if (double.IsNaN(value)) return JsonValue.Create("NaN")!;
      if (double.IsPositiveInfinity(value)) return JsonValue.Create("Infinity")!;
      if (double.IsNegativeInfinity(value)) return JsonValue.Create("-Infinity")!;

      double rounded = Math.Round(value, 9, MidpointRounding.AwayFromZero);
      return JsonValue.Create(rounded.ToString("R", CultureInfo.InvariantCulture))!;
   }

   static string Hash(string json)
   {
      byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
      byte[] digest = SHA256.HashData(bytes);
      return Convert.ToHexString(digest)[..16].ToLowerInvariant();
   }
}
