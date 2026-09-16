using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка пакетного результата проверки ширины трещин пластины.</summary>
public sealed class ShellLayeredSlsBatchRowVM
{
   public int Num { get; init; }
   public string Label { get; init; } = "";
   public string StatusText { get; init; } = "";
   public string AcrcText { get; init; } = "";
   public string AngleText { get; init; } = "";
   public string DirectionFaceText { get; init; } = "";
}

/// <summary>Представление пакетного SLS-результата слоистой пластины.</summary>
public sealed class ShellLayeredSlsBatchResultVM : ViewModelBase
{
   static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

   public IReadOnlyList<ShellLayeredSlsBatchRowVM> Rows { get; }

   public ShellLayeredSlsBatchResultVM(string dataJson)
   {
      using var doc = JsonDocument.Parse(dataJson);
      var rows = new List<ShellLayeredSlsBatchRowVM>();
      if (doc.RootElement.TryGetProperty("rows", out var rowsEl))
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
               StatusText = LocalizeStatus(r.GetProperty("status").GetString() ?? ""),
               AcrcText = acrc?.ToString("F3", Inv) ?? "—",
               AngleText = angle.HasValue ? $"{angle:F1}°" : "—",
               DirectionFaceText = string.IsNullOrEmpty(direction) ? "—" : $"{direction} / {face}",
            });
         }
      }
      Rows = rows;
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
