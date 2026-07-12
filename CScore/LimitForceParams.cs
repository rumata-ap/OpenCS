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

   /// <summary>Учитывать влияние прогиба (η, п. 8.1.15 СП63.13330).</summary>
   public bool EtaEnabled { get; set; }

   /// <summary>false — буквальный режим (формула нормы); true — уточнённый (итерационный).</summary>
   public bool EtaIterative { get; set; }

   /// <summary>Расчётная длина по оси X (для Mx), м.</summary>
   public double? EtaL0x { get; set; }

   /// <summary>Расчётная длина по оси Y (для My), м.</summary>
   public double? EtaL0y { get; set; }

   /// <summary>Момент от длительной нагрузки, ось X (только режим A), кН·м.</summary>
   public double? EtaM1lx { get; set; }

   /// <summary>Момент от длительной нагрузки, ось Y (только режим A), кН·м.</summary>
   public double? EtaM1ly { get; set; }

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

         if (root.TryGetProperty("etaEnabled",   out var eeEl)) p.EtaEnabled   = eeEl.GetBoolean();
         if (root.TryGetProperty("etaIterative", out var eiEl)) p.EtaIterative = eiEl.GetBoolean();
         if (root.TryGetProperty("etaL0x",  out var l0xEl))  p.EtaL0x  = l0xEl.GetDouble();
         if (root.TryGetProperty("etaL0y",  out var l0yEl))  p.EtaL0y  = l0yEl.GetDouble();
         if (root.TryGetProperty("etaM1lx", out var m1lxEl)) p.EtaM1lx = m1lxEl.GetDouble();
         if (root.TryGetProperty("etaM1ly", out var m1lyEl)) p.EtaM1ly = m1lyEl.GetDouble();
      }
      catch { /* defaults */ }

      return p;
   }

   /// <summary>Сериализовать в JSON.</summary>
   public string ToJson()
   {
      if (EtaEnabled)
      {
         return JsonSerializer.Serialize(new
         {
            solver = Solver,
            N  = N  ?? 0.0,
            Mx = Mx ?? 0.0,
            My = My ?? 0.0,
            etaEnabled   = true,
            etaIterative = EtaIterative,
            etaL0x  = EtaL0x  ?? 0.0,
            etaL0y  = EtaL0y  ?? 0.0,
            etaM1lx = EtaM1lx,
            etaM1ly = EtaM1ly,
         });
      }

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
