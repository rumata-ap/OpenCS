using CScore.Import.Cad;
using Xunit;

namespace CScore.Tests.Import.Cad;

/// <summary>Сборка снимка: дуги полилиний, сшивка LINE/ARC, кольца (единицы — мм).</summary>
public class CadSnapshotBuilderTests
{
   const double Chord = 10, Tol = 0.01;

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

   [Theory]
   [InlineData(10.0, Math.PI / 2, 10.0, 9)]     // малое скругление — по углу
   [InlineData(1000.0, Math.PI / 2, 10.0, 158)] // большой радиус — по хорде
   public void ArcSegments_AngleOrChord(double r, double sweep, double chord, int expected) =>
      Assert.Equal(expected, CadGeometry.ArcSegments(r, sweep, chord));
}
