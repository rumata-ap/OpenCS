using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using CScore.Batch;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка пакетного результата проверки ширины трещин пластины.</summary>
public sealed class ShellLayeredSlsBatchRowVM : IBatchVerdictRow
{
   public int Num { get; init; }
   public string Label { get; init; } = "";
   /// <summary>Номер КЭ источника строки набора; null — строка не из МКЭ-схемы.</summary>
   public int? Elem { get; init; }
   public string ElemText => Elem?.ToString(CultureInfo.InvariantCulture) ?? "—";
   public string StatusText { get; init; } = "";
   /// <summary>acrc, мм; -1 — нет значения (не сошлось) — для сортировки.</summary>
   public double AcrcValue { get; init; } = -1;
   public string AcrcText { get; init; } = "";
   public string AngleText { get; init; } = "";
   public string DirectionFaceText { get; init; } = "";
   /// <summary>Причина строки.</summary>
   public BatchVerdictView Reason { get; init; } = BatchVerdictView.Empty;
}

/// <summary>Представление пакетного SLS-результата слоистой пластины.</summary>
public sealed class ShellLayeredSlsBatchResultVM : ViewModelBase
{
   static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

   public IReadOnlyList<ShellLayeredSlsBatchRowVM> Rows { get; }

   /// <summary>Предел acrc,ult, мм; null — результат до 13.11.2026 (предел в нём не хранился).</summary>
   public double? AcrcLimMm { get; }

   /// <summary>Сводка: число строк и предел acrc,ult.</summary>
   public string SummaryText { get; }

   public ShellLayeredSlsBatchResultVM(string dataJson)
   {
      using var doc = JsonDocument.Parse(dataJson);
      var root = doc.RootElement;
      var rows = new List<ShellLayeredSlsBatchRowVM>();
      if (root.TryGetProperty("rows", out var rowsEl))
      {
         foreach (var r in rowsEl.EnumerateArray())
         {
            double? acrc = r.TryGetProperty("acrc_mm", out var a) && a.ValueKind == JsonValueKind.Number
               ? a.GetDouble() : null;
            double? angle = r.TryGetProperty("crack_angle_deg", out var g) && g.ValueKind == JsonValueKind.Number
               ? g.GetDouble() : null;
            string direction = r.TryGetProperty("direction", out var d) ? d.GetString() ?? "" : "";
            string face = r.TryGetProperty("face", out var f) ? f.GetString() ?? "" : "";

            rows.Add(new ShellLayeredSlsBatchRowVM
            {
               Num = r.TryGetProperty("num", out var n) && n.ValueKind == JsonValueKind.Number
                  ? n.GetInt32() : rows.Count + 1,
               Label = r.GetProperty("label").GetString() ?? "",
               Elem = r.TryGetProperty("elem", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : null,
               StatusText = LocalizeStatus(r.GetProperty("status").GetString() ?? ""),
               AcrcValue = acrc ?? -1,
               AcrcText = acrc?.ToString("F3", Inv) ?? "—",
               AngleText = angle.HasValue ? $"{angle:F1}°" : "—",
               DirectionFaceText = string.IsNullOrEmpty(direction) ? "—" : $"{direction} / {face}",
               Reason = new BatchVerdictView(BatchRowVerdictReader.Read("shell_layered_sls_batch", r)),
            });
         }
      }
      Rows = rows;
      AcrcLimMm = root.TryGetProperty("acrc_lim_mm", out var lim) && lim.ValueKind == JsonValueKind.Number
         ? lim.GetDouble() : null;
      SummaryText = string.Format(CultureInfo.CurrentCulture, Loc.S("ShellLayeredSlsBatchSummary"),
         rows.Count, AcrcLimMm?.ToString("F2", CultureInfo.CurrentCulture) ?? "—");
   }

   static string LocalizeStatus(string status) => status switch
   {
      "ok" => Loc.S("ShellLayeredSlsBatchStatusOk"),
      "not_passed" => Loc.S("ShellLayeredSlsBatchStatusNotPassed"),
      "not_converged" => Loc.S("ShellLayeredSlsBatchStatusNotConverged"),
      "partial" => Loc.S("ShellLayeredSlsBatchStatusPartial"),
      "error" => Loc.S("ShellLayeredSlsBatchStatusError"),
      _ => status,
   };
}
