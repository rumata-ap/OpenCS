using System.Diagnostics;
using CScore.Import.Cad;
using OpenCS.Services.Cad;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Распознавание реального листа, выгруженного из AutoCAD/nanoCAD (OPENCS_CAD_DXF — путь к DXF; без неё тест сразу
/// выходит). Ожидаемые числа — переменные OPENCS_CAD_SECTIONS (экземпляров) и OPENCS_CAD_BARS (стержней), если заданы.
/// Лист из спеки (2026-10-13-cad-sections80.dxf): 80 сечений, 642 стержня.
/// </summary>
public class CadSectionImportManualTests(ITestOutputHelper output)
{
   [Fact]
   public void RealSheet()
   {
      string? path = Environment.GetEnvironmentVariable("OPENCS_CAD_DXF");
      if (string.IsNullOrWhiteSpace(path)) return;
      var sw = Stopwatch.StartNew();
      var s = DxfSnapshotReader.Read(path, CadArcDiscretization.ByDeviation(0.1), 0.01);
      long read = sw.ElapsedMilliseconds;
      var o = new CadImportOptions { MetersPerUnit = s.MetersPerUnit ?? 0.001 }.GuessRoles(s);
      var r = CadSectionRecognizer.Recognize(s, o);
      output.WriteLine($"чтение {read} мс, распознавание {sw.ElapsedMilliseconds - read} мс");
      foreach (var kv in o.Roles) output.WriteLine($"слой «{kv.Key}» → {kv.Value}");
      output.WriteLine($"экземпляров {r.Instances}, разных сечений {r.Sections.Count}, стержней {r.Sections.Sum(x => x.Bars.Count * x.Count)}");
      foreach (var w in s.Warnings.Concat(r.Warnings)) output.WriteLine($"! {w}");
      foreach (var c in r.Sections)
         output.WriteLine($"{c.Name} ×{c.Count}: областей {c.Regions.Count}, отверстий {c.Regions.Sum(x => x.Holes.Count)}, " +
                          $"стержней {c.Bars.Count}{(c.Warnings.Count > 0 ? " | " + string.Join("; ", c.Warnings) : "")}");

      if (int.TryParse(Environment.GetEnvironmentVariable("OPENCS_CAD_SECTIONS"), out int n)) Assert.Equal(n, r.Instances);
      if (int.TryParse(Environment.GetEnvironmentVariable("OPENCS_CAD_BARS"), out int nb))
         Assert.Equal(nb, r.Sections.Sum(x => x.Bars.Count * x.Count));
   }
}
