namespace CScore;

/// <summary>
/// Поиск предельного коэффициента пропорционального нагружения.
/// </summary>
public sealed class LimitForceSolver
{
   readonly ILimitSection _section;
   readonly CalcType _calc;
   readonly LimitSectionStrainSolver _solver;
   readonly double _solverTol;
   readonly bool _ten;
   readonly bool _ca;
   readonly double _bisectTol;
   readonly int _bisectMaxIter;

   /// <summary>
   /// Создаёт решатель предельного коэффициента.
   /// </summary>
   public LimitForceSolver(
      ILimitSection section,
      CrossSection guessSection,
      CalcType calc = CalcType.C,
      double solverTol = 0.5,
      int solverMaxIter = 60,
      double solverStep = 1e-7,
      bool ten = true,
      bool ca = true,
      double bisectTol = 1e-4,
      int bisectMaxIter = 60)
   {
      _section = section ?? throw new ArgumentNullException(nameof(section));
      guessSection ??= section is CrossSectionLimitAdapter adapter
         ? adapter.Section
         : throw new ArgumentException(
            "Для ILimitSection, отличного от CrossSectionLimitAdapter, требуется guessSection.",
            nameof(guessSection));

      _calc = calc;
      _solverTol = solverTol;
      _ten = ten;
      _ca = ca;
      _bisectTol = bisectTol;
      _bisectMaxIter = bisectMaxIter;
      _solver = new LimitSectionStrainSolver(section, guessSection, calc, solverTol, solverMaxIter, solverStep);
   }

   /// <summary>Создаёт решатель для обычного сечения через адаптер.</summary>
   public static LimitForceSolver ForCrossSection(CrossSection section, CalcType calc = CalcType.C)
      => new(new CrossSectionLimitAdapter(section, calc), section, calc);

   /// <summary>
   /// Предельный коэффициент пропорционального нагружения k·(N, Mx, My).
   /// </summary>
   public LimitForceResult AllFactor(double n, double mx, double my)
   {
      int totalNewton = 0;

      (bool Feasible, Kurvature? StrainPlane) FeasibleAt(double k)
      {
         bool ok = IsFeasible(k * n, k * mx, k * my, out var sp);
         totalNewton += _solver.Iterations;
         return (ok, sp);
      }

      double? a = null;
      double? b = null;
      bool feasibleSeen = false;

      var kGrid = new List<double> { 0.0 };
      double kCur = 1e-6;
      while (kCur < 1e12)
      {
         kGrid.Add(kCur);
         kCur *= 2.0;
      }
      kGrid.Add(1e12);

      foreach (double k in kGrid)
      {
         var (feasible, _) = FeasibleAt(k);
         if (feasible)
         {
            feasibleSeen = true;
            a = k;
            continue;
         }

         if (feasibleSeen)
         {
            b = k;
            break;
         }
      }

      if (!feasibleSeen || a is null)
      {
         return new LimitForceResult
         {
            Factor = 0.0,
            Utilization = double.PositiveInfinity,
            Converged = false,
            Iterations = 0,
            NewtonIterations = totalNewton,
            StrainPlane = null,
            NLimit = 0.0,
            MxLimit = 0.0,
            MyLimit = 0.0,
            EpsContourMin = 0.0,
            EpsCu = _section.EpsCu,
            EpsRebarMax = null,
            EpsSu = null,
            Governing = "none"
         };
      }

      if (b is null)
      {
         double ka = a.Value;
         return new LimitForceResult
         {
            Factor = ka,
            Utilization = ka > 1e-15 ? 1.0 / ka : double.PositiveInfinity,
            Converged = false,
            Iterations = 0,
            NewtonIterations = totalNewton,
            StrainPlane = null,
            NLimit = ka * n,
            MxLimit = ka * mx,
            MyLimit = ka * my,
            EpsContourMin = 0.0,
            EpsCu = _section.EpsCu,
            EpsRebarMax = null,
            EpsSu = null,
            Governing = "none"
         };
      }

      Kurvature? bestSp = null;
      int nIter = 0;
      double left = a.Value;
      double right = b.Value;

      for (nIter = 1; nIter <= _bisectMaxIter; nIter++)
      {
         double mid = 0.5 * (left + right);
         var (feasible, sp) = FeasibleAt(mid);
         if (feasible)
         {
            left = mid;
            bestSp = sp;
         }
         else
         {
            right = mid;
         }

         if ((right - left) <= _bisectTol)
            break;
      }

      bool converged = (right - left) <= _bisectTol * 10.0;
      double kFinal = left;

      double epsContourMin = 0.0;
      double? epsRebarMax = null;
      double? epsSu = null;
      if (bestSp.HasValue)
      {
         var sp = bestSp.Value;
         epsContourMin = _section.ContourVertices.Any()
            ? _section.ContourVertices.Min(p => Eps(sp, p.X, p.Y))
            : 0.0;
         if (_section.RebarPoints.Any())
         {
            epsRebarMax = _section.RebarPoints.Max(p => Eps(sp, p.X, p.Y));
            epsSu = _section.RebarPoints.Max(p => p.EpsSu);
         }
      }

      const double govTol = 0.02;
      bool concGoverns = bestSp.HasValue
         && Math.Abs(epsContourMin - _section.EpsCu) < Math.Abs(_section.EpsCu) * govTol;
      bool rebarGoverns = epsRebarMax.HasValue && epsSu.HasValue
         && Math.Abs(epsRebarMax.Value - epsSu.Value) < Math.Abs(epsSu.Value) * govTol;

      string governing = concGoverns && rebarGoverns
         ? "both"
         : rebarGoverns
            ? "rebar"
            : concGoverns
               ? "concrete"
               : "none";

      return new LimitForceResult
      {
         Factor = kFinal,
         Utilization = kFinal > 1e-15 ? 1.0 / kFinal : double.PositiveInfinity,
         Converged = converged,
         Iterations = nIter,
         NewtonIterations = totalNewton,
         StrainPlane = bestSp,
         NLimit = kFinal * n,
         MxLimit = kFinal * mx,
         MyLimit = kFinal * my,
         EpsContourMin = epsContourMin,
         EpsCu = _section.EpsCu,
         EpsRebarMax = epsRebarMax,
         EpsSu = epsSu,
         Governing = governing
      };
   }

   bool IsFeasible(double n, double mx, double my, out Kurvature? strainPlane)
   {
      strainPlane = null;

      Kurvature k = _solver.Solve(n, mx, my);
      if (!_solver.Converged || _solver.Residual > _solverTol)
         return false;

      Load act = _section.Integral(k, _calc);
      double resid = Math.Sqrt(
         Math.Pow(act.N - n, 2) +
         Math.Pow(act.Mx - mx, 2) +
         Math.Pow(act.My - my, 2));
      if (resid > 1.0)
         return false;

      foreach (var p in _section.ContourVertices)
      {
         if (Eps(k, p.X, p.Y) < _section.EpsCu)
            return false;
      }

      foreach (var rb in _section.RebarPoints)
      {
         if (Eps(k, rb.X, rb.Y) > rb.EpsSu)
            return false;
      }

      strainPlane = k;
      return true;
   }

   static double Eps(Kurvature k, double x, double y) => k.e0 + k.ky * y + k.kz * x;
}
