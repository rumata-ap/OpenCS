using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;
using CScore.Fem;

namespace OpenCS.Reporting;

/// <summary>Поставщик отчёта по ширине раскрытия трещин слоистой пластины.</summary>
public sealed class ShellLayeredCrackWidthReportProvider : IReportProvider
{
   static readonly JsonSerializerOptions JsonOptions = new()
   {
      PropertyNameCaseInsensitive = true,
      NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
   };

   public string TaskKind => "shell_layered_sls";
   public IReadOnlyCollection<string> SupportedKinds => [TaskKind];
   public bool CanHandle(CalcTask task) => SupportedKinds.Contains(task.Kind, StringComparer.Ordinal);

   public ReportDocument Build(ReportContext context)
   {
      ArgumentNullException.ThrowIfNull(context);
      if (!CanHandle(context.Task))
         throw new ArgumentException("Поставщик не поддерживает данный тип задачи.", nameof(context));

      var data = JsonSerializer.Deserialize<ShellLayeredSlsReportData>(context.Result.DataJson, JsonOptions)
         ?? new ShellLayeredSlsReportData();
      data.Strips ??= [];
      data.Variables ??= [];
      var parameters = ParseParams(context.Task.ParamsJson);
      string tag = string.IsNullOrWhiteSpace(context.Task.Tag) ? "без метки" : context.Task.Tag;
      var document = new ReportDocument($"Ширина раскрытия трещин слоистой пластины — {tag}");
      SectionReportSections.Identification(document, context,
         "N — кН/м; M — кН·м/м; acrc, acrc,lim — мм; угол трещины — ° относительно оси X");

      document
         .Add(new ReportHeading(1, "Исходные данные"))
         .Add(new ReportKeyValueTable(
         [
            ("Nx, кН/м", F(parameters.Nx)), ("Ny, кН/м", F(parameters.Ny)), ("Nxy, кН/м", F(parameters.Nxy)),
            ("Mx, кН·м/м", F(parameters.Mx)), ("My, кН·м/м", F(parameters.My)), ("Mxy, кН·м/м", F(parameters.Mxy)),
            ("φ1 (длительность действия нагрузки), п. 8.2.10", F(parameters.Phi1)),
            ("φ2 (профиль арматуры), п. 8.2.10", F(parameters.Phi2)),
            ("acrc,lim, мм", F(parameters.AcrcLimMm)),
         ], "Параметр", "Значение"))
         .Add(new ReportHeading(1, "Переменные расчёта"))
         .Add(new ReportKeyValueTable(
            data.Variables.OrderBy(kv => kv.Key).Select(kv => (kv.Key, F(kv.Value))).ToList(),
            "Переменная", "Значение"))
         .Add(new ReportHeading(1, "Вердикт"))
         .Add(new ReportKeyValueTable(
         [
            ("Сошлась ли деформационная модель", data.Converged ? "да" : "нет"),
            ("acrc,max, мм", F(data.AcrcMaxMm)),
            ("Коэффициент использования", F(data.Utilization)),
            ("Определяющая полоса", GoverningText(data)),
            ("Вердикт", data.Converged
               ? (data.Passed ? "acrc ≤ acrc,lim — условие выполнено" : "acrc > acrc,lim — условие не выполнено")
               : "Нет сходимости НДС"),
         ], "Параметр", "Значение"));

      document
         .Add(new ReportHeading(1, "Полосы (все слои × направления)"))
         .Add(new ReportTable(
            ["Слой", "Грань", "Направление", "Mcrc, кН·м/м", "Трещины", "σs, МПа", "σs,crc, МПа", "ψs", "ls, м", "acrc, мм", "Угол, °"],
            data.Strips.Select(s => (IReadOnlyList<string>)[
               string.IsNullOrWhiteSpace(s.LayerName) ? $"#{s.LayerIndex}" : s.LayerName,
               s.IsTop ? "верх" : "низ",
               s.Direction,
               F(s.Mcrc),
               s.Cracked ? "да" : "нет",
               s.Cracked ? F(s.SigmaS / 1000.0) : "—",
               s.Cracked ? F(s.SigmaSCrc / 1000.0) : "—",
               s.Cracked ? F(s.PsiS) : "—",
               s.Cracked ? F(s.LsM) : "—",
               F(s.AcrcMm),
               F(s.CrackAngleDeg),
            ]).ToList()));

      if (data.Converged && !data.Passed)
         document.Add(new ReportWarning("Ширина раскрытия трещин превышает предельно допустимую хотя бы для одной полосы."));
      if (!data.Converged)
         document.Add(new ReportWarning("Деформационное состояние пластины не сошлось — результат недостоверен."));

      return document;
   }

   static string F(double value) => SectionReportSections.F(value);

   static string GoverningText(ShellLayeredSlsReportData data)
   {
      var s = data.GoverningIndex >= 0 && data.GoverningIndex < data.Strips.Count
         ? data.Strips[data.GoverningIndex]
         : null;
      return s is null ? "—"
         : $"{(string.IsNullOrWhiteSpace(s.LayerName) ? $"#{s.LayerIndex}" : s.LayerName)} / {(s.IsTop ? "верх" : "низ")} / {s.Direction}";
   }

   static ShellLayeredSlsReportParams ParseParams(string? json) =>
      string.IsNullOrWhiteSpace(json)
         ? new ShellLayeredSlsReportParams()
         : JsonSerializer.Deserialize<ShellLayeredSlsReportParams>(json, JsonOptions)
           ?? new ShellLayeredSlsReportParams();

   sealed class ShellLayeredSlsReportData
   {
      public List<ShellCrackStripResult> Strips { get; set; } = [];
      public int GoverningIndex { get; set; } = -1;
      public double AcrcMaxMm { get; set; }
      public double AcrcLimMm { get; set; }
      public double Utilization { get; set; }
      public bool Passed { get; set; }
      public bool Converged { get; set; }
      public Dictionary<string, double> Variables { get; set; } = [];
   }

   sealed class ShellLayeredSlsReportParams
   {
      [JsonPropertyName("nx")] public double Nx { get; set; }
      [JsonPropertyName("ny")] public double Ny { get; set; }
      [JsonPropertyName("nxy")] public double Nxy { get; set; }
      [JsonPropertyName("mx")] public double Mx { get; set; }
      [JsonPropertyName("my")] public double My { get; set; }
      [JsonPropertyName("mxy")] public double Mxy { get; set; }
      [JsonPropertyName("acrc_lim_mm")] public double AcrcLimMm { get; set; } = 0.3;
      [JsonPropertyName("phi1")] public double Phi1 { get; set; } = 1.0;
      [JsonPropertyName("phi2")] public double Phi2 { get; set; } = 0.5;
   }
}
