using CScore.Import.Cad;
using Xunit;

namespace CScore.Tests.Import.Cad;

/// <summary>Сборка снимка: дуги полилиний, сшивка LINE/ARC, кольца (единицы — мм).</summary>
public class CadSnapshotBuilderTests
{
   const double Tol = 0.01;
   static readonly CadArcDiscretization Chord = CadArcDiscretization.ByChord(10);

   /// <summary>Площадь квадрата a×a со скруглёнными углами радиуса r.</summary>
   static double RoundedSquare(double a, double r) => a * a - (4 - Math.PI) * r * r;

   [Fact]
   public void PolylineFillets_ByBulge_AreaWithinTenthOfPercent()
   {
      var b = new CadSnapshotBuilder(Chord, Tol);
      double t = Math.Tan(Math.PI / 8); // скругление 90°
      b.AddPolyline("Сталь", [
         new(10, 0), new(90, 0, t), new(100, 10), new(100, 90, t), new(90, 100), new(10, 100, t),
         new(0, 90), new(0, 10, t)], closed: true);
      var p = Assert.Single(b.Build().Polygons);
      Assert.Equal(RoundedSquare(100, 10), Math.Abs(p.Area), RoundedSquare(100, 10) * 1e-3);
      // Скругление r = 10 при хорде 10 мм делится по углу: 9 отрезков на 90°.
      Assert.Equal(4 * 2 + 4 * 8, p.Points.Count);
   }

   [Fact]
   public void ExplodedProfile_LinesAndArcs_StitchedIntoContour()
   {
      var b = new CadSnapshotBuilder(Chord, Tol);
      // Тот же квадрат, расчленённый: отрезки в произвольном порядке и направлении, дуги — против часовой.
      b.AddLine("Сталь", 90, 100, 10, 100);
      b.AddArc("Сталь", 90, 10, 10, -Math.PI / 2, 0);
      b.AddLine("Сталь", 0, 10, 0, 90);
      b.AddArc("Сталь", 10, 90, 10, Math.PI / 2, Math.PI);
      b.AddLine("Сталь", 100, 10, 100, 90);
      b.AddArc("Сталь", 90, 90, 10, 0, Math.PI / 2);
      b.AddLine("Сталь", 10, 0, 90, 0);
      b.AddArc("Сталь", 10, 10, 10, Math.PI, 1.5 * Math.PI);
      var s = b.Build();
      var p = Assert.Single(s.Polygons);
      Assert.Empty(s.Warnings);
      Assert.Equal(RoundedSquare(100, 10), Math.Abs(p.Area), RoundedSquare(100, 10) * 1e-3);
   }

   [Fact]
   public void ArcChamfer_InPolylineCorner()
   {
      // Прямоугольник 300×200 с дуговой фаской (вогнутой, bulge < 0) радиусом 50 в правом верхнем углу.
      var b = new CadSnapshotBuilder(Chord, Tol);
      double t = Math.Tan(Math.PI / 8);
      b.AddPolyline("Бетон", [new(0, 0), new(300, 0), new(300, 150, -t), new(250, 200), new(0, 200)], closed: true);
      var p = Assert.Single(b.Build().Polygons);
      double exact = 300 * 200 - Math.PI * 50 * 50 / 4; // вогнутая четверть круга вырезана
      Assert.Equal(exact, Math.Abs(p.Area), exact * 1e-3);
   }

   [Fact]
   public void Ring_TwoConcentricCirclesOneLayer_HollowSection()
   {
      var b = new CadSnapshotBuilder(Chord, Tol);
      b.AddCircle("Бетон", 0, 0, 300);
      b.AddCircle("Бетон", 0, 0, 200);
      var s = b.Build();
      var o = new CadImportOptions().GuessRoles(s);
      var c = Assert.Single(CadSectionRecognizer.Recognize(s, o).Sections);
      var reg = Assert.Single(c.Regions);
      Assert.Single(reg.Holes);
      Assert.Equal(Math.PI * (0.09 - 0.04), reg.Area, 3);
   }

   [Fact]
   public void OpenChain_Warned_ClosedPolylineWithoutFlag_IsContour()
   {
      var b = new CadSnapshotBuilder(Chord, Tol);
      b.AddLine("Бетон", 0, 0, 100, 0);
      b.AddLine("Бетон", 100, 0, 100, 100);
      // Незамкнутая по флагу, но с совпадающими концами — контур.
      b.AddPolyline("Бетон", [new(200, 0), new(300, 0), new(300, 100), new(200, 0.001)], closed: false);
      var s = b.Build();
      Assert.Single(s.Polygons);
      Assert.Contains("незамкнутых линий на слое «Бетон»: 1", s.Warnings);
   }

   [Fact]
   public void WidePolyline_ClosedIsContourAndPath_OpenIsPathOnly()
   {
      var b = new CadSnapshotBuilder(Chord, Tol);
      // Хомут полилинией с шириной 8 по оси 200×300 и шпилька с крючками (незамкнутая, ширина 6).
      b.AddPolyline("Хомуты", [new(0, 0), new(200, 0), new(200, 300), new(0, 300)], closed: true, width: 8);
      b.AddPolyline("Хомуты", [new(30, 60), new(0, 30), new(0, 150), new(200, 150), new(200, 120)], closed: false, width: 6);
      var s = b.Build();

      Assert.Single(s.Polygons);
      Assert.Equal(2, s.Paths.Count);
      var hoop = Assert.Single(s.Paths, l => l.Closed);
      Assert.Equal(8, hoop.Width);
      Assert.Equal(4, hoop.Points.Count);
      var tie = Assert.Single(s.Paths, l => !l.Closed);
      Assert.Equal(6, tie.Width);
      Assert.Equal(5, tie.Points.Count);
      Assert.Equal(new CadBounds(0, 30, 200, 150), tie.Bounds);
   }

   [Fact]
   public void StitchedChains_KeptAsPaths_WidthOnlyIfCommon()
   {
      var b = new CadSnapshotBuilder(Chord, Tol);
      // Замкнутая цепь из отрезков — контур и замкнутая линия без ширины.
      b.AddLine("Хомуты", 0, 0, 100, 0);
      b.AddLine("Хомуты", 100, 100, 100, 0);
      b.AddLine("Хомуты", 100, 100, 0, 100);
      b.AddLine("Хомуты", 0, 0, 0, 100);
      // Незамкнутая цепь из двух полилиний одной ширины — одна линия с этой шириной.
      b.AddPolyline("Хомуты", [new(300, 0), new(400, 0)], closed: false, width: 8);
      b.AddPolyline("Хомуты", [new(500, 50), new(400, 0)], closed: false, width: 8);
      // Из полилинии с шириной и отрезка — ширина 0.
      b.AddPolyline("Шпильки", [new(0, 500), new(100, 500)], closed: false, width: 8);
      b.AddLine("Шпильки", 100, 500, 100, 600);
      var s = b.Build();

      Assert.Single(s.Polygons);
      var closed = Assert.Single(s.Paths, l => l.Closed);
      Assert.Equal(0, closed.Width);
      Assert.Equal(4, closed.Points.Count);
      var same = Assert.Single(s.Paths, l => !l.Closed && l.Layer == "Хомуты");
      Assert.Equal(8, same.Width);
      Assert.Equal(3, same.Points.Count);
      Assert.Equal(new CadPoint(400, 0), same.Points[1]);
      Assert.Equal(0, Assert.Single(s.Paths, l => l.Layer == "Шпильки").Width);
   }

   [Fact]
   public void Paths_InWindowExtentsAndLayerCounts()
   {
      var b = new CadSnapshotBuilder(Chord, Tol);
      b.AddPolyline("Хомуты", [new(0, 0), new(200, 0), new(200, 300), new(0, 300)], closed: true, width: 8);
      b.AddPolyline("Хомуты", [new(0, 150), new(200, 150)], closed: false);
      b.AddPolyline("Хомуты", [new(1000, 0), new(1500, 0)], closed: false);
      b.AddText("Хомуты", 50, 350, 20, "Ø8 шаг 150");
      var s = b.Build();

      Assert.Equal(new CadBounds(0, 0, 1500, 350), s.Extents());
      Assert.Equal(2, s.Within(new CadBounds(-10, -10, 400, 400)).Paths.Count);
      var layer = Assert.Single(s.Layers());
      Assert.Equal(1, layer.Polygons);
      Assert.Equal(2, layer.Lines);
      // Слой с линиями и текстами — не подписи.
      Assert.NotEqual(CadLayerRole.Label, CadLayerRules.Guess(new CadLayerInfo("Слой1") { Lines = 2, Texts = 1 }));
   }

   [Fact]
   public void FixedSegmentsSetting_AppliedToBulgeArcsAndCircles()
   {
      var d = CadArcDiscretization.FromMeters(CadArcMode.FixedSegments, 0.0001, 0.01, 6, 0.001);
      var b = new CadSnapshotBuilder(d, Tol);
      double t = Math.Tan(Math.PI / 8);
      b.AddPolyline("Сталь", [new(0, 0), new(90, 0, t), new(100, 10), new(0, 10)], closed: true);
      Assert.Equal(4 + 5, Assert.Single(b.Build().Polygons).Points.Count);
      Assert.Equal(6, CadGeometry.CirclePoints(0, 0, 500, d).Count);

      var byChord = CadArcDiscretization.FromMeters(CadArcMode.Chord, 0.0001, 0.01, 6, 0.001);
      Assert.Equal(10, byChord.Value, 12); // 10 мм хорды в единицах чертежа (мм)
   }

   [Theory]
   [InlineData(10.0, Math.PI / 2, 10.0, 9)]     // малое скругление — по углу
   [InlineData(1000.0, Math.PI / 2, 10.0, 158)] // большой радиус — по хорде
   public void ArcSegments_AngleOrChord(double r, double sweep, double chord, int expected) =>
      Assert.Equal(expected, CadGeometry.ArcSegments(r, sweep, chord));

   [Theory]
   [InlineData(10.0, Math.PI / 2, 9)]     // скругление — ограничение 10°
   [InlineData(200.0, 2 * Math.PI, 100)]  // по стрелке 0,1 мм
   [InlineData(600.0, 2 * Math.PI, 173)]
   public void ArcSegments_ByDeviation(double r, double sweep, int expected) =>
      Assert.Equal(expected, CadArcDiscretization.ByDeviation(0.1).Segments(r, sweep));

   [Fact]
   public void DeviationMode_ChordDeviationWithinTolerance()
   {
      foreach (double r in new[] { 5.0, 50, 300, 1500 })
      {
         var pts = CadGeometry.CirclePoints(0, 0, r, CadArcDiscretization.ByDeviation(0.1));
         // Стрелка правильного n-угольника — r·(1 − cos(π/n)).
         Assert.InRange(r * (1 - Math.Cos(Math.PI / pts.Count)), 0, 0.1 + 1e-12);
      }
   }
}
