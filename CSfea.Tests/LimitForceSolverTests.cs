using CScore;

namespace CSfea.Tests;

/// <summary>
/// Проверки поиска предельного коэффициента нагружения.
/// </summary>
public static class LimitForceSolverTests
{
   public static void RunAll()
   {
      TestHarness.Section("LimitForceSolver: базовые проверки");
      Rectangle_AxialCompression_ReturnsPositiveFactor();
      Rectangle_BendingOnly_ReturnsFinitePositiveFactor();
   }

   static void Rectangle_AxialCompression_ReturnsPositiveFactor()
   {
      var section = BuildConcreteRectangle(width: 0.5, height: 0.5, nx: 18, ny: 18);
      var solver = LimitForceSolver.ForCrossSection(section, CalcType.C);
      LimitForceResult res = solver.AllFactor(-1.0, 0.0, 0.0);

      bool ok = double.IsFinite(res.Factor) && res.Factor > 1.0;
      TestHarness.Check(
         "LimitForce_Rectangle_AxialCompression_FactorGt1",
         ok,
         $"k={res.Factor:F4}, conv={res.Converged}, gov={res.Governing}");
   }

   static void Rectangle_BendingOnly_ReturnsFinitePositiveFactor()
   {
      var section = BuildConcreteRectangle(width: 0.4, height: 0.6, nx: 22, ny: 22);
      var solver = LimitForceSolver.ForCrossSection(section, CalcType.C);
      LimitForceResult res = solver.AllFactor(0.0, 100.0, 0.0);

      bool ok = double.IsFinite(res.Factor) && res.Factor > 0.0;
      TestHarness.Check(
         "LimitForce_Rectangle_MxOnly_FinitePositiveFactor",
         ok,
         $"k={res.Factor:F4}, conv={res.Converged}, iter={res.Iterations}");
   }

   static CrossSection BuildConcreteRectangle(double width, double height, int nx, int ny)
   {
      var concrete = BuildConcreteMaterial();
      var hull = new Contour(
         new[] { 0.0, width, width, 0.0, 0.0 },
         new[] { 0.0, 0.0, height, height, 0.0 },
         "rect")
      { Type = ContourType.Hull };

      var area = new MaterialArea
      {
         Tag = "conc-rect",
         Category = AreaCategory.Region,
         Material = concrete,
         MaterialId = concrete.Id,
         DiagrammType = DiagrammType.L2,
         Contours = [hull],
         NX = nx,
         NY = ny
      };
      area.Hull = hull;
      area.ResolveAndBuildDiagramms();
      area.SliceXY(nx, ny);

      return new CrossSection
      {
         Tag = "limit-force-rect",
         Areas = [area]
      };
   }

   static Material BuildConcreteMaterial()
   {
      MaterialChars Ch(CalcType ct) => new(ct)
      {
         E = 30_000,
         Fc = -20.0,
         Ft = 1.6,
         Ec2 = -0.0035,
         Ec1Red = -0.0015,
         Et1Red = 0.0001,
         Et2 = 0.00015,
         Type = MatType.Concrete
      };

      var m = new Material
      {
         Id = 10_001,
         Tag = "test-concrete",
         Type = MatType.Concrete,
         E = 30_000
      };
      m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
      return m;
   }
}
