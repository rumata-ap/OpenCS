using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using CScore.Fem;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка результата проверки ширины трещин одной полосы пластины.</summary>
public sealed class ShellLayeredSlsStripRowVM
{
   public string LayerText { get; init; } = "";
   public string FaceText { get; init; } = "";
   public string DirectionText { get; init; } = "";
   public string McrcText { get; init; } = "";
   public string SigmaSText { get; init; } = "";
   public string SigmaSCrcText { get; init; } = "";
   public string PsiSText { get; init; } = "";
   public string LsText { get; init; } = "";
   public string AcrcText { get; init; } = "";
   public string AngleText { get; init; } = "";
   public bool Cracked { get; init; }
}

/// <summary>Представление результата SLS-проверки слоистой пластины.</summary>
public sealed class ShellLayeredSlsResultVM : ViewModelBase
{
   static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

   public string TaskTag { get; }
   public string SectionTag { get; }
   public string CreatedText { get; }
   public string VerdictText { get; }
   public string UtilizationText { get; }
   public string GoverningText { get; }
   public Brush VerdictBrush { get; }
   public Brush VerdictBackground { get; }
   public IReadOnlyList<ShellLayeredSlsStripRowVM> Rows { get; }

   public ShellLayeredSlsResultVM(string dataJson, string taskTag, string sectionTag, string createdText)
   {
      TaskTag = taskTag;
      SectionTag = sectionTag;
      CreatedText = createdText;

      var data = JsonSerializer.Deserialize<ShellLayeredSlsResultData>(dataJson)
         ?? new ShellLayeredSlsResultData();
      data.Strips ??= [];
      var governing = data.GoverningIndex >= 0 && data.GoverningIndex < data.Strips.Count
         ? data.Strips[data.GoverningIndex]
         : null;
      string face = governing?.IsTop == true ? Loc.S("ShellLayeredSlsFaceTop")
         : Loc.S("ShellLayeredSlsFaceBottom");
      GoverningText = governing is null
         ? "—"
         : $"{(string.IsNullOrWhiteSpace(governing.LayerName) ? $"#{governing.LayerIndex}" : governing.LayerName)} / {face} / {governing.Direction}";
      UtilizationText = data.Utilization.ToString("F2", Inv);

      Rows = data.Strips.Select(s => new ShellLayeredSlsStripRowVM
      {
         LayerText = string.IsNullOrWhiteSpace(s.LayerName) ? $"#{s.LayerIndex}" : s.LayerName,
         FaceText = s.IsTop ? Loc.S("ShellLayeredSlsFaceTop") : Loc.S("ShellLayeredSlsFaceBottom"),
         DirectionText = s.Direction,
         McrcText = s.Mcrc.ToString("F2", Inv),
         SigmaSText = s.Cracked ? (s.SigmaS / 1000.0).ToString("F1", Inv) : "—",
         SigmaSCrcText = s.Cracked ? (s.SigmaSCrc / 1000.0).ToString("F1", Inv) : "—",
         PsiSText = s.Cracked ? s.PsiS.ToString("F2", Inv) : "—",
         LsText = s.Cracked ? s.LsM.ToString("F3", Inv) : "—",
         AcrcText = s.Cracked ? s.AcrcMm.ToString("F3", Inv) : Loc.S("ShellLayeredSlsNoCracks"),
         AngleText = $"{s.CrackAngleDeg:F1}°",
         Cracked = s.Cracked,
      }).ToList();

      if (!data.Converged)
      {
         VerdictText = Loc.S("ShellLayeredSlsNoConvergence");
         VerdictBrush = Brushes.DarkRed;
         VerdictBackground = new SolidColorBrush(Color.FromRgb(0xF8, 0xE0, 0xE0));
      }
      else
      {
         string key = data.Passed ? "ShellLayeredSlsVerdictPassed" : "ShellLayeredSlsVerdictNotPassed";
         VerdictText = string.Format(Loc.S(key), data.AcrcMaxMm, data.AcrcLimMm,
            UtilizationText, GoverningText);
         VerdictBrush = data.Passed ? Brushes.DarkGreen : Brushes.DarkRed;
         VerdictBackground = new SolidColorBrush(data.Passed
            ? Color.FromRgb(0xE0, 0xF0, 0xE0)
            : Color.FromRgb(0xF8, 0xE0, 0xE0));
      }
   }
}
