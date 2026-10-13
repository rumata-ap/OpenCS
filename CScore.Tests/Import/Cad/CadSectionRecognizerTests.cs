using CScore.Import.Cad;
using Xunit;

namespace CScore.Tests.Import.Cad;

/// <summary>Распознавание множества сечений на одном листе чертежа (единицы чертежа — мм).</summary>
public class CadSectionRecognizerTests
{
   const string Conc = "Бетон_B25", Steel = "Сталь_С245", Rebar = "Арм_A500", Hole = "Отверстие", Frame = "РАМКИ", Label = "Подписи";

   static CadPolygon Rect(string layer, double x, double y, double w, double h) =>
      new(layer, [new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)]);

   static CadPolygon Poly(string layer, params double[] xy) =>
      new(layer, Enumerable.Range(0, xy.Length / 2).Select(i => new CadPoint(xy[2 * i], xy[2 * i + 1])).ToList());

   /// <summary>Колонна b×h с левым нижним углом (x, y) и четырьмя стержнями d в углах с привязкой a.</summary>
   static void Column(CadSnapshot s, double x, double y, double b = 300, double h = 500, double d = 20, double a = 50)
   {
      s.Polygons.Add(Rect(Conc, x, y, b, h));
      foreach (var (px, py) in new[] { (x + a, y + a), (x + b - a, y + a), (x + a, y + h - a), (x + b - a, y + h - a) })
         s.Circles.Add(new CadCircle(Rebar, px, py, d / 2));
   }

   static CadImportOptions Options(CadSnapshot s) => new CadImportOptions().GuessRoles(s);

   static CadPoint NetCentroid(CadSectionCandidate c)
   {
      double a = 0, sx = 0, sy = 0;
      void Add(List<CadPoint> ring, double sign)
      {
         double ar = Math.Abs(CadGeometry.SignedArea(ring));
         var g = CadGeometry.Centroid(ring);
         a += sign * ar; sx += sign * ar * g.X; sy += sign * ar * g.Y;
      }
      // Вложенные области (сталь в бетоне) уже вычтены отверстием носителя — складываем все области и вычитаем все отверстия.
      foreach (var r in c.Regions)
      {
         Add(r.Outer, 1);
         foreach (var h in r.Holes) Add(h, -1);
      }
      return new(sx / a, sy / a);
   }

   [Fact]
   public void SingleColumn_CentroidAtOrigin_BarsHostedInConcrete()
   {
      var s = new CadSnapshot();
      Column(s, 1000, 2000);
      var r = CadSectionRecognizer.Recognize(s, Options(s));

      var c = Assert.Single(r.Sections);
      Assert.Equal("Сеч. 1", c.Name);
      Assert.Equal(new CadPoint(1150, 2250), c.Origin);
      var reg = Assert.Single(c.Regions);
      Assert.Equal(CadLayerRole.Concrete, reg.Role);
      Assert.Equal(0.15, reg.Area, 9);
      Assert.True(CadGeometry.SignedArea(reg.Outer) > 0);
      Assert.Equal(4, c.Bars.Count);
      Assert.All(c.Bars, b => { Assert.Equal(0, b.Host); Assert.Equal(0.020, b.DiameterM, 12); });
      Assert.Contains(c.Bars, b => Math.Abs(b.X + 0.1) < 1e-12 && Math.Abs(b.Y + 0.2) < 1e-12);
      Assert.Empty(c.Warnings);
   }

   [Fact]
   public void AsymmetricHole_CentroidOfNetAreaIsZero()
   {
      var s = new CadSnapshot();
      Column(s, 0, 0, 400, 600);
      s.Polygons.Add(Rect(Hole, 80, 300, 100, 150));
      var c = Assert.Single(CadSectionRecognizer.Recognize(s, Options(s)).Sections);

      var reg = Assert.Single(c.Regions);
      var hole = Assert.Single(reg.Holes);
      Assert.True(CadGeometry.SignedArea(hole) < 0);
      Assert.Equal(0.24 - 0.015, reg.Area, 9);
      var g = NetCentroid(c);
      Assert.Equal(0, g.X, 9);
      Assert.Equal(0, g.Y, 9);
   }

   [Fact]
   public void SameLayerNestedContour_IsHole_HollowBox()
   {
      var s = new CadSnapshot();
      s.Polygons.Add(Rect(Conc, 0, 0, 600, 600));
      s.Polygons.Add(Rect(Conc, 100, 100, 400, 400));
      var c = Assert.Single(CadSectionRecognizer.Recognize(s, Options(s)).Sections);
      var reg = Assert.Single(c.Regions);
      Assert.Single(reg.Holes);
      Assert.Equal(0.36 - 0.16, reg.Area, 9);
      Assert.Contains("нет арматуры", c.Warnings);
   }

   [Fact]
   public void SteelInsideConcrete_CutsHoleAndBecomesSteelRegion()
   {
      var s = new CadSnapshot();
      Column(s, 0, 0, 400, 400);
      // Двутавр 120×160 в центре.
      double cx = 200, cy = 200, fw = 60, hh = 80, tw = 4, tf = 6;
      s.Polygons.Add(Poly(Steel, cx - fw, cy - hh, cx + fw, cy - hh, cx + fw, cy - hh + tf, cx + tw, cy - hh + tf,
                                 cx + tw, cy + hh - tf, cx + fw, cy + hh - tf, cx + fw, cy + hh, cx - fw, cy + hh,
                                 cx - fw, cy + hh - tf, cx - tw, cy + hh - tf, cx - tw, cy - hh + tf, cx - fw, cy - hh + tf));
      var c = Assert.Single(CadSectionRecognizer.Recognize(s, Options(s)).Sections);

      Assert.Equal(2, c.Regions.Count);
      var conc = c.Regions.Single(r => r.Role == CadLayerRole.Concrete);
      var steel = c.Regions.Single(r => r.Role == CadLayerRole.Steel);
      Assert.Single(conc.Holes);
      Assert.Empty(steel.Holes);
      double steelArea = 2 * 120 * 6 + 8 * 148;
      Assert.Equal(steelArea * 1e-6, steel.Area, 9);
      Assert.Equal(0.16 - steelArea * 1e-6, conc.Area, 9);
      Assert.Equal(new CadPoint(200, 200), c.Origin);
      Assert.All(c.Bars, b => Assert.Equal(c.Regions.IndexOf(conc), b.Host));
   }

   [Fact]
   public void ConcreteFilledTube_SteelRingAndConcreteCore()
   {
      var s = new CadSnapshot();
      s.Circles.Add(new CadCircle(Steel, 0, 0, 200));
      s.Circles.Add(new CadCircle(Conc, 0, 0, 190));
      var o = Options(s);
      var c = Assert.Single(CadSectionRecognizer.Recognize(s, o).Sections);
      var steel = c.Regions.Single(r => r.Role == CadLayerRole.Steel);
      var conc = c.Regions.Single(r => r.Role == CadLayerRole.Concrete);
      Assert.Single(steel.Holes);
      Assert.Empty(conc.Holes);
      // Ломаная по хорде 10 мм: площадь кольца близка к точной.
      Assert.Equal(Math.PI * (0.2 * 0.2 - 0.19 * 0.19), steel.Area, 4);
      Assert.Equal(0, c.Origin.X, 6);
      Assert.Equal(0, c.Origin.Y, 6);
   }

   [Fact]
   public void Frames_SectionPerFrame_NamesFromLabels_OrderedRowsLeftToRight()
   {
      var s = new CadSnapshot();
      // Ряд 1: рамки (0,0) и (1500,0); ряд 2: (0,−1500).
      foreach (var (fx, fy, name, b) in new[] { (1500.0, 0.0, "С-2", 300.0), (0.0, -1500.0, "С-3", 350.0), (0.0, 0.0, "С-1", 400.0) })
      {
         s.Polygons.Add(Rect(Frame, fx, fy, 1200, 1200));
         Column(s, fx + 300, fy + 300, b);
         s.Texts.Add(new CadText(Label, fx + 50, fy + 80, 60, name));
      }
      var r = CadSectionRecognizer.Recognize(s, Options(s));
      Assert.Equal(["С-1", "С-2", "С-3"], r.Sections.Select(x => x.Name));
      Assert.All(r.Sections, x => Assert.Equal(4, x.Bars.Count));
      Assert.Equal(new CadBounds(0, 0, 1200, 1200), r.Sections[0].Instances[0]);
      Assert.Empty(r.Warnings);
   }

   [Fact]
   public void WithoutFrames_LabelGoesToNearestSection()
   {
      var s = new CadSnapshot();
      Column(s, 0, 0);
      Column(s, 2000, 0, 400);
      s.Texts.Add(new CadText(Label, 100, -150, 50, "К1"));
      s.Texts.Add(new CadText(Label, 2100, -150, 50, "К2"));
      s.Texts.Add(new CadText(Label, 9000, 9000, 50, "далеко"));
      var r = CadSectionRecognizer.Recognize(s, Options(s));
      Assert.Equal(["К1", "К2"], r.Sections.Select(x => x.Name));
   }

   [Fact]
   public void IdenticalSections_MergedWithAllNames()
   {
      var s = new CadSnapshot();
      for (int i = 0; i < 3; i++)
      {
         Column(s, i * 1000, 0);
         s.Texts.Add(new CadText(Label, i * 1000 + 100, -100, 50, $"К{i + 1}"));
      }
      // Тот же контур, но полилиния начинается с другой вершины и обходится по часовой.
      s.Polygons.Add(Poly(Conc, 3300, 500, 3300, 0, 3000, 0, 3000, 500));
      foreach (var (px, py) in new[] { (3050.0, 50.0), (3250.0, 50.0), (3050.0, 450.0), (3250.0, 450.0) })
         s.Circles.Add(new CadCircle(Rebar, px, py, 10));
      s.Texts.Add(new CadText(Label, 3100, -100, 50, "К4"));
      Column(s, 4000, 0, d: 25);
      s.Texts.Add(new CadText(Label, 4100, -100, 50, "К5"));

      var r = CadSectionRecognizer.Recognize(s, Options(s));
      Assert.Equal(2, r.Sections.Count);
      Assert.Equal(["К1", "К2", "К3", "К4"], r.Sections[0].Names);
      Assert.Equal(4, r.Sections[0].Count);
      Assert.Equal(["К5"], r.Sections[1].Names);
      Assert.Equal(5, r.Instances);

      var o = Options(s);
      o.MergeIdentical = false;
      Assert.Equal(5, CadSectionRecognizer.Recognize(s, o).Sections.Count);
   }

   [Fact]
   public void Bars_SnappedToAssortment_OutsideAndCrossingReported()
   {
      var s = new CadSnapshot();
      s.Polygons.Add(Rect(Conc, 0, 0, 300, 300));
      s.Circles.Add(new CadCircle(Rebar, 50, 50, 9.9));    // Ø19,8 → 20
      s.Circles.Add(new CadCircle(Rebar, 250, 50, 9.0));   // Ø18 — точно
      s.Circles.Add(new CadCircle(Rebar, 150, 150, 10.6)); // Ø21,2 — вне сортамента
      s.Circles.Add(new CadCircle(Rebar, 5, 150, 10));     // пересекает грань
      s.Circles.Add(new CadCircle(Rebar, 20, 150, 10));    // перекрывает предыдущий
      var c = Assert.Single(CadSectionRecognizer.Recognize(s, Options(s)).Sections);
      Assert.Equal(0.020, c.Bars[0].DiameterM, 12);
      Assert.Equal(0.018, c.Bars[1].DiameterM, 12);
      Assert.Equal(0.0212, c.Bars[2].DiameterM, 12);
      Assert.Contains("диаметров вне сортамента: 1", c.Warnings);
      Assert.Contains("стержней, пересекающих границу области: 1", c.Warnings);
      Assert.Contains("перекрывающихся стержней: 1", c.Warnings);
   }

   [Fact]
   public void RoundColumn_FromCircleOnConcreteLayer()
   {
      var s = new CadSnapshot();
      s.Circles.Add(new CadCircle(Conc, 500, 500, 200));
      for (int i = 0; i < 6; i++)
      {
         double a = i * Math.PI / 3;
         s.Circles.Add(new CadCircle(Rebar, 500 + 150 * Math.Cos(a), 500 + 150 * Math.Sin(a), 8));
      }
      var c = Assert.Single(CadSectionRecognizer.Recognize(s, Options(s)).Sections);
      Assert.Equal(Math.PI * 0.04, c.Regions[0].Area, 3);
      Assert.Equal(6, c.Bars.Count);
      Assert.All(c.Bars, b => Assert.Equal(0, b.Host));
   }

   [Fact]
   public void Within_KeepsOnlyObjectsInsideWindow()
   {
      var s = new CadSnapshot();
      Column(s, 0, 0);
      Column(s, 2000, 0);
      var part = s.Within(new CadBounds(-10, -10, 400, 600));
      Assert.Single(CadSectionRecognizer.Recognize(part, Options(part)).Sections);
   }

   [Theory]
   [InlineData("Бетон_B25", CadLayerRole.Concrete, "B25")]
   [InlineData("бетон В30", CadLayerRole.Concrete, "B30")]   // кириллическая В
   [InlineData("Арм_A500", CadLayerRole.Rebar, "A500")]
   [InlineData("А400", CadLayerRole.Rebar, "A400")]
   [InlineData("Сталь_С245", CadLayerRole.Steel, "C245")]
   [InlineData("РАМКИ", CadLayerRole.Frame, null)]
   [InlineData("Отверстие", CadLayerRole.Hole, null)]
   [InlineData("Подписи", CadLayerRole.Label, null)]
   [InlineData("0", CadLayerRole.Ignore, null)]
   public void LayerRoleAndClassGuess(string layer, CadLayerRole role, string? cls)
   {
      var guessed = CadLayerRules.Guess(new CadLayerInfo(layer) { Polygons = 1 });
      Assert.Equal(role, guessed);
      Assert.Equal(cls, CadLayerRules.GuessClass(layer, role));
   }

   [Fact]
   public void BulgeArc_SemicircleBelowChord()
   {
      var pts = CadGeometry.BulgeArc(new CadPoint(-1, 0), new CadPoint(1, 0), 1.0, 0.1).ToList();
      Assert.True(pts.Count > 10);
      Assert.All(pts, p => Assert.Equal(1, Math.Sqrt(p.X * p.X + p.Y * p.Y), 9));
      Assert.All(pts, p => Assert.True(p.Y < 0));
   }
}
