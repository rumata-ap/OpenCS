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
      {
         double Area(CadLayerRole role) => c.Regions.Where(x => x.Role == role).Sum(x => x.Area) * 1e4;
         output.WriteLine($"{c.Name} ×{c.Count}: бетон {Area(CadLayerRole.Concrete):0.00} см², сталь {Area(CadLayerRole.Steel):0.00} см², " +
                          $"областей {c.Regions.Count}, отверстий {c.Regions.Sum(x => x.Holes.Count)}, стержней {c.Bars.Count}" +
                          $"{(c.Warnings.Count > 0 ? " | " + string.Join("; ", c.Warnings) : "")}");
      }

      if (int.TryParse(Environment.GetEnvironmentVariable("OPENCS_CAD_SECTIONS"), out int n)) Assert.Equal(n, r.Instances);
      if (int.TryParse(Environment.GetEnvironmentVariable("OPENCS_CAD_BARS"), out int nb))
         Assert.Equal(nb, r.Sections.Sum(x => x.Bars.Count * x.Count));
   }

   /// <summary>
   /// Запущенные AutoCAD/nanoCAD (OPENCS_CAD_LIVE=1): поиск, чтение всего активного документа и рамкой вокруг первого
   /// распознанного сечения, зум к нему. Ввод рамки мышью (PickWindow) — только вручную в программе.
   /// </summary>
   [Fact]
   public void LiveCad()
   {
      if (Environment.GetEnvironmentVariable("OPENCS_CAD_LIVE") != "1") return;
      var found = CadComConnector.Find();
      foreach (var c in found) output.WriteLine($"найдено: {c} ({c.ProgId})");
      Assert.NotEmpty(found);
      foreach (var cad in found)
      {
         var sw = Stopwatch.StartNew();
         var s = CadComConnector.Read(cad, CadArcDiscretization.ByDeviation(0.1), 0.01);
         var o = new CadImportOptions { MetersPerUnit = s.MetersPerUnit ?? 0.001 }.GuessRoles(s);
         var r = CadSectionRecognizer.Recognize(s, o);
         output.WriteLine($"{cad.Product}: весь чертёж {sw.ElapsedMilliseconds} мс — контуров {s.Polygons.Count}, " +
                          $"окружностей {s.Circles.Count}, текстов {s.Texts.Count}; экземпляров {r.Instances}, разных {r.Sections.Count}");
         if (r.Sections.Count == 0) continue;

         var first = r.Sections[0].Instances[0];
         sw.Restart();
         var part = CadComConnector.Read(cad, CadArcDiscretization.ByDeviation(0.1), 0.01, first.Inflate(1));
         var rp = CadSectionRecognizer.Recognize(part, o);
         output.WriteLine($"{cad.Product}: рамка {sw.ElapsedMilliseconds} мс — экземпляров {rp.Instances}");
         Assert.Equal(1, rp.Instances);
         CadComConnector.ZoomTo(cad, first);
      }
   }
}
