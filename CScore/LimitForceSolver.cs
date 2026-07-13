namespace CScore;

/// <summary>
/// Поиск предельного коэффициента нагружения методом бисекции
/// с явной проверкой деформаций контура и арматуры.
/// </summary>
public sealed class LimitForceSolver : ILimitForceSolver
{
   readonly ILimitSection _section;
   readonly CrossSection _guessSection;
   readonly CalcType _calc;
   readonly LimitSectionStrainSolver _solver;
   readonly double _solverTol;
   readonly double _bisectTol;
   readonly int _bisectMaxIter;
   readonly bool _ten;
   readonly LimitForceParams? _eta;

   /// <summary>Создаёт бисекционный решатель предельных усилий.</summary>
   public LimitForceSolver(
      ILimitSection section,
      CrossSection guessSection,
      CalcType calc = CalcType.C,
      double solverTol = 0.5,
      int solverMaxIter = 60,
      double solverStep = 1e-7,
      double bisectTol = 1e-4,
      int bisectMaxIter = 60,
      bool ten = true,
      LimitForceParams? etaParams = null)
   {
      _section = section ?? throw new ArgumentNullException(nameof(section));
      _guessSection = guessSection ?? (section is CrossSectionLimitAdapter adapter
         ? adapter.Section
         : throw new ArgumentException(
            "Для ILimitSection, отличного от CrossSectionLimitAdapter, требуется guessSection.",
            nameof(guessSection)));

      _calc = calc;
      _solverTol = solverTol;
      _bisectTol = bisectTol;
      _bisectMaxIter = bisectMaxIter;
      _ten = ten;
      _eta = etaParams is { EtaEnabled: true } ? etaParams : null;
      _solver = new LimitSectionStrainSolver(section, _guessSection, calc, solverTol, solverMaxIter, solverStep, ten);
   }

   /// <summary>Создаёт решатель для обычного сечения через адаптер.</summary>
   public static LimitForceSolver ForCrossSection(
      CrossSection section,
      CalcType calc = CalcType.C,
      double solverTol = 0.5,
      int solverMaxIter = 60,
      double bisectTol = 1e-4,
      int bisectMaxIter = 60,
      bool ten = true,
      LimitForceParams? etaParams = null)
      => new(new CrossSectionLimitAdapter(section, calc), section, calc,
         solverTol, solverMaxIter, bisectTol: bisectTol, bisectMaxIter: bisectMaxIter, ten: ten, etaParams: etaParams);

   /// <inheritdoc/>
   public LimitForceResult AllFactor(double n, double mx, double my)
      => Bisect(k => k * n, k => k * mx, k => k * my);

   /// <summary>
   /// Предельный коэффициент k·(Mx, My) при фиксированном N. Если в параметрах
   /// задачи включена поправка η (п. 8.1.15 — при N=const её можно пересчитывать
   /// на каждой пробной точке k без риска потери устойчивости бисекции, в
   /// отличие от AllFactor/AxialFactor, где N сам является искомой величиной),
   /// на каждом пробном k момент (k·mx, k·my) перед проверкой вместимости
   /// сечения усиливается через <see cref="Sp63.RodEtaWiring.Apply"/>; в отчёте
   /// (MxLimit/MyLimit) при этом остаётся исходный (неусиленный) момент —
   /// диагностика усиления доступна в <see cref="LimitForceResult.Eta"/>.
   /// </summary>
   public LimitForceResult MomentFactor(double n, double mx, double my)
      => _eta != null
         ? Bisect(_ => n, k => k * mx, k => k * my, AmplifyForEta)
         : Bisect(_ => n, k => k * mx, k => k * my);

   /// <inheritdoc/>
   public LimitForceResult AxialFactor(double n, double mx, double my)
      => Bisect(k => k * n, _ => mx, _ => my);

   Sp63.RodEtaWiring.Result AmplifyForEta(double n, double mxRaw, double myRaw)
      => Sp63.RodEtaWiring.Apply(
         _guessSection, n, mxRaw, myRaw,
         _eta!.EtaL0x, _eta.EtaL0y,
         _eta.EtaPsiX ?? 1.0, _eta.EtaPsiY ?? 1.0,
         _eta.EtaIterative,
         (tmx, tmy) => _solver.Solve(n, tmx, tmy),
         _eta.EtaSlendernessThreshold ?? Sp63.EccentricityAmplifier.SlendernessThreshold);

   LimitForceResult Bisect(
      Func<double, double> nFn, Func<double, double> mxFn, Func<double, double> myFn,
      Func<double, double, double, Sp63.RodEtaWiring.Result>? amplify = null)
   {
      int totalNewton = 0;

      (bool Feasible, Kurvature? StrainPlane, Sp63.RodEtaWiring.Result? Eta) FeasibleAt(double k)
      {
         double n = nFn(k), mxRaw = mxFn(k), myRaw = myFn(k);
         double mx = mxRaw, my = myRaw;
         Sp63.RodEtaWiring.Result? eta = null;
         if (amplify != null)
         {
            var wiring = amplify(n, mxRaw, myRaw);
            eta = wiring;
            mx = wiring.MxEff;
            my = wiring.MyEff;
         }
         bool ok = IsFeasible(n, mx, my, out var sp);
         totalNewton += _solver.Iterations;
         return (ok, sp, eta);
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
         var (feasible, _, _) = FeasibleAt(k);
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
         return EmptyResult(totalNewton, nFn(0), mxFn(0), myFn(0));

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
            NLimit = nFn(ka),
            MxLimit = mxFn(ka),
            MyLimit = myFn(ka),
            EpsContourMin = 0.0,
            EpsCu = _section.EpsCu,
            Governing = "none"
         };
      }

      Kurvature? bestSp = null;
      Sp63.RodEtaWiring.Result? bestEta = null;
      int nIter = 0;
      double left = a.Value;
      double right = b.Value;

      for (nIter = 1; nIter <= _bisectMaxIter; nIter++)
      {
         double mid = 0.5 * (left + right);
         var (feasible, sp, eta) = FeasibleAt(mid);
         if (feasible)
         {
            left = mid;
            bestSp = sp;
            bestEta = eta;
         }
         else
         {
            right = mid;
         }

         if ((right - left) <= _bisectTol)
            break;
      }

      return BuildResult(
         left, nIter, totalNewton, bestSp, bestEta,
         (right - left) <= _bisectTol * 10.0,
         nFn, mxFn, myFn);
   }

   LimitForceResult EmptyResult(int totalNewton, double n0, double mx0, double my0)
      => new()
      {
         Factor = 0.0,
         Utilization = double.PositiveInfinity,
         Converged = false,
         Iterations = 0,
         NewtonIterations = totalNewton,
         NLimit = n0,
         MxLimit = mx0,
         MyLimit = my0,
         EpsCu = _section.EpsCu,
         Governing = "none"
      };

   LimitForceResult BuildResult(
      double k,
      int nIter,
      int totalNewton,
      Kurvature? bestSp,
      Sp63.RodEtaWiring.Result? bestEta,
      bool converged,
      Func<double, double> nFn,
      Func<double, double> mxFn,
      Func<double, double> myFn)
   {
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

      string governing = concGoverns && rebarGoverns ? "both"
         : rebarGoverns ? "rebar"
         : "concrete";

      return new LimitForceResult
      {
         Factor = k,
         Utilization = k > 1e-15 ? 1.0 / k : double.PositiveInfinity,
         Converged = converged,
         Iterations = nIter,
         NewtonIterations = totalNewton,
         StrainPlane = bestSp,
         NLimit = nFn(k),
         MxLimit = mxFn(k),
         MyLimit = myFn(k),
         EpsContourMin = epsContourMin,
         EpsCu = _section.EpsCu,
         EpsRebarMax = epsRebarMax,
         EpsSu = epsSu,
         Governing = governing,
         Eta = bestEta
      };
   }

   bool IsFeasible(double n, double mx, double my, out Kurvature? strainPlane)
   {
      strainPlane = null;

      Kurvature k = _solver.Solve(n, mx, my);

      if (!_solver.Converged || _solver.Residual > _solverTol)
         return false;

      Load act = _section.Integral(k, _calc, _ten);
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
