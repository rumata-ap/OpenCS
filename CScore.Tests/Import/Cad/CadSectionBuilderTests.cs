using CScore.Import.Cad;
using Xunit;

namespace CScore.Tests.Import.Cad;

/// <summary>Сборка CrossSection из распознанного сечения.</summary>
public class CadSectionBuilderTests
{
   static MaterialChars ConcreteChars(CalcType ct) => new(ct)
   {
      Type = MatType.Concrete, E = 30_000_000.0, Fc = -14_500.0, Ft = 1_050.0,
      Ec0 = -0.002, Ec1 = -0.6 * 14_500.0 / 30_000_000.0, Ec2 = -0.0035, Ec1Red = -0.0015,
      Et0 = 0.0001, Et1 = 0.6 * 1_050.0 / 30_000_000.0, Et2 = 0.00015, Et1Red = 0.00008,
   };

   static MaterialChars SteelChars(CalcType ct) => new(ct)
   {
      Type = MatType.ReSteelF, E = 200_000_000.0, Fc = -435_000.0, Ft = 435_000.0, Ec2 = -0.025, Et2 = 0.025,
   };

   static Material Make(Material m, Func<CalcType, MaterialChars> chars)
   {
      m.C = chars(CalcType.C);
      m.CL = chars(CalcType.CL);
      m.N = chars(CalcType.N);
      m.NL = chars(CalcType.NL);
      return m;
   }

   static readonly Material Concrete = Make(new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 }, ConcreteChars);
   static readonly Material Rebar = Make(new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 }, SteelChars);

   static Material? MaterialOf(string layer, CadLayerRole role) => role switch
   {
      CadLayerRole.Concrete => Concrete,
      CadLayerRole.Steel or CadLayerRole.Rebar => Rebar,
      _ => null,
   };

   static CadSectionCandidate Recognize(CadSnapshot s) =>
      Assert.Single(CadSectionRecognizer.Recognize(s, new CadImportOptions().GuessRoles(s)).Sections);

   static CadPolygon Rect(string layer, double x, double y, double w, double h) =>
      new(layer, [new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)]);

   [Fact]
   public void Column_ConcreteAndRebarGroupWithHost_CentroidAtZero()
   {
      var s = new CadSnapshot();
      s.Polygons.Add(Rect("Бетон", 1000, 1000, 300, 500));
      s.Polygons.Add(Rect("Отверстие", 1050, 1300, 60, 120)); // несимметричное отверстие
      foreach (var (x, y) in new[] { (1050.0, 1050.0), (1250.0, 1050.0), (1250.0, 1450.0) })
         s.Circles.Add(new CadCircle("Арм", x, y, 10));
      s.Texts.Add(new CadText("Подписи", 1000, 900, 50, "Кол-1"));

      var (sec, reason) = CadSectionBuilder.Build(Recognize(s), MaterialOf);
      Assert.Null(reason);
      Assert.NotNull(sec);
      Assert.Equal("Кол-1", sec!.Tag);
      Assert.Equal(2, sec.Areas.Count);
      var conc = sec.Areas[0];
      var reb = sec.Areas[1];
      Assert.Equal(AreaCategory.Region, conc.Category);
      Assert.Single(conc.Holes);
      Assert.Same(Concrete, conc.Material);
      Assert.Equal(AreaCategory.RebarGroup, reb.Category);
      Assert.Same(conc, reb.HostArea);
      Assert.Equal(3, reb.Fibers.Count);
      Assert.All(reb.Fibers, f => Assert.Equal(Math.PI * 0.0001, f.Area, 12));
      Assert.NotEmpty(reb.Diagramms);

      // Центр тяжести бетона (за вычетом отверстия) — в начале координат.
      var g = new GeoProps(conc.Hull!);
      double a = g.A, sx = g.Sx, sy = g.Sy;
      var gh = new GeoProps(conc.Holes[0]);
      // GeoProps: площадь по модулю, статические моменты со знаком обхода — у отверстия (по часовой) уже минус.
      a -= gh.A; sx += gh.Sx; sy += gh.Sy;
      Assert.Equal(0, sx / a, 9);
      Assert.Equal(0, sy / a, 9);
   }

   [Fact]
   public void MissingMaterial_Reason()
   {
      var s = new CadSnapshot();
      s.Polygons.Add(Rect("Бетон", 0, 0, 300, 300));
      var (sec, reason) = CadSectionBuilder.Build(Recognize(s), (_, _) => null);
      Assert.Null(sec);
      Assert.Contains("Бетон", reason);
   }
}
