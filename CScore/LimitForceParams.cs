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

   /// <summary>Длина элемента, м (п. 8.1.17).</summary>
   public double? EtaL { get; set; }

   /// <summary>Коэффициент расчётной длины μx (плоскость Mx) — l0x = μx·L (п. 8.1.17).</summary>
   public double? EtaMuX { get; set; }

   /// <summary>Коэффициент расчётной длины μy (плоскость My) — l0y = μy·L (п. 8.1.17).</summary>
   public double? EtaMuY { get; set; }

   /// <summary>Расчётная длина в плоскости Mx, м: l0x = L·μx (0, если L не задана).</summary>
   public double EtaL0x => (EtaL ?? 0) * (EtaMuX ?? 1.0);

   /// <summary>Расчётная длина в плоскости My, м: l0y = L·μy (0, если L не задана).</summary>
   public double EtaL0y => (EtaL ?? 0) * (EtaMuY ?? 1.0);

   /// <summary>
   /// Относительная доля длительности момента ψx = M1l/M1 в плоскости Mx
   /// (только режим A). Не зависит от абсолютной величины момента — применима
   /// к любой силовой позиции, в т.ч. в пакетных задачах.
   /// </summary>
   public double? EtaPsiX { get; set; }

   /// <summary>Относительная доля длительности момента ψy = M1l/M1 в плоскости My (только режим A).</summary>
   public double? EtaPsiY { get; set; }

   /// <summary>
   /// Предельная гибкость l0/h, выше которой требуется поправка η (по умолчанию
   /// 14 — п. 8.1.2 СП63.13330; пользователь может уточнить значение).
   /// </summary>
   public double? EtaSlendernessThreshold { get; set; }

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
         if (root.TryGetProperty("etaL",   out var lEl))   p.EtaL   = lEl.GetDouble();
         if (root.TryGetProperty("etaMuX", out var muxEl)) p.EtaMuX = muxEl.GetDouble();
         if (root.TryGetProperty("etaMuY", out var muyEl)) p.EtaMuY = muyEl.GetDouble();
         // etaPsiX/etaPsiY сериализуются как null в итерационном режиме (не запрашиваются
         // у пользователя) — GetDouble() на null-элементе бросает исключение, поэтому
         // проверяем ValueKind, а не просто наличие свойства.
         if (root.TryGetProperty("etaPsiX", out var psixEl) && psixEl.ValueKind != JsonValueKind.Null)
            p.EtaPsiX = psixEl.GetDouble();
         if (root.TryGetProperty("etaPsiY", out var psiyEl) && psiyEl.ValueKind != JsonValueKind.Null)
            p.EtaPsiY = psiyEl.GetDouble();
         if (root.TryGetProperty("etaSlendernessThreshold", out var thEl) && thEl.ValueKind != JsonValueKind.Null)
            p.EtaSlendernessThreshold = thEl.GetDouble();
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
            etaL    = EtaL    ?? 0.0,
            etaMuX  = EtaMuX  ?? 1.0,
            etaMuY  = EtaMuY  ?? 1.0,
            etaPsiX = EtaPsiX,
            etaPsiY = EtaPsiY,
            etaSlendernessThreshold = EtaSlendernessThreshold ?? Sp63.EccentricityAmplifier.SlendernessThreshold,
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
