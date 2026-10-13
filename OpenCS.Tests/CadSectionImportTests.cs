using CScore;
using CScore.Import.Cad;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Units;
using OpenCS.Services;
using OpenCS.Services.Cad;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Импорт сечений из DXF: чтение netDxf → распознавание → сборка → сохранение в проект.</summary>
public sealed class CadSectionImportTests
{
   static readonly CadArcDiscretization Arcs = CadArcDiscretization.ByDeviation(0.1);

   static Layer L(DxfDocument d, string name)
   {
      if (!d.Layers.Contains(name)) d.Layers.Add(new Layer(name));
      return d.Layers[name];
   }

   static Polyline2D Rect(Layer layer, double x, double y, double w, double h) =>
      new(new Vector2[] { new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h) }, true) { Layer = layer };

   /// <summary>
   /// Лист: рамка 1 — колонна 300×500 с 4 стержнями и подписью; рамка 2 — колонна из блока (объекты блока на слое «0»,
   /// вставка на слое бетона, повёрнута на 90°); рамка 3 — двутавр из LINE и ARC (скругления r = 10) в бетоне 400×400;
   /// рамка 4 — кольцо (две концентрические окружности бетона).
   /// </summary>
   static DxfDocument Sheet()
   {
      var d = new DxfDocument();
      d.DrawingVariables.InsUnits = DrawingUnits.Millimeters;
      Layer frame = L(d, "РАМКИ"), conc = L(d, "Бетон_B25"), steel = L(d, "Сталь_С245"), reb = L(d, "Арм_A500"), lab = L(d, "Подписи");
      void Frame(double x, string name)
      {
         d.Entities.Add(Rect(frame, x, 0, 1200, 1200));
         d.Entities.Add(new Text(name, new Vector2(x + 50, 80), 60) { Layer = lab });
      }

      Frame(0, "К-1");
      d.Entities.Add(Rect(conc, 450, 350, 300, 500));
      foreach (var (x, y) in new[] { (500.0, 400.0), (700.0, 400.0), (500.0, 800.0), (700.0, 800.0) })
         d.Entities.Add(new Circle(new Vector2(x, y), 10) { Layer = reb });

      Frame(1500, "К-2");
      var blk = new Block("КОЛОННА");
      var layer0 = L(d, "0");
      blk.Entities.Add(Rect(layer0, -150, -250, 300, 500));
      foreach (var (x, y) in new[] { (-100.0, -200.0), (100.0, -200.0), (-100.0, 200.0), (100.0, 200.0) })
         blk.Entities.Add(new Circle(new Vector2(x, y), 12.5) { Layer = reb });
      d.Entities.Add(new Insert(blk, new Vector3(2100, 600, 0)) { Layer = conc, Rotation = 90 });

      Frame(3000, "К-3");
      d.Entities.Add(Rect(conc, 3400, 400, 400, 400));
      // Двутавр 120×160, полки 12, стенка 8, скругления r = 10 между стенкой и полками — расчленён на LINE/ARC.
      double cx = 3600, cy = 600, b2 = 60, h2 = 80, tf = 12, tw2 = 4, r = 10;
      void Ln(double x1, double y1, double x2, double y2) => d.Entities.Add(new Line(new Vector2(x1, y1), new Vector2(x2, y2)) { Layer = steel });
      void Ar(double ax, double ay, double s, double e) => d.Entities.Add(new Arc(new Vector2(ax, ay), r, s, e) { Layer = steel });
      Ln(cx - b2, cy - h2, cx + b2, cy - h2);
      Ln(cx + b2, cy - h2, cx + b2, cy - h2 + tf);
      Ln(cx + b2, cy - h2 + tf, cx + tw2 + r, cy - h2 + tf);
      Ar(cx + tw2 + r, cy - h2 + tf + r, 180, 270);
      Ln(cx + tw2, cy - h2 + tf + r, cx + tw2, cy + h2 - tf - r);
      Ar(cx + tw2 + r, cy + h2 - tf - r, 90, 180);
      Ln(cx + tw2 + r, cy + h2 - tf, cx + b2, cy + h2 - tf);
      Ln(cx + b2, cy + h2 - tf, cx + b2, cy + h2);
      Ln(cx + b2, cy + h2, cx - b2, cy + h2);
      Ln(cx - b2, cy + h2, cx - b2, cy + h2 - tf);
      Ln(cx - b2, cy + h2 - tf, cx - tw2 - r, cy + h2 - tf);
      Ar(cx - tw2 - r, cy + h2 - tf - r, 0, 90);
      Ln(cx - tw2, cy + h2 - tf - r, cx - tw2, cy - h2 + tf + r);
      Ar(cx - tw2 - r, cy - h2 + tf + r, 270, 360);
      Ln(cx - tw2 - r, cy - h2 + tf, cx - b2, cy - h2 + tf);
      Ln(cx - b2, cy - h2 + tf, cx - b2, cy - h2);

      Frame(4500, "К-4");
      d.Entities.Add(new Circle(new Vector2(5100, 600), 300) { Layer = conc });
      d.Entities.Add(new Circle(new Vector2(5100, 600), 200) { Layer = conc });
      return d;
   }

   static CadRecognitionResult Recognize(CadSnapshot s)
   {
      var o = new CadImportOptions { MetersPerUnit = s.MetersPerUnit ?? 0.001 }.GuessRoles(s);
      return CadSectionRecognizer.Recognize(s, o);
   }

   [Fact]
   public void DxfSheet_ReadAndRecognized()
   {
      string path = Path.Combine(Path.GetTempPath(), $"opencs-cad-{Guid.NewGuid():N}.dxf");
      try
      {
         Sheet().Save(path);
         var s = DxfSnapshotReader.Read(path, Arcs, 0.01);
         Assert.Equal(0.001, s.MetersPerUnit);
         Assert.Empty(s.Warnings);
         var r = Recognize(s);
         Assert.Empty(r.Warnings);
         Assert.Equal(["К-1", "К-2", "К-3", "К-4"], r.Sections.Select(x => x.Name));

         var k1 = r.Sections[0];
         Assert.Equal(0.15, Assert.Single(k1.Regions).Area, 9);
         Assert.Equal(4, k1.Bars.Count);

         // Блок на слое «0» → слой вставки (бетон); поворот 90° — сечение 500×300.
         var k2 = r.Sections[1];
         var reg2 = Assert.Single(k2.Regions);
         Assert.Equal("Бетон_B25", reg2.Layer);
         Assert.Equal(0.5, reg2.Outer.Max(p => p.X) - reg2.Outer.Min(p => p.X), 9);
         Assert.All(k2.Bars, b => Assert.Equal(0.025, b.DiameterM, 12));

         // Двутавр со скруглениями из LINE/ARC: площадь = полки + стенка + 4 скругления (1 − π/4)·r².
         var k3 = r.Sections[2];
         var st = k3.Regions.Single(x => x.Role == CadLayerRole.Steel);
         double exact = (2 * 120 * 12 + 8 * 136 + 4 * (1 - Math.PI / 4) * 100) * 1e-6;
         Assert.Equal(exact, st.Area, exact * 2e-3);
         Assert.Single(k3.Regions.Single(x => x.Role == CadLayerRole.Concrete).Holes);

         var k4 = r.Sections[3];
         var ring = Assert.Single(k4.Regions);
         Assert.Single(ring.Holes);
         Assert.Equal(Math.PI * 0.05, ring.Area, Math.PI * 0.05 * 1e-3);
         Assert.Equal(0, k4.Origin.X - 5100, 6);
      }
      finally { File.Delete(path); }
   }

   static string CatalogDirectory()
   {
      var dir = new DirectoryInfo(AppContext.BaseDirectory);
      while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
      return Path.Combine(dir!.FullName, "OpenCS", "DataSource");
   }

   [Fact]
   public void Sections_SavedAndReloaded_WithHolesAndRebarHost()
   {
      string dxf = Path.Combine(Path.GetTempPath(), $"opencs-cad-{Guid.NewGuid():N}.dxf");
      string dbPath = Path.Combine(Path.GetTempPath(), $"opencs-cad-{Guid.NewGuid():N}.db");
      try
      {
         Sheet().Save(dxf);
         var r = Recognize(DxfSnapshotReader.Read(dxf, Arcs, 0.01));

         var db = new DatabaseService(dbPath);
         var concrete = MaterialCatalog.CreateHeavyConcrete("B25", CatalogDirectory())!;
         var steel = MaterialCatalog.CreateRebar("А500С", CatalogDirectory())!;
         db.AddMaterial(concrete);
         db.AddMaterial(steel);
         Material? Of(string layer, CadLayerRole role) => role == CadLayerRole.Concrete ? concrete : steel;

         var sections = r.Sections.Select(c => CadSectionBuilder.Build(c, Of)).Select(x => Assert.IsType<CrossSection>(x.Section)).ToList();
         db.SaveNewCrossSections(sections);
         Assert.All(sections, s => Assert.NotEqual(0, s.Id));
         db.Dispose();

         var loaded = new DatabaseService(dbPath);
         loaded.LoadAll();
         Assert.Equal(["К-1", "К-2", "К-3", "К-4"], loaded.CrossSections.OrderBy(s => s.Num).Select(s => s.Tag));
         var k1 = loaded.CrossSections.Single(s => s.Tag == "К-1");
         var rebar = k1.Areas.Single(a => a.Category == AreaCategory.RebarGroup);
         var host = k1.Areas.Single(a => a.Category == AreaCategory.Region);
         Assert.Equal(host.Id, rebar.HostAreaId);
         Assert.Equal(4, rebar.Fibers.Count);
         var k3 = loaded.CrossSections.Single(s => s.Tag == "К-3");
         Assert.Equal(2, k3.Areas.Count); // бетон с отверстием и сталь, арматуры нет
         Assert.Contains(k3.Areas, a => a.Holes.Count == 1);
         loaded.Dispose();
      }
      finally
      {
         File.Delete(dxf);
         try { File.Delete(dbPath); } catch (IOException) { }
      }
   }
}
