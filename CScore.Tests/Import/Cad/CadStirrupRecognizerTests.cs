using CScore.Import.Cad;
using Xunit;

namespace CScore.Tests.Import.Cad;

/// <summary>
/// Хомуты и шпильки из чертежа (единицы — мм): полилиния с шириной, тонкая осевая, двойной контур, с крючками,
/// шпилька; подписи «Ø8 шаг 150»; значения слоя по умолчанию; проверки.
/// </summary>
public class CadStirrupRecognizerTests
{
   const string Conc = "Бетон_B25", Reb = "Арм_A500", St = "Хомуты_A240", Lab = "Подписи";
   const double Tol = 0.01;
   static readonly double T90 = Math.Tan(Math.PI / 8); // bulge скругления 90°

   /// <summary>Колонна 300×400 (ц. т. 150; 200), 4 стержня Ø20 с центрами в 50 мм от граней, имя «К-1».</summary>
   static CadSnapshotBuilder Column()
   {
      var b = new CadSnapshotBuilder(CadArcDiscretization.ByDeviation(0.1), Tol);
      b.AddPolyline(Conc, [new(0, 0), new(300, 0), new(300, 400), new(0, 400)], closed: true);
      foreach (var (x, y) in new[] { (50.0, 50.0), (250.0, 50.0), (50.0, 350.0), (250.0, 350.0) })
         b.AddCircle(Reb, x, y, 10);
      b.AddText(Lab, 20, -60, 40, "К-1");
      return b;
   }

   static CadSectionCandidate One(CadSnapshotBuilder b, Action<CadImportOptions>? setup = null)
   {
      var s = b.Build();
      var o = new CadImportOptions().GuessRoles(s);
      setup?.Invoke(o);
      var r = CadSectionRecognizer.Recognize(s, o);
      Assert.Empty(r.Warnings);
      return Assert.Single(r.Sections);
   }

   /// <summary>Прямоугольник со скруглёнными углами радиуса r по bulge.</summary>
   static List<CadVertex> RoundedRect(double x0, double y0, double x1, double y1, double r) =>
   [
      new(x0 + r, y0), new(x1 - r, y0, T90), new(x1, y0 + r), new(x1, y1 - r, T90),
      new(x1 - r, y1), new(x0 + r, y1, T90), new(x0, y1 - r), new(x0, y0 + r, T90),
   ];

   static List<CadVertex> Rect(double x0, double y0, double x1, double y1) =>
      [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];

   static CadBounds Bounds(CadSectionStirrup s) => CadBounds.Of(s.Axis);

   /// <summary>Габарит оси хомута 36…264 × 36…364 мм в местных осях, м.</summary>
   static void AssertAxisBox(CadSectionStirrup s, double tolMm = 1e-6)
   {
      var bb = Bounds(s);
      Assert.Equal(-0.114, bb.MinX, tolMm / 1000);
      Assert.Equal(0.114, bb.MaxX, tolMm / 1000);
      Assert.Equal(-0.164, bb.MinY, tolMm / 1000);
      Assert.Equal(0.164, bb.MaxY, tolMm / 1000);
   }

   [Theory]
   [InlineData("Ø8 шаг 150", 8.0, 150.0, null)]
   [InlineData("Ø8А240 с шагом 150", 8.0, 150.0, "A240")]
   [InlineData("%%c10 s=200", 10.0, 200.0, null)]
   [InlineData("Ф12A500C, шаг 100", 12.0, 100.0, "A500C")]
   [InlineData("хомуты ⌀6,5 @250", 6.5, 250.0, null)]
   [InlineData("Шаг 120", null, 120.0, null)]
   public void Label_Parsed(string text, double? d, double? s, string? cls)
   {
      var l = CadStirrupLabel.Parse(text);
      Assert.Equal(d, l.DiameterMm);
      Assert.Equal(s, l.SpacingMm);
      Assert.Equal(cls, l.Class);
      Assert.True(l.IsAnnotation);
   }

   [Theory]
   [InlineData("К-1")]
   [InlineData("Колонна К2 (A500)")]
   [InlineData("Фундамент")]
   public void Label_NamesAreNotAnnotations(string text) => Assert.False(CadStirrupLabel.Parse(text).IsAnnotation);

   [Fact]
   public void LayerName_GuessedAsStirrups_WithRebarClass()
   {
      Assert.Equal(CadLayerRole.Stirrups, CadLayerRules.Guess(new CadLayerInfo("Хомуты_A240")));
      Assert.Equal(CadLayerRole.Stirrups, CadLayerRules.Guess(new CadLayerInfo("Арм_поперечная")));
      Assert.Equal(CadLayerRole.Stirrups, CadLayerRules.Guess(new CadLayerInfo("шпильки")));
      Assert.Equal("A240", CadLayerRules.GuessClass("Хомуты_А240", CadLayerRole.Stirrups));
   }

   [Fact]
   public void Offset_RectangleInwardAndOutward()
   {
      List<CadPoint> sq = [new(0, 0), new(100, 0), new(100, 50), new(0, 50)];
      var inner = CadGeometry.Offset(sq, -4);
      Assert.Equal(new CadBounds(4, 4, 96, 46), CadBounds.Of(inner));
      var cw = Enumerable.Reverse(sq).ToList();
      Assert.Equal(new CadBounds(-4, -4, 104, 54), CadBounds.Of(CadGeometry.Offset(cw, 4)));
   }

   [Fact]
   public void WidePolyline_RoundedCorners_DiameterFromWidth_SpacingFromLabel_NotAName()
   {
      var b = Column();
      b.AddPolyline(St, RoundedRect(36, 36, 264, 364, 20), closed: true, width: 8);
      b.AddText(Lab, 100, 200, 60, "Ø8 шаг 150"); // внутри и крупнее имени — но это подпись хомутов
      var c = One(b);

      Assert.Equal("К-1", c.Name);
      Assert.Empty(c.Warnings);
      var s = Assert.Single(c.Stirrups);
      Assert.Equal(CadStirrupShape.Wide, s.Shape);
      Assert.True(s.Closed);
      Assert.Equal(0.008, s.DiameterM, 12);
      Assert.Equal(0.15, s.SpacingM!.Value, 12);
      Assert.Null(s.Class);
      Assert.Equal(St, s.Layer);
      AssertAxisBox(s);
   }

   [Fact]
   public void ThinAxis_FromStitchedLines_DiameterSpacingClassFromLabelOnStirrupLayer()
   {
      var b = Column();
      b.AddLine(St, 36, 36, 264, 36);
      b.AddLine(St, 264, 364, 264, 36);
      b.AddLine(St, 264, 364, 36, 364);
      b.AddLine(St, 36, 36, 36, 364);
      b.AddText(St, 320, 200, 20, "%%c10А240 с шагом 200");
      var s = Assert.Single(One(b).Stirrups);

      Assert.Equal(CadStirrupShape.Thin, s.Shape);
      Assert.Equal(0.010, s.DiameterM, 12);
      Assert.Equal(0.2, s.SpacingM!.Value, 12);
      Assert.Equal("A240", s.Class);
      AssertAxisBox(s);
   }

   [Fact]
   public void ThinAxis_NoLabel_LayerDefaults_OrSkippedWithWarning()
   {
      var b = Column();
      b.AddPolyline(St, Rect(36, 36, 264, 364), closed: true);
      var s = Assert.Single(One(b, o => o.StirrupDefaults[St] = new CadStirrupDefaults(8, 100)).Stirrups);
      Assert.Equal(0.008, s.DiameterM, 12);
      Assert.Equal(0.1, s.SpacingM!.Value, 12);

      b = Column();
      b.AddPolyline(St, Rect(36, 36, 264, 364), closed: true);
      var c = One(b);
      Assert.Empty(c.Stirrups);
      Assert.Contains(c.Warnings, w => w.StartsWith("хомутов без диаметра"));

      // Диаметр есть, шага нет — элемент остаётся, предупреждение.
      b = Column();
      b.AddPolyline(St, Rect(36, 36, 264, 364), closed: true, width: 8);
      c = One(b);
      Assert.Null(Assert.Single(c.Stirrups).SpacingM);
      Assert.Contains("хомутов без шага: 1", c.Warnings);
   }

   [Fact]
   public void DoubleContour_RoundedCorners_DiameterIsGap_AxisInBetween()
   {
      // Ось 36…264 × 36…364 с радиусом 24, d = 8: грани — ±4 мм от оси.
      var b = Column();
      b.AddPolyline(St, RoundedRect(32, 32, 268, 368, 28), closed: true);
      b.AddPolyline(St, RoundedRect(40, 40, 260, 360, 20), closed: true);
      b.AddText(Lab, 320, 200, 20, "шаг 150");
      var s = Assert.Single(One(b).Stirrups);

      Assert.Equal(CadStirrupShape.DoubleContour, s.Shape);
      Assert.Equal(0.008, s.DiameterM, 12);
      Assert.Equal(0.15, s.SpacingM!.Value, 12);
      AssertAxisBox(s, tolMm: 0.05);
      double area = (228 * 328 - (4 - Math.PI) * 24 * 24) * 1e-6;
      Assert.Equal(area, Math.Abs(CadGeometry.SignedArea(s.Axis)), area * 2e-3);
   }

   [Fact]
   public void Hooked135_EndsBackAtCorner_ClosedByCorner_TailsDropped()
   {
      // Крючки 135° у левого нижнего угла: от конца крючка в угол, обход, снова в угол и крючок внутрь.
      double h = 60 * Math.Sqrt(0.5);
      var b = Column();
      b.AddPolyline(St, [new(36 + h, 36 + h), new(36, 36), new(264, 36), new(264, 364), new(36, 364), new(36, 36),
                         new(36 + h + 6, 36 + h - 6)], closed: false, width: 8);
      b.AddText(Lab, 320, 200, 20, "шаг 150");
      var s = Assert.Single(One(b).Stirrups);

      Assert.Equal(CadStirrupShape.Hooked, s.Shape);
      Assert.Equal(0.008, s.DiameterM, 12);
      Assert.Equal(4, s.Axis.Count);
      AssertAxisBox(s);
      Assert.Equal(0.228 * 0.328, Math.Abs(CadGeometry.SignedArea(s.Axis)), 1e-9);
   }

   [Fact]
   public void Hooked_CrossingEnds_ClosedAtIntersection_DiameterFromLabel()
   {
      var b = Column();
      b.AddPolyline(St, [new(16, 36), new(264, 36), new(264, 364), new(36, 364), new(36, 16)], closed: false);
      b.AddText(Lab, 320, 200, 20, "Ø8 шаг 100");
      var s = Assert.Single(One(b).Stirrups);

      Assert.Equal(CadStirrupShape.Hooked, s.Shape);
      Assert.Equal(0.008, s.DiameterM, 12);
      Assert.Equal(0.1, s.SpacingM!.Value, 12);
      AssertAxisBox(s);
      Assert.Equal(0.228 * 0.328, Math.Abs(CadGeometry.SignedArea(s.Axis)), 1e-9);
   }

   [Fact]
   public void Tie_Hooks135And90_Dropped()
   {
      double h = 60 * Math.Sqrt(0.5);
      var b = Column();
      b.AddPolyline(St, RoundedRect(36, 36, 264, 364, 20), closed: true, width: 8);
      // Шпилька Ø8 поперёк сечения: слева крючок 135°, справа 90° (короче 10 d = 80 мм).
      b.AddPolyline(St, [new(36 + h, 200 - h), new(36, 200), new(264, 200), new(264, 140)], closed: false, width: 8);
      b.AddText(Lab, 320, 200, 20, "шаг 150");
      var c = One(b);

      var tie = Assert.Single(c.Stirrups, x => !x.Closed);
      Assert.Equal(CadStirrupShape.Tie, tie.Shape);
      Assert.Equal(0.008, tie.DiameterM, 12);
      Assert.Equal([new CadPoint(-0.114, 0), new CadPoint(0.114, 0)],
         tie.Axis.Select(p => new CadPoint(Math.Round(p.X, 9), Math.Round(p.Y, 9))));
      Assert.Single(c.Stirrups, x => x.Closed);
   }

   [Fact]
   public void Tie_WithArcBends_TrimmedAtStraightBody()
   {
      // Крючок 135° со скруглённым загибом: тело от (60; 200) вправо, загиб r = 16 против часовой с центром над
      // телом, хвост 40 мм уходит внутрь сечения под 45° к телу.
      double r = 16, cx = 60, cy = 200 + r, a = 0.75 * Math.PI;
      var bend = new CadPoint(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
      var tip = new CadPoint(bend.X + 40 * Math.Sin(a), bend.Y - 40 * Math.Cos(a)); // против направления обхода
      var b = Column();
      b.AddPolyline(St, [new(tip.X, tip.Y), new(bend.X, bend.Y, Math.Tan(0.75 * Math.PI / 4)), new(cx, 200), new(264, 200)],
         closed: false, width: 8);
      b.AddText(Lab, 320, 200, 20, "шаг 150");
      var tie = Assert.Single(One(b).Stirrups);
      Assert.Equal(CadStirrupShape.Tie, tie.Shape);
      Assert.Equal(-0.090, tie.Axis[0].X, 9);
      Assert.Equal(0, tie.Axis[0].Y, 9);
      Assert.Equal(2, tie.Axis.Count);
   }

   [Fact]
   public void Circles_ThinAndDoubleContour_RoundColumn()
   {
      static CadSnapshotBuilder Round()
      {
         var b = new CadSnapshotBuilder(CadArcDiscretization.ByDeviation(0.1), Tol);
         b.AddCircle(Conc, 0, 0, 200);
         for (int i = 0; i < 6; i++)
            b.AddCircle(Reb, 150 * Math.Cos(i * Math.PI / 3), 150 * Math.Sin(i * Math.PI / 3), 10);
         return b;
      }

      var b1 = Round();
      b1.AddCircle(St, 0, 0, 172);
      b1.AddText(Lab, 250, 0, 20, "Ø10 шаг 150");
      var thin = Assert.Single(One(b1).Stirrups);
      Assert.Equal(CadStirrupShape.Thin, thin.Shape);
      Assert.Equal(0.010, thin.DiameterM, 12);
      Assert.All(thin.Axis, p => Assert.Equal(0.172, Math.Sqrt(p.X * p.X + p.Y * p.Y), 9));

      var b2 = Round();
      b2.AddCircle(St, 0, 0, 177);
      b2.AddCircle(St, 0, 0, 167);
      b2.AddText(Lab, 250, 0, 20, "шаг 150");
      var dbl = Assert.Single(One(b2).Stirrups);
      Assert.Equal(CadStirrupShape.DoubleContour, dbl.Shape);
      Assert.Equal(0.010, dbl.DiameterM, 12);
      Assert.All(dbl.Axis, p => Assert.Equal(0.172, Math.Sqrt(p.X * p.X + p.Y * p.Y), 0.0002));
   }

   [Fact]
   public void TwoLabels_EachElementTakesNearest()
   {
      var b = new CadSnapshotBuilder(CadArcDiscretization.ByDeviation(0.1), Tol);
      b.AddPolyline(Conc, Rect(0, 0, 600, 300), closed: true);
      foreach (var x in new[] { 50.0, 290, 310, 550 })
         foreach (var y in new[] { 50.0, 250 })
            b.AddCircle(Reb, x, y, 10);
      b.AddPolyline(St, Rect(36, 36, 304, 264), closed: true);
      b.AddPolyline(St, Rect(296, 36, 564, 264), closed: true);
      b.AddText(Lab, 40, 320, 20, "Ø8 шаг 100");
      b.AddText(Lab, 400, 320, 20, "Ø10 шаг 200");
      var c = One(b);

      Assert.Equal(2, c.Stirrups.Count);
      var left = c.Stirrups.MinBy(s => Bounds(s).MinX)!;
      var right = c.Stirrups.MaxBy(s => Bounds(s).MinX)!;
      Assert.Equal((0.008, 0.1), (left.DiameterM, left.SpacingM!.Value));
      Assert.Equal((0.010, 0.2), (right.DiameterM, right.SpacingM!.Value));
   }

   [Fact]
   public void Warnings_OutsideConcrete_EmptyHoop()
   {
      var b = Column();
      b.AddPolyline(St, Rect(2, 2, 298, 398), closed: true, width: 8);   // ось в 2 мм от грани — d/2 = 4 мм наружу
      b.AddPolyline(St, Rect(120, 150, 180, 250), closed: true, width: 8); // внутри без стержней
      b.AddText(Lab, 320, 200, 20, "шаг 150");
      var c = One(b);

      Assert.Equal(2, c.Stirrups.Count);
      Assert.Contains("хомутов, выходящих за бетон: 1", c.Warnings);
      Assert.Contains("хомутов, не охватывающих продольных стержней: 1", c.Warnings);
   }

   [Fact]
   public void Frames_StirrupsAndLabelsByFrame_IdenticalMerged_DifferentSpacingNot()
   {
      var b = new CadSnapshotBuilder(CadArcDiscretization.ByDeviation(0.1), Tol);
      void Frame(double x0, string name, string note)
      {
         b.AddPolyline("Рамки", Rect(x0, -200, x0 + 800, 700), closed: true);
         b.AddPolyline(Conc, Rect(x0, 0, x0 + 300, 400), closed: true);
         foreach (var (x, y) in new[] { (50.0, 50.0), (250.0, 50.0), (50.0, 350.0), (250.0, 350.0) })
            b.AddCircle(Reb, x0 + x, y, 10);
         b.AddPolyline(St, Rect(x0 + 36, 36, x0 + 264, 364), closed: true, width: 8);
         b.AddText(Lab, x0 + 20, -100, 40, name);
         b.AddText(Lab, x0 + 400, 200, 20, note);
      }
      Frame(0, "К-1", "шаг 150");
      Frame(1000, "К-2", "шаг 150");
      Frame(2000, "К-3", "шаг 200");
      var s = b.Build();
      var r = CadSectionRecognizer.Recognize(s, new CadImportOptions().GuessRoles(s));

      Assert.Empty(r.Warnings);
      Assert.Equal(2, r.Sections.Count);
      Assert.Equal(["К-1", "К-2"], r.Sections[0].Names);
      Assert.Equal(0.15, Assert.Single(r.Sections[0].Stirrups).SpacingM!.Value, 12);
      Assert.Equal(["К-3"], r.Sections[1].Names);
      Assert.Equal(0.2, Assert.Single(r.Sections[1].Stirrups).SpacingM!.Value, 12);
   }
}
