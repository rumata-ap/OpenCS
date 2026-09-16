using CScore;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

public sealed class ShellLayeredCrackWidthReportProviderTests
{
   const string PassedJson = """
      {"Strips":[
         {"LayerIndex":0,"LayerName":"верх","Direction":"x","Z":0.0875,"IsTop":true,
          "MDes":50,"NDes":0,"Mcrc":8.5,"Cracked":true,
          "SigmaS":236400,"SigmaSCrc":40000,"PsiS":0.86,"Phi1":1.0,"Phi2":0.5,"Phi3":1.0,
          "LsM":0.15,"AcrcMm":0.15,"CrackAngleDeg":90.0},
         {"LayerIndex":0,"LayerName":"верх","Direction":"y","Z":0.0875,"IsTop":true,
          "MDes":10,"NDes":0,"Mcrc":8.5,"Cracked":false,
          "SigmaS":0,"SigmaSCrc":0,"PsiS":0,"Phi1":0,"Phi2":0,"Phi3":0,
          "LsM":0,"AcrcMm":0,"CrackAngleDeg":0.0}
      ],
      "GoverningIndex":0,"AcrcMaxMm":0.15,"AcrcLimMm":0.3,"Utilization":0.5,"Passed":true,"Converged":true}
      """;

   static CalcTask MakeTask(string paramsJson = "{}") => new()
   {
      Id = 5, Kind = "shell_layered_sls", Tag = "Плита П-1",
      CalcType = CalcType.C, ParamsJson = paramsJson,
   };

   [Fact]
   public void Provider_BuildsReportWithAllStrips()
   {
      var task = MakeTask("""{"mx":50,"my":10,"phi1":1.0,"phi2":0.5,"acrc_lim_mm":0.3}""");
      var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = PassedJson };

      var document = new ShellLayeredCrackWidthReportProvider().Build(new ReportContext(task, result));

      var tables = document.Blocks.OfType<ReportTable>().ToList();
      var allCells = tables.SelectMany(t => t.Rows).SelectMany(r => r).ToList();
      Assert.Contains(allCells, c => c.Contains("90"));
      Assert.Contains(allCells, c => c.Contains("40"));
      Assert.Contains(allCells, c => c == "x");
      Assert.Contains(allCells, c => c == "y");
      var kvTables = document.Blocks.OfType<ReportKeyValueTable>().ToList();
      Assert.Contains(kvTables, t => t.Rows.Any(r => r.Key == "Вердикт" && r.Value.Contains("выполнено")));
      _ = new HtmlReportRenderer().Render(document);
   }

   [Fact]
   public void Provider_RejectsUnsupportedKind()
   {
      var task = new CalcTask { Kind = "cracking" };
      var result = new CalcResult { TaskKind = task.Kind, DataJson = PassedJson };
      Assert.Throws<ArgumentException>(() =>
         new ShellLayeredCrackWidthReportProvider().Build(new ReportContext(task, result)));
   }
}
