using System.Text.Json;

namespace CScore;

/// <summary>Параметры задачи предельного нагружения (хранятся в CalcTask.ParamsJson).</summary>
public sealed class LimitForceParams
{
   /// <summary>"bisection" или "fast".</summary>
   public string Solver { get; set; } = "bisection";

   /// <summary>Ручной ввод N (кН), если задан.</summary>
   public double? N { get; set; }

   /// <summary>Ручной ввод Mx (кН·м), если задан.</summary>
   public double? Mx { get; set; }

   /// <summary>Ручной ввод My (кН·м), если задан.</summary>
   public double? My { get; set; }

   /// <summary>Есть ли ручные усилия в параметрах.</summary>
   public bool HasManualForces => N.HasValue || Mx.HasValue || My.HasValue;

   /// <summary>Разобрать JSON-параметры задачи.</summary>
   public static LimitForceParams Parse(string? json)
   {
      var p = new LimitForceParams();
      if (string.IsNullOrWhiteSpace(json) || json == "{}")
         return p;

      try
      {
         using var doc = JsonDocument.Parse(json);
         var root = doc.RootElement;
         if (root.TryGetProperty("solver", out var s))
         {
            var v = s.GetString()?.Trim().ToLowerInvariant();
            if (v is "fast" or "newton")
               p.Solver = "fast";
            else if (v is "bisection" or "bisect")
               p.Solver = "bisection";
         }

         if (root.TryGetProperty("N",  out var nEl))  p.N  = nEl.GetDouble();
         if (root.TryGetProperty("Mx", out var mxEl)) p.Mx = mxEl.GetDouble();
         if (root.TryGetProperty("My", out var myEl)) p.My = myEl.GetDouble();
      }
      catch { /* defaults */ }

      return p;
   }

   /// <summary>Сериализовать в JSON.</summary>
   public string ToJson()
   {
      if (HasManualForces)
      {
         return JsonSerializer.Serialize(new
         {
            solver = Solver,
            N  = N  ?? 0.0,
            Mx = Mx ?? 0.0,
            My = My ?? 0.0
         });
      }

      return JsonSerializer.Serialize(new { solver = Solver });
   }

   /// <summary>Создать LoadItem из ручных усилий параметров.</summary>
   public LoadItem ToLoadItem() => new()
   {
      N  = N  ?? 0.0,
      Mx = Mx ?? 0.0,
      My = My ?? 0.0
   };
}
