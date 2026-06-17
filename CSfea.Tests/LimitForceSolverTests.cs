using CScore;

namespace CSfea.Tests;

/// <summary>
/// Проверки поиска предельного коэффициента нагружения.
///
/// Быстрый решатель (<see cref="LimitForceSolverFast"/>) проверяется не на паритет
/// с бисекцией, а по ФИЗИЧЕСКОЙ корректности предельного состояния: на найденной
/// плоскости деформаций сечение должно быть в равновесии с k·(N,Mx,My), оставаться
/// допустимым (бетон ≥ εcu, арматура ≤ εsu) и иметь активную предельную связь
/// (угол бетона на εcu ЛИБО стержень на εsu). Бисекция здесь не эталон: её
/// StrainSolver расходится у предела и занижает предельный коэффициент.
/// </summary>
public static class LimitForceSolverTests
{
   public static void RunAll()
   {
      TestHarness.Section("LimitForceSolver: базовые проверки");
      Rectangle_AxialCompression_ReturnsPositiveFactor();
      Rectangle_BendingOnly_ReturnsFinitePositiveFactor();

      TestHarness.Section("LimitForceSolverFast: корректность предельного состояния");
      Fast_AxialCompression_LimitStateValid();
      Fast_BendingOnly_LimitStateValid();
      Fast_BiplanarCompression_LimitStateValid();
      Fast_HeavyBiplanar_LimitStateValid();
   }

   static void Rectangle_AxialCompression_ReturnsPositiveFactor()
   {
      var section = BuildReinforcedRectangle(width: 0.5, height: 0.5, nx: 18, ny: 18);
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
      var section = BuildReinforcedRectangle(width: 0.4, height: 0.6, nx: 22, ny: 22);
      var solver = LimitForceSolver.ForCrossSection(section, CalcType.C);
      LimitForceResult res = solver.AllFactor(0.0, 100.0, 0.0);

      bool ok = double.IsFinite(res.Factor) && res.Factor > 0.0;
      TestHarness.Check(
         "LimitForce_Rectangle_MxOnly_FinitePositiveFactor",
         ok,
         $"k={res.Factor:F4}, conv={res.Converged}, iter={res.Iterations}");
   }

   static void Fast_AxialCompression_LimitStateValid()
   {
      var section = BuildReinforcedRectangle(width: 0.5, height: 0.5, nx: 18, ny: 18);
      CheckFastLimitState(section, -1.0, 0.0, 0.0, "Fast_AxialCompression_LimitStateValid");
   }

   static void Fast_BendingOnly_LimitStateValid()
   {
      var section = BuildReinforcedRectangle(width: 0.4, height: 0.6, nx: 22, ny: 22);
      CheckFastLimitState(section, 0.0, 100.0, 0.0, "Fast_BendingOnly_LimitStateValid");
   }

   static void Fast_BiplanarCompression_LimitStateValid()
   {
      var section = BuildReinforcedRectangle(width: 0.3, height: 0.6, nx: 24, ny: 24);
      CheckFastLimitState(section, -200.0, 80.0, 40.0, "Fast_BiplanarCompression_LimitStateValid");
   }

   static void Fast_HeavyBiplanar_LimitStateValid()
   {
      var section = BuildReinforcedRectangle(width: 0.3, height: 0.6, nx: 24, ny: 24);
      CheckFastLimitState(section, -500.0, 120.0, 60.0, "Fast_HeavyBiplanar_LimitStateValid");
   }

   /// <summary>
   /// Проверяет, что быстрый решатель нашёл физически корректное предельное состояние:
   /// сошёлся, k&gt;0, равновесие F(sp)≈k·load, бетон ≥ εcu и арматура ≤ εsu (допустимость),
   /// и активна хотя бы одна предельная связь (угол на εcu или стержень на εsu).
   /// </summary>
   static void CheckFastLimitState(CrossSection section, double n, double mx, double my, string name)
   {
      var fast = new LimitForceSolverFast(section, CalcType.C);
      var r = fast.AllFactor(n, mx, my);

      if (!r.Converged || r.StrainPlane is not Kurvature sp || !(r.Factor > 0))
      {
         TestHarness.Check(name, false, $"не сошёлся: conv={r.Converged}, k={r.Factor:F4}");
         return;
      }

      var adapter = new CrossSectionLimitAdapter(section, CalcType.C);
      double epsCu = adapter.EpsCu;
      var contour = adapter.ContourVertices.ToList();
      var rebar = adapter.RebarPoints.ToList();

      static double Eps(Kurvature k, double x, double y) => k.e0 + k.ky * y + k.kz * x;

      double concMin = contour.Min(p => Eps(sp, p.X, p.Y));
      double rebarMax = rebar.Count > 0 ? rebar.Max(p => Eps(sp, p.X, p.Y)) : double.NegativeInfinity;
      double epsSu = rebar.Count > 0 ? rebar.Max(p => p.EpsSu) : double.PositiveInfinity;

      // Равновесие: F(sp) ≈ k·(N,Mx,My).
      var f = section.Compute(sp, CalcType.C, computeStiffness: false);
      double nT = r.Factor * n, mxT = r.Factor * mx, myT = r.Factor * my;
      double resid = Math.Sqrt(
         (f.N - nT) * (f.N - nT) + (f.Mx - mxT) * (f.Mx - mxT) + (f.My - myT) * (f.My - myT));
      double mag = Math.Sqrt(nT * nT + mxT * mxT + myT * myT);
      bool equilibrium = resid <= Math.Max(1e-2 * mag, 1.0);

      // Допустимость: бетон не переуплотнён, арматура не перетянута (с допуском 2%).
      double concTol = Math.Abs(epsCu) * 0.02;
      double suTol = double.IsFinite(epsSu) ? Math.Abs(epsSu) * 0.02 : 0;
      bool feasible = concMin >= epsCu - concTol
         && (rebar.Count == 0 || rebarMax <= epsSu + suTol);

      // Активная предельная связь: угол бетона на εcu ЛИБО стержень на εsu.
      bool concActive = Math.Abs(concMin - epsCu) <= concTol;
      bool rebarActive = rebar.Count > 0 && Math.Abs(rebarMax - epsSu) <= suTol;
      bool limitActive = concActive || rebarActive;

      bool ok = equilibrium && feasible && limitActive;
      TestHarness.Check(
         name,
         ok,
         $"k={r.Factor:F4} gov={r.Governing} concMin={concMin:E3}/εcu={epsCu:E3} "
         + $"rebarMax={rebarMax:E3}/εsu={epsSu:E3} resid={resid:E3} "
         + $"(equil={equilibrium} feas={feasible} active={limitActive})");
   }

   static CrossSection BuildReinforcedRectangle(double width, double height, int nx, int ny)
   {
      var concrete = BuildConcreteMaterial();
      double x0 = -width / 2, x1 = width / 2, y0 = -height / 2, y1 = height / 2;
      var hull = new Contour(
         new[] { x0, x1, x1, x0, x0 },
         new[] { y0, y0, y1, y1, y0 },
         "rect")
      { Type = ContourType.Hull };

      var area = new MaterialArea
      {
         Id = 1,
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

      // Армирование: 4 стержня Ø25 по углам, защитный слой 0.05 м.
      var steel = BuildSteelMaterial();
      double cover = 0.05, dia = 0.025;
      double rx = width / 2 - cover, ry = height / 2 - cover;
      var bars = new[]
      {
         Fiber.CreatePoint(dia, -rx, -ry),
         Fiber.CreatePoint(dia,  rx, -ry),
         Fiber.CreatePoint(dia,  rx,  ry),
         Fiber.CreatePoint(dia, -rx,  ry),
      };
      var rebar = MaterialArea.CreateRebarArea(bars, steel, DiagrammType.L2, area);

      return new CrossSection
      {
         Tag = "limit-force-rc-rect",
         Areas = [area, rebar]
      };
   }

   static Material BuildConcreteMaterial()
   {
      MaterialChars Ch(CalcType ct) => new(ct)
      {
         E = 32_500_000,
         Fc = -17_000,
         Ft = 1_200,
         Ec2 = -0.0035,
         Ec1Red = -17_000.0 / 32_500_000,
         Et1Red = 1_200.0 / 32_500_000,
         Et2 = 0.00015,
         Type = MatType.Concrete
      };

      var m = new Material
      {
         Id = 10_001,
         Tag = "test-concrete",
         Type = MatType.Concrete,
         E = 32_500_000
      };
      m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
      return m;
   }

   static Material BuildSteelMaterial()
   {
      MaterialChars Ch(CalcType ct) => new(ct)
      {
         E = 200_000_000,
         Fc = -435_000,
         Ft = 435_000,
         Ec2 = -0.025,
         Et2 = 0.025,
         Type = MatType.ReSteelF
      };

      var m = new Material
      {
         Id = 10_002,
         Tag = "test-steel-A500",
         Type = MatType.ReSteelF,
         E = 200_000_000
      };
      m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
      return m;
   }
}
