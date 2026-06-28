namespace CScore;

/// <summary>
/// Быстрый поиск предельного коэффициента нагружения методом Ньютона 3×3
/// с пином на наиболее сжатой вершине контура. При расходимости — fallback на бисекцию.
/// </summary>
public sealed class LimitForceSolverFast : ILimitForceSolver
{
   /// <summary>Необязательный трассировщик для диагностики fallback на бисекцию.</summary>
   public static Action<string>? DebugTrace;

   enum SolveMode { All, Moment, Axial }

   readonly CrossSection _section;
   readonly CrossSectionLimitAdapter _adapter;
   readonly CalcType _calc;
   readonly double _tol;
   readonly int _maxIter;
   readonly double _hDiff;
   readonly double _bisectTol;
   readonly int _bisectMaxIter;
   readonly double _solverTol;
   readonly int _solverMaxIter;

   readonly List<(double X, double Y)> _contourPts;
   readonly List<(double X, double Y, double EpsSu)> _rebarLimits;
   readonly LimitSectionStrainSolver _strainSolver;
   readonly double _epsCu;
   readonly double _yRef;
   readonly double _xRef;
   const double _relTol = 1e-3;

   /// <summary>Создаёт быстрый решатель предельных усилий.</summary>
   public LimitForceSolverFast(
      CrossSection section,
      CalcType calc = CalcType.C,
      double newtonTol = 0.5,
      int newtonMaxIter = 60,
      double hDiff = 1e-6,
      double bisectTol = 1e-4,
      int bisectMaxIter = 60,
      double solverTol = 0.5,
      int solverMaxIter = 60)
   {
      _section = section ?? throw new ArgumentNullException(nameof(section));
      _calc = calc;
      _tol = newtonTol;
      _maxIter = newtonMaxIter;
      _hDiff = hDiff;
      _bisectTol = bisectTol;
      _bisectMaxIter = bisectMaxIter;
      _solverTol = solverTol;
      _solverMaxIter = solverMaxIter;

      _adapter = new CrossSectionLimitAdapter(section, calc);
      _contourPts = _adapter.ContourVertices.ToList();
      _rebarLimits = _adapter.RebarPoints.ToList();
      _strainSolver = new LimitSectionStrainSolver(_adapter, section, calc, solverTol, solverMaxIter);
      _epsCu = _adapter.EpsCu;

      if (_contourPts.Count == 0)
         throw new InvalidOperationException("LimitForceSolverFast: нет вершин контура.");

      var allPts = _contourPts.Concat(_rebarLimits.Select(r => (r.X, r.Y))).ToList();
      _yRef = Math.Max(allPts.Max(p => Math.Abs(p.Y)), 1e-12);
      _xRef = Math.Max(allPts.Max(p => Math.Abs(p.X)), 1e-12);
   }

   /// <inheritdoc/>
   public LimitForceResult AllFactor(double n, double mx, double my)
      => Solve(n, mx, my, k => k * n, k => k * mx, k => k * my, n, mx, my, SolveMode.All);

   /// <inheritdoc/>
   public LimitForceResult MomentFactor(double n, double mx, double my)
      => Solve(n, mx, my, _ => n, k => k * mx, k => k * my, 0, mx, my, SolveMode.Moment);

   /// <inheritdoc/>
   public LimitForceResult AxialFactor(double n, double mx, double my)
      => Solve(n, mx, my, k => k * n, _ => mx, _ => my, n, 0, 0, SolveMode.Axial);

   LimitForceResult Solve(
      double n, double mx, double my,
      Func<double, double> nFn, Func<double, double> mxFn, Func<double, double> myFn,
      double dNdk, double dMxdk, double dMydk,
      SolveMode mode)
   {
      double forceMag = Math.Abs(n) + Math.Abs(mx) + Math.Abs(my);
      double momentRatio = forceMag > 1e-30
         ? Math.Sqrt(Math.Pow(mx / forceMag, 2) + Math.Pow(my / forceMag, 2))
         : 0.0;

      if (momentRatio < 1e-6)
      {
         var axial = SolveAxial(nFn, dNdk);
         if (axial is not null && IsValidAxial(axial, nFn, mxFn, myFn))
            return axial;
      }

      var guess = _section.Guess(new Load { N = n, Mx = mx, My = my });
      var comp = SolveCompression(n, mx, my, nFn, mxFn, myFn, dNdk, dMxdk, dMydk, guess);
      LimitForceResult? tens = null;
      if (_rebarLimits.Count > 0 && n > 0)
         tens = SolveTension(nFn, mxFn, myFn, dNdk, dMxdk, dMydk, guess);

      var candidates = new[] { comp, tens }.Where(r => r is { Converged: true }).ToList();
      if (candidates.Count > 0)
         return candidates.MinBy(r => r!.Factor)!;

      DebugTrace?.Invoke($"BisectFallback: comp={(comp is { Converged: true } ? comp.Factor.ToString("G6") : "fail")} tens={(tens is { Converged: true } ? tens.Factor.ToString("G6") : "fail")} mode={mode}");
      return BisectFallback(n, mx, my, mode);
   }

   LimitForceResult? SolveAxial(Func<double, double> nFn, double dNdk)
   {
      if (Math.Abs(dNdk) < 1e-30)
         return null;

      var sp = new Kurvature { e0 = _epsCu, ky = 0, kz = 0 };
      var f0 = Forces(sp);
      double k = f0.N / dNdk;
      if (k <= 0 || !double.IsFinite(k))
         return null;

      foreach (var rb in _rebarLimits)
      {
         if (Eps(sp, rb.X, rb.Y) > rb.EpsSu)
            return null;
      }

      return BuildResult(k, 1, 1, sp, nFn, _ => 0, _ => 0, "concrete");
   }

   LimitForceResult? SolveCompression(
      double n, double mx, double my,
      Func<double, double> nFn, Func<double, double> mxFn, Func<double, double> myFn,
      double dNdk, double dMxdk, double dMydk,
      Kurvature guess)
   {
      int innerIters = 0;
      if (!TryEstimateCompressionStart(n, mx, my, guess, dNdk, dMxdk, dMydk,
            out double xA, out double yA, out double kx0, out double ky0, out double k0, ref innerIters))
      {
         DebugTrace?.Invoke("SolveCompression: TryEstimateCompressionStart failed");
         return null;
      }

      int nDrivers = (Math.Abs(dNdk) > 1e-30 ? 1 : 0)
                   + (Math.Abs(dMxdk) > 1e-30 ? 1 : 0)
                   + (Math.Abs(dMydk) > 1e-30 ? 1 : 0);
      if (nDrivers == 1)
      {
         var single = SolveSingleDriver(n, mx, my, xA, yA, kx0, ky0,
            nFn, mxFn, myFn, dNdk, dMxdk, dMydk);
         if (single is not null)
         {
            single.NewtonIterations += innerIters;
            return single;
         }
      }

      var (kx, ky, k, nIter, conv, spFinal) = Newton(xA, yA, kx0, ky0, k0,
         nFn, mxFn, myFn, dNdk, dMxdk, dMydk, _epsCu);

      // Отклонить вырожденное решение Ньютона (kx,ky→∞, k→0): силы и цель → 0,
      // невязка ложно мала. Физическая допустимость проверяется в IsValidSolution.
      if (conv && k < 1e-6)
      {
         DebugTrace?.Invoke($"SolveCompression: Newton degenerate k={k:G6}");
         conv = false;
      }

      if (!conv && nDrivers < 3)
      {
         var fb2d = TryNewton2d(xA, yA, kx0, ky0, k0, n, mx, my,
            nFn, mxFn, myFn, dNdk, dMxdk, dMydk, out kx, out ky, out k, out nIter, out conv, out spFinal);
         if (!fb2d)
         {
            DebugTrace?.Invoke($"SolveCompression: Newton3d+Newton2d failed (nDrivers={nDrivers}, nIter={nIter})");
            return null;
         }
      }

      // Бетонная фаза должна лишь сойтись; допустимость по арматуре проверяет
      // арматурная фаза ниже (перепинивает на управляющий стержень при εs > εsu).
      if (!conv)
      {
         DebugTrace?.Invoke($"SolveCompression: Newton not converged (nIter={nIter}, k={k:G6})");
         return null;
      }

      int rebarIter = 0;
      bool rebarGoverns = false;
      if (_rebarLimits.Count > 0)
      {
         // RebarPhase == null → бетон управляет (ни один стержень не за пределом),
         // сохраняем бетонное решение. Иначе → перепин на арматуру.
         var rebar = RebarPhase(kx, ky, k, spFinal, nFn, mxFn, myFn, dNdk, dMxdk, dMydk);
         if (rebar is not null)
         {
            (kx, ky, k, rebarIter, conv, spFinal) = rebar.Value;
            if (!conv)
            {
               DebugTrace?.Invoke("SolveCompression: RebarPhase not converged");
               return null;
            }
            rebarGoverns = true;
         }
      }

      if (!IsValidSolution(kx, ky, k, spFinal, nFn, mxFn, myFn))
      {
         var f = Forces(spFinal);
         double nT = nFn(k), mxT = mxFn(k), myT = myFn(k);
         double res = Math.Sqrt(Math.Pow(f.N - nT, 2) + Math.Pow(f.Mx - mxT, 2) + Math.Pow(f.My - myT, 2));
         double epsMin = _contourPts.Min(p => Eps(spFinal, p.X, p.Y));
         DebugTrace?.Invoke($"SolveCompression: IsValidSolution failed k={k:G6} res={res:G6} resTol={ResidualTol(k, nFn, mxFn, myFn):G6} epsMin={epsMin:G6} epsCu={_epsCu:G6}");
         return null;
      }

      string gov = rebarGoverns ? "rebar" : "concrete";
      DebugTrace?.Invoke($"SolveCompression: OK k={k:G6} nIter={nIter} inner={innerIters} rebar={rebarIter} gov={gov}");
      var result = BuildResult(k, nIter, innerIters + nIter + rebarIter, spFinal, nFn, mxFn, myFn, gov);
      return result;
   }

   /// <summary>
   /// Начальное приближение для Ньютона: сначала упругое (порт из Python) — лучшая
   /// обусловленность якобиана и близкая к нулю начальная невязка. Запасной вариант —
   /// бисекция по k + StrainSolver.
   /// </summary>
   bool TryEstimateCompressionStart(
      double n, double mx, double my, Kurvature elasticGuess,
      double dNdk, double dMxdk, double dMydk,
      out double xA, out double yA,
      out double kx0, out double ky0, out double k0,
      ref int innerIters)
   {
      xA = yA = kx0 = ky0 = k0 = 0;

      // Упругое приближение (python: elastic_guess → pin A → projection k0).
      // Якобиан в этой точке хорошо обусловлен — Ньютон не уходит в k≈0.
      if (_contourPts.Count > 0)
      {
         var strains = _contourPts.Select(p => Eps(elasticGuess, p.X, p.Y)).ToList();
         double minEps = strains.Min();
         if (minEps < -1e-12)
         {
            int iMin = strains.IndexOf(minEps);
            (double exA, double eyA) = _contourPts[iMin];
            double scale = _epsCu / minEps;
            double kx0el = elasticGuess.kz * scale;
            double ky0el = elasticGuess.ky * scale;
            if (double.IsFinite(kx0el) && double.IsFinite(ky0el))
            {
               var spEl = MakeSp(kx0el, ky0el, exA, eyA, _epsCu);
               var fEl = Forces(spEl);
               double denom = dNdk * dNdk + dMxdk * dMxdk + dMydk * dMydk;
               if (denom > 1e-30)
               {
                  double k0el = (fEl.N * dNdk + fEl.Mx * dMxdk + fEl.My * dMydk) / denom;
                  if (k0el > 0 && double.IsFinite(k0el))
                  {
                     xA = exA; yA = eyA;
                     kx0 = kx0el; ky0 = ky0el;
                     k0 = k0el;
                     DebugTrace?.Invoke($"TryEstimateCompressionStart: elastic guess k0={k0:G6} pin=({xA:G4},{yA:G4})");
                     return true;
                  }
                  DebugTrace?.Invoke($"TryEstimateCompressionStart: elastic k0el={k0el:G6} <=0 (fEl N={fEl.N:G4} Mx={fEl.Mx:G4} My={fEl.My:G4})");
                  if (TryBracketKLite(n, mx, my, dNdk, dMxdk, dMydk, out double kLite, ref innerIters))
                     return FinishBracketStart(kLite, n, mx, my, dNdk, dMxdk, dMydk, elasticGuess,
                        ref innerIters, out xA, out yA, out kx0, out ky0, out k0, lite: true);
               }
            }
            else
               DebugTrace?.Invoke("TryEstimateCompressionStart: elastic non-finite kx0/ky0");
         }
         else
            DebugTrace?.Invoke($"TryEstimateCompressionStart: elastic minEps={minEps:G6} (no compression on contour)");
      }

      // Запасной вариант: полный bracket по k + StrainSolver.
      if (!TryBracketK(n, mx, my, dNdk, dMxdk, dMydk, out double kLo, out _, ref innerIters))
      {
         DebugTrace?.Invoke("TryEstimateCompressionStart: TryBracketK failed");
         return false;
      }

      return FinishBracketStart(kLo, n, mx, my, dNdk, dMxdk, dMydk, elasticGuess,
         ref innerIters, out xA, out yA, out kx0, out ky0, out k0, lite: false);
   }

   bool FinishBracketStart(
      double k0val,
      double n, double mx, double my,
      double dNdk, double dMxdk, double dMydk,
      Kurvature elasticGuess,
      ref int innerIters,
      out double xA, out double yA, out double kx0, out double ky0, out double k0,
      bool lite)
   {
      xA = yA = kx0 = ky0 = 0;
      k0 = k0val;
      var (nk, mxk, myk) = LoadAtK(k0, n, mx, my, dNdk, dMxdk, dMydk);
      var sp = _strainSolver.Solve(nk, mxk, myk);
      innerIters += _strainSolver.Iterations;
      if (!_strainSolver.Converged)
      {
         DebugTrace?.Invoke($"TryEstimateCompressionStart: StrainSolver failed at k0={k0:G6}");
         return false;
      }

      if (!TryPinFromPlane(sp, elasticGuess, out xA, out yA, out kx0, out ky0))
      {
         DebugTrace?.Invoke("TryEstimateCompressionStart: TryPinFromPlane failed");
         return false;
      }

      string tag = lite ? "bracket-lite" : "bracket";
      DebugTrace?.Invoke($"TryEstimateCompressionStart: {tag} k0={k0:G6} innerIters={innerIters} pin=({xA:G4},{yA:G4})");
      return k0 > 0 && double.IsFinite(k0);
   }

   static (double N, double Mx, double My) LoadAtK(
      double k, double n, double mx, double my,
      double dNdk, double dMxdk, double dMydk)
      => (
         Math.Abs(dNdk) > 1e-30 ? k * n : n,
         Math.Abs(dMxdk) > 1e-30 ? k * mx : mx,
         Math.Abs(dMydk) > 1e-30 ? k * my : my);

   /// <summary>
   /// Облегчённый bracket: k=1 → удвоение до первого недопустимого, 4 шага бисекции.
   /// ~10–15 вызовов StrainSolver вместо ~80.
   /// </summary>
   bool TryBracketKLite(
      double n, double mx, double my,
      double dNdk, double dMxdk, double dMydk,
      out double kLo, ref int innerIters)
   {
      kLo = 0;
      const int maxBisect = 4;
      const double kMax = 1e4;

      double kFeas = 0;
      foreach (double kTry in new[] { 1.0, 0.5, 0.25, 0.125 })
      {
         var (nk, mxk, myk) = LoadAtK(kTry, n, mx, my, dNdk, dMxdk, dMydk);
         if (IsFeasibleLoad(nk, mxk, myk, out _, ref innerIters))
         {
            kFeas = kTry;
            break;
         }
      }

      if (kFeas <= 0)
         return false;

      double kInf = kFeas * 2.0;
      while (kInf <= kMax)
      {
         var (nk, mxk, myk) = LoadAtK(kInf, n, mx, my, dNdk, dMxdk, dMydk);
         if (!IsFeasibleLoad(nk, mxk, myk, out _, ref innerIters))
            break;
         kFeas = kInf;
         kInf *= 2.0;
      }

      if (kInf <= kFeas)
         kInf = Math.Min(kFeas * 4.0, kMax);

      kLo = kFeas;
      for (int i = 0; i < maxBisect && (kInf - kLo) > _bisectTol * Math.Max(kLo, 1e-6); i++)
      {
         double mid = 0.5 * (kLo + kInf);
         var (nk, mxk, myk) = LoadAtK(mid, n, mx, my, dNdk, dMxdk, dMydk);
         if (IsFeasibleLoad(nk, mxk, myk, out _, ref innerIters))
            kLo = mid;
         else
            kInf = mid;
      }

      return kLo > 0;
   }

   bool TryBracketK(double n, double mx, double my,
      double dNdk, double dMxdk, double dMydk,
      out double kLo, out double kHi, ref int innerIters)
   {
      kLo = 0;
      kHi = 0;
      double? feasible = null;
      double? infeasible = null;

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
         if (k <= 0)
            continue;

         var load = LoadAtK(k, n, mx, my, dNdk, dMxdk, dMydk);
         if (IsFeasibleLoad(load.N, load.Mx, load.My, out _, ref innerIters))
         {
            feasible = k;
            continue;
         }

         if (feasible is not null)
         {
            infeasible = k;
            break;
         }
      }

      if (feasible is null || infeasible is null)
         return false;

      kLo = feasible.Value;
      kHi = infeasible.Value;
      for (int i = 0; i < 6 && (kHi - kLo) > _bisectTol; i++)
      {
         double mid = 0.5 * (kLo + kHi);
         var loadMid = LoadAtK(mid, n, mx, my, dNdk, dMxdk, dMydk);
         if (IsFeasibleLoad(loadMid.N, loadMid.Mx, loadMid.My, out _, ref innerIters))
            kLo = mid;
         else
            kHi = mid;
      }

      return kHi > kLo;
   }

   bool IsFeasibleLoad(double n, double mx, double my, out Kurvature sp, ref int innerIters)
   {
      sp = _strainSolver.Solve(n, mx, my);
      innerIters += _strainSolver.Iterations;
      if (!_strainSolver.Converged || _strainSolver.Residual > _solverTol * 5.0)
         return false;

      foreach (var p in _contourPts)
      {
         if (Eps(sp, p.X, p.Y) < _epsCu - 1e-6)
            return false;
      }

      foreach (var rb in _rebarLimits)
      {
         if (Eps(sp, rb.X, rb.Y) > rb.EpsSu + 1e-6)
            return false;
      }

      return true;
   }

   bool TryPinFromPlane(Kurvature sp, Kurvature fallback,
      out double xA, out double yA, out double kx0, out double ky0)
   {
      xA = yA = kx0 = ky0 = 0;
      if (_contourPts.Count == 0)
         return false;

      var strains = _contourPts.Select(p => Eps(sp, p.X, p.Y)).ToList();
      int iMin = strains.IndexOf(strains.Min());
      (xA, yA) = _contourPts[iMin];
      double epsA = strains[iMin];

      if (Math.Abs(epsA) > 1e-12)
      {
         double scale = _epsCu / epsA;
         kx0 = sp.kz * scale;
         ky0 = sp.ky * scale;
         return double.IsFinite(kx0) && double.IsFinite(ky0);
      }

      var fbStrains = _contourPts.Select(p => Eps(fallback, p.X, p.Y)).ToList();
      int iFb = fbStrains.IndexOf(fbStrains.Min());
      (xA, yA) = _contourPts[iFb];
      double mxR = Math.Abs(sp.ky), myR = Math.Abs(sp.kz);
      if (mxR >= myR)
      {
         kx0 = 0;
         ky0 = _epsCu / Math.Max(Math.Abs(yA), 1e-3);
      }
      else
      {
         kx0 = _epsCu / Math.Max(Math.Abs(xA), 1e-3);
         ky0 = 0;
      }

      return true;
   }

   bool IsValidAxial(LimitForceResult res, Func<double, double> nFn, Func<double, double> mxFn, Func<double, double> myFn)
   {
      if (res.StrainPlane is not Kurvature sp)
         return false;
      return IsValidSolution(sp.kz, sp.ky, res.Factor, sp, nFn, mxFn, myFn);
   }

   bool IsValidSolution(
      double kx, double ky, double k, Kurvature sp,
      Func<double, double> nFn, Func<double, double> mxFn, Func<double, double> myFn)
   {
      if (k <= 0 || !double.IsFinite(k))
         return false;

      var f = Forces(sp);
      double nT = nFn(k), mxT = mxFn(k), myT = myFn(k);
      double res = Math.Sqrt(
         Math.Pow(f.N - nT, 2) +
         Math.Pow(f.Mx - mxT, 2) +
         Math.Pow(f.My - myT, 2));
      double tgt = Math.Sqrt(nT * nT + mxT * mxT + myT * myT);
      if (res > ResidualTol(k, nFn, mxFn, myFn))
         return false;

      double epsMin = _contourPts.Min(p => Eps(sp, p.X, p.Y));
      if (epsMin < _epsCu - Math.Max(1e-5, Math.Abs(_epsCu) * 0.02))
         return false;

      foreach (var rb in _rebarLimits)
      {
         if (Eps(sp, rb.X, rb.Y) > rb.EpsSu + Math.Max(1e-5, Math.Abs(rb.EpsSu) * 0.02))
            return false;
      }

      return true;
   }

   double ResidualTol(double k, Func<double, double> nFn, Func<double, double> mxFn, Func<double, double> myFn)
   {
      double nT = nFn(k), mxT = mxFn(k), myT = myFn(k);
      double mag = Math.Sqrt(nT * nT + mxT * mxT + myT * myT);
      return Math.Max(_relTol * Math.Max(mag, 1e-9), 1e-9);
   }

   LimitForceResult? SolveTension(
      Func<double, double> nFn, Func<double, double> mxFn, Func<double, double> myFn,
      double dNdk, double dMxdk, double dMydk,
      Kurvature guess)
   {
      if (_rebarLimits.Count == 0)
         return null;

      var best = _rebarLimits
         .Select(rb => (rb, ratio: Eps(guess, rb.X, rb.Y) / rb.EpsSu))
         .OrderByDescending(x => x.ratio)
         .First();

      double k0 = 1.0;
      var (kx, ky, k, nIter, conv, sp) = Newton(best.rb.X, best.rb.Y, guess.kz, guess.ky, k0,
         nFn, mxFn, myFn, dNdk, dMxdk, dMydk, best.rb.EpsSu);

      if (!conv)
         return null;

      return BuildResult(k, nIter, nIter, sp, nFn, mxFn, myFn, "rebar");
   }

   (double kx, double ky, double k, int nIter, bool conv, Kurvature sp)? RebarPhase(
      double kx, double ky, double k,
      Kurvature sp,
      Func<double, double> nFn, Func<double, double> mxFn, Func<double, double> myFn,
      double dNdk, double dMxdk, double dMydk)
   {
      (double X, double Y, double EpsSu)? Gov(Kurvature plane)
      {
         (double X, double Y, double EpsSu)? best = null;
         double bestRatio = 0;
         foreach (var rb in _rebarLimits)
         {
            double epsR = Eps(plane, rb.X, rb.Y);
            if (epsR > rb.EpsSu)
            {
               double ratio = epsR / rb.EpsSu;
               if (ratio > bestRatio)
               {
                  bestRatio = ratio;
                  best = rb;
               }
            }
         }
         return best;
      }

      var gov = Gov(sp);
      if (gov is null)
         return null;

      int totalIter = 0;
      var spCur = sp;
      double kxCur = kx, kyCur = ky, kCur = k;

      for (int outer = 0; outer < 3; outer++)
      {
         var (xR, yR, epsSu) = gov.Value;
         double epsR = Eps(spCur, xR, yR);
         double lam = Math.Abs(epsR) > 1e-15 ? epsSu / epsR : 0.5;
         lam = Math.Clamp(lam, 0.01, 2.0);

         var res = Newton(xR, yR, kxCur * lam, kyCur * lam, Math.Max(kCur * lam, 1e-3),
            nFn, mxFn, myFn, dNdk, dMxdk, dMydk, epsSu);
         totalIter += res.nIter;
         if (!res.conv)
            return (kxCur * lam, kyCur * lam, Math.Max(kCur * lam, 1e-3), totalIter, false, spCur);

         kxCur = res.kx; kyCur = res.ky; kCur = res.k; spCur = res.sp;
         gov = Gov(spCur);
         if (gov is null)
            break;
      }

      return (kxCur, kyCur, kCur, totalIter, true, spCur);
   }

   (double kx, double ky, double k, int nIter, bool conv, Kurvature sp) Newton(
      double xA, double yA,
      double kx0, double ky0, double k0,
      Func<double, double> nFn, Func<double, double> mxFn, Func<double, double> myFn,
      double dNdk, double dMxdk, double dMydk,
      double epsPin)
   {
      double yr = _yRef, xr = _xRef, ke = Math.Max(Math.Abs(k0), 1e-6);
      double Kx = kx0 * yr, Ky = ky0 * xr, K = k0 / ke;
      double h = _hDiff;
      Kurvature sp = MakeSp(kx0, ky0, xA, yA, epsPin);

      for (int nIter = 1; nIter <= _maxIter; nIter++)
      {
         Unscale(Kx, Ky, K, yr, xr, ke, out double kx, out double ky, out double k);
         sp = MakeSp(kx, ky, xA, yA, epsPin);
         var f0 = Forces(sp);
         double g0 = f0.N - nFn(k);
         double g1 = f0.Mx - mxFn(k);
         double g2 = f0.My - myFn(k);
         double norm = Math.Sqrt(g0 * g0 + g1 * g1 + g2 * g2);
         if (norm <= ResidualTol(k, nFn, mxFn, myFn))
            return (kx, ky, k, nIter, true, sp);

         double hKx = Kx != 0 ? Math.Max(h, Math.Abs(Kx) * 1e-4) : h;
         double hKy = Ky != 0 ? Math.Max(h, Math.Abs(Ky) * 1e-4) : h;

         Unscale(Kx + hKx, Ky, K, yr, xr, ke, out double kxH, out double kyH, out _);
         var fKx = Forces(MakeSp(kxH, kyH, xA, yA, epsPin));
         Unscale(Kx, Ky + hKy, K, yr, xr, ke, out kxH, out kyH, out _);
         var fKy = Forces(MakeSp(kxH, kyH, xA, yA, epsPin));
         double[,] j = new double[3, 3]
         {
            { (fKx.N - f0.N) / hKx, (fKy.N - f0.N) / hKy, -dNdk * ke },
            { (fKx.Mx - f0.Mx) / hKx, (fKy.Mx - f0.Mx) / hKy, -dMxdk * ke },
            { (fKx.My - f0.My) / hKx, (fKy.My - f0.My) / hKy, -dMydk * ke },
         };

         if (!GaussSolve(j, [-g0, -g1, -g2], out double[] delta))
         {
            for (int i = 0; i < 3; i++) j[i, i] += 1e-4;
            if (!GaussSolve(j, [-g0, -g1, -g2], out delta))
               return (kx, ky, k, nIter, false, sp);
         }

         double alpha = 1.0;
         for (int ls = 0; ls < 8; ls++)
         {
            double Ktry = K + alpha * delta[2];
            if (Ktry <= 0) { alpha *= 0.5; continue; }
            Unscale(Kx + alpha * delta[0], Ky + alpha * delta[1], Ktry, yr, xr, ke,
               out double kxn, out double kyn, out double kn);
            var fn = Forces(MakeSp(kxn, kyn, xA, yA, epsPin));
            double normNew = Math.Sqrt(
               Math.Pow(fn.N - nFn(kn), 2) +
               Math.Pow(fn.Mx - mxFn(kn), 2) +
               Math.Pow(fn.My - myFn(kn), 2));
            if (normNew < norm)
               break;
            alpha *= 0.5;
         }

         Kx += alpha * delta[0];
         Ky += alpha * delta[1];
         K  += alpha * delta[2];
      }

      Unscale(Kx, Ky, K, yr, xr, ke, out double kxf, out double kyf, out double kf);
      sp = MakeSp(kxf, kyf, xA, yA, epsPin);
      return (kxf, kyf, kf, _maxIter, false, sp);
   }

   LimitForceResult? SolveSingleDriver(
      double n, double mx, double my,
      double xA, double yA, double kx0, double ky0,
      Func<double, double> nFn, Func<double, double> mxFn, Func<double, double> myFn,
      double dNdk, double dMxdk, double dMydk)
   {
      bool varyKy = Math.Abs(mx) >= Math.Abs(my);
      double yr = _yRef, xr = _xRef;
      double KxFixed = kx0 * yr;
      double Ky = ky0 * xr;
      double h = _hDiff;
      int nIter = 0;

      double Secondary(Kurvature sp)
      {
         var f = Forces(sp);
         if (Math.Abs(dNdk) < 1e-30 && Math.Abs(n) < 1e-30)
            return f.N;
         if (Math.Abs(dMydk) < 1e-30 && Math.Abs(my) < 1e-30)
            return f.My;
         if (Math.Abs(dMxdk) < 1e-30 && Math.Abs(mx) < 1e-30)
            return f.Mx;
         return 0;
      }

      double Driver(Kurvature sp)
      {
         var f = Forces(sp);
         if (Math.Abs(dMxdk) > 1e-30) return f.Mx;
         if (Math.Abs(dMydk) > 1e-30) return f.My;
         if (Math.Abs(dNdk) > 1e-30) return f.N;
         return 0;
      }

      double DriverDk()
      {
         if (Math.Abs(dMxdk) > 1e-30) return dMxdk;
         if (Math.Abs(dMydk) > 1e-30) return dMydk;
         return dNdk;
      }

      for (nIter = 1; nIter <= _maxIter; nIter++)
      {
         double kx = KxFixed / yr, ky = Ky / xr;
         var sp = MakeSp(kx, ky, xA, yA, _epsCu);
         double g = Secondary(sp);
         if (Math.Abs(g) <= _tol)
         {
            double dK = DriverDk();
            double k = Driver(sp) / dK;
            if (k > 0 && double.IsFinite(k) && IsValidSolution(kx, ky, k, sp, nFn, mxFn, myFn))
               return BuildResult(k, nIter, nIter, sp, nFn, mxFn, myFn, "concrete");
            return null;
         }

         double hVar = varyKy
            ? (Ky != 0 ? Math.Max(h, Math.Abs(Ky) * 1e-4) : h)
            : (KxFixed != 0 ? Math.Max(h, Math.Abs(KxFixed) * 1e-4) : h);

         Kurvature spH;
         if (varyKy)
         {
            double kxH = KxFixed / yr, kyH = (Ky + hVar) / xr;
            spH = MakeSp(kxH, kyH, xA, yA, _epsCu);
         }
         else
         {
            double kxH = (KxFixed + hVar) / yr, kyH = Ky / xr;
            spH = MakeSp(kxH, kyH, xA, yA, _epsCu);
         }

         double dg = (Secondary(spH) - g) / hVar;
         if (Math.Abs(dg) < 1e-30)
            break;

         double step = -g / dg;
         if (varyKy) Ky += step;
         else KxFixed += step;
      }

      return null;
   }

   bool TryNewton2d(
      double xA, double yA,
      double kx0, double ky0, double k0,
      double n, double mx, double my,
      Func<double, double> nFn, Func<double, double> mxFn, Func<double, double> myFn,
      double dNdk, double dMxdk, double dMydk,
      out double kx, out double ky, out double k, out int nIter, out bool conv, out Kurvature spFinal)
   {
      kx = kx0; ky = ky0; k = k0; nIter = 0; conv = false;
      spFinal = MakeSp(kx0, ky0, xA, yA, _epsCu);

      double mxR = Math.Abs(mx), myR = Math.Abs(my);

      if (mxR >= myR)
      {
         var r = Newton2d(xA, yA, kx0, ky0, k0, nFn, mxFn, myFn, dNdk, dMxdk, dMydk, fixKy: false);
         if (r.conv) { kx = kx0; ky = r.ky; k = r.k; nIter = r.nIter; conv = true; spFinal = r.sp; }
      }
      else
      {
         var r = Newton2d(xA, yA, kx0, ky0, k0, nFn, mxFn, myFn, dNdk, dMxdk, dMydk, fixKy: true);
         if (r.conv) { kx = r.kx; ky = 0; k = r.k; nIter = r.nIter; conv = true; spFinal = r.sp; }
      }

      if (!conv)
         return false;

      conv = IsValidSolution(kx, ky, k, spFinal, nFn, mxFn, myFn);
      return conv;
   }

   (double kx, double ky, double k, int nIter, bool conv, Kurvature sp) Newton2d(
      double xA, double yA,
      double kx0, double ky0, double k0,
      Func<double, double> nFn, Func<double, double> mxFn, Func<double, double> myFn,
      double dNdk, double dMxdk, double dMydk,
      bool fixKy)
   {
      double yr = _yRef, xr = _xRef, ke = Math.Max(Math.Abs(k0), 1e-6);
      double h = _hDiff;
      double KyFixed = ky0 * xr;
      double KxFixed = kx0 * yr;
      double Kx = fixKy ? kx0 * yr : KxFixed;
      double Ky = fixKy ? KyFixed : ky0 * xr;
      double K = k0 / ke;
      Kurvature sp = MakeSp(kx0, ky0, xA, yA, _epsCu);

      for (int nIter = 1; nIter <= _maxIter; nIter++)
      {
         double kx, ky, k;
         if (fixKy)
            Unscale(Kx, KyFixed, K, yr, xr, ke, out kx, out ky, out k);
         else
            Unscale(KxFixed, Ky, K, yr, xr, ke, out kx, out ky, out k);

         sp = MakeSp(kx, ky, xA, yA, _epsCu);
         var f0 = Forces(sp);
         double g0 = f0.N - nFn(k);
         double g1 = f0.Mx - mxFn(k);
         double g2 = f0.My - myFn(k);
         double norm = Math.Sqrt(g0 * g0 + g1 * g1 + g2 * g2);
         if (norm <= ResidualTol(k, nFn, mxFn, myFn))
            return (kx, ky, k, nIter, true, sp);

         double hVar;
         double[] gVec = [g0, g1, g2];
         double[] fVec = [f0.N, f0.Mx, f0.My];
         double[] dkVec = [dNdk, dMxdk, dMydk];

         if (fixKy)
         {
            hVar = Kx != 0 ? Math.Max(h, Math.Abs(Kx) * 1e-4) : h;
            Unscale(Kx + hVar, KyFixed, K, yr, xr, ke, out double kxH, out double kyH, out _);
            var fh = Forces(MakeSp(kxH, kyH, xA, yA, _epsCu));
            double[] fhVec = [fh.N, fh.Mx, fh.My];
            var order = new[] { 0, 1, 2 }.OrderByDescending(i => Math.Abs(gVec[i])).ToArray();
            int i0 = order[0];
            int i1 = order.Skip(1).OrderByDescending(i => Math.Abs(dkVec[i])).ThenByDescending(i => Math.Abs(gVec[i])).First();
            double dfDvar = (fhVec[i0] - fVec[i0]) / hVar;
            double dgDvar = (fhVec[i1] - fVec[i1]) / hVar;
            double d0 = dkVec[i0], d1 = dkVec[i1];
            double J11 = dfDvar, J12 = -d0 * ke;
            double J21 = dgDvar, J22 = -d1 * ke;
            double det = J11 * J22 - J21 * J12;
            if (Math.Abs(det) < 1e-30) break;
            double dVar = (J22 * (-gVec[i0]) - J12 * (-gVec[i1])) / det;
            double dK = (J11 * (-gVec[i1]) - J21 * (-gVec[i0])) / det;

            double alpha = 1.0;
            for (int ls = 0; ls < 8; ls++)
            {
               double Ktry = K + alpha * dK;
               if (Ktry <= 0) { alpha *= 0.5; continue; }
               Unscale(Kx + alpha * dVar, KyFixed, Ktry, yr, xr, ke, out double kxn, out double kyn, out double kn);
               var fn = Forces(MakeSp(kxn, kyn, xA, yA, _epsCu));
               double normNew = Math.Sqrt(
                  Math.Pow(fn.N - nFn(kn), 2) +
                  Math.Pow(fn.Mx - mxFn(kn), 2) +
                  Math.Pow(fn.My - myFn(kn), 2));
               if (normNew < norm) break;
               alpha *= 0.5;
            }
            Kx += alpha * dVar;
            K += alpha * dK;
         }
         else
         {
            hVar = Ky != 0 ? Math.Max(h, Math.Abs(Ky) * 1e-4) : h;
            Unscale(KxFixed, Ky + hVar, K, yr, xr, ke, out double kxH, out double kyH, out _);
            var fh = Forces(MakeSp(kxH, kyH, xA, yA, _epsCu));
            double[] fhVec = [fh.N, fh.Mx, fh.My];
            var order = new[] { 0, 1, 2 }.OrderByDescending(i => Math.Abs(gVec[i])).ToArray();
            int i0 = order[0];
            int i1 = order.Skip(1).OrderByDescending(i => Math.Abs(dkVec[i])).ThenByDescending(i => Math.Abs(gVec[i])).First();
            double dfDvar = (fhVec[i0] - fVec[i0]) / hVar;
            double dgDvar = (fhVec[i1] - fVec[i1]) / hVar;
            double d0 = dkVec[i0], d1 = dkVec[i1];
            double J11 = dfDvar, J12 = -d0 * ke;
            double J21 = dgDvar, J22 = -d1 * ke;
            double det = J11 * J22 - J21 * J12;
            if (Math.Abs(det) < 1e-30) break;
            double dVar = (J22 * (-gVec[i0]) - J12 * (-gVec[i1])) / det;
            double dK = (J11 * (-gVec[i1]) - J21 * (-gVec[i0])) / det;

            double alpha = 1.0;
            for (int ls = 0; ls < 8; ls++)
            {
               double Ktry = K + alpha * dK;
               if (Ktry <= 0) { alpha *= 0.5; continue; }
               Unscale(KxFixed, Ky + alpha * dVar, Ktry, yr, xr, ke, out double kxn, out double kyn, out double kn);
               var fn = Forces(MakeSp(kxn, kyn, xA, yA, _epsCu));
               double normNew = Math.Sqrt(
                  Math.Pow(fn.N - nFn(kn), 2) +
                  Math.Pow(fn.Mx - mxFn(kn), 2) +
                  Math.Pow(fn.My - myFn(kn), 2));
               if (normNew < norm) break;
               alpha *= 0.5;
            }
            Ky += alpha * dVar;
            K += alpha * dK;
         }
      }

      double kxf, kyf, kf;
      if (fixKy)
         Unscale(Kx, KyFixed, K, yr, xr, ke, out kxf, out kyf, out kf);
      else
         Unscale(KxFixed, Ky, K, yr, xr, ke, out kxf, out kyf, out kf);
      sp = MakeSp(kxf, kyf, xA, yA, _epsCu);
      return (kxf, kyf, kf, _maxIter, false, sp);
   }

   LimitForceResult BisectFallback(double n, double mx, double my, SolveMode mode)
   {
      var bisect = LimitForceSolver.ForCrossSection(_section, _calc,
         solverTol: _solverTol, solverMaxIter: _solverMaxIter,
         bisectTol: _bisectTol, bisectMaxIter: _bisectMaxIter);
      return mode switch
      {
         SolveMode.Moment => bisect.MomentFactor(n, mx, my),
         SolveMode.Axial  => bisect.AxialFactor(n, mx, my),
         _                => bisect.AllFactor(n, mx, my),
      };
   }

   LimitForceResult BuildResult(
      double k, int iterations, int newtonIter, Kurvature sp,
      Func<double, double> nFn, Func<double, double> mxFn, Func<double, double> myFn,
      string governing)
   {
      double epsContourMin = _contourPts.Min(p => Eps(sp, p.X, p.Y));
      double? epsRebarMax = _rebarLimits.Count > 0
         ? _rebarLimits.Max(p => Eps(sp, p.X, p.Y))
         : null;
      double? epsSu = _rebarLimits.Count > 0
         ? _rebarLimits.Max(p => p.EpsSu)
         : null;

      return new LimitForceResult
      {
         Factor = k,
         Utilization = k > 1e-15 ? 1.0 / k : double.PositiveInfinity,
         Converged = true,
         Iterations = iterations,
         NewtonIterations = newtonIter,
         StrainPlane = sp,
         NLimit = nFn(k),
         MxLimit = mxFn(k),
         MyLimit = myFn(k),
         EpsContourMin = epsContourMin,
         EpsCu = _epsCu,
         EpsRebarMax = epsRebarMax,
         EpsSu = epsSu,
         Governing = governing
      };
   }

   Kurvature MakeSp(double kx, double ky, double xA, double yA, double epsPin)
      => new() { e0 = epsPin - ky * yA - kx * xA, ky = ky, kz = kx };

   (double N, double Mx, double My) Forces(Kurvature sp)
   {
      var r = _section.Compute(sp, _calc, computeStiffness: false);
      return (r.N, r.Mx, r.My);
   }

   static double Eps(Kurvature k, double x, double y) => k.e0 + k.ky * y + k.kz * x;

   static void Unscale(double Kx, double Ky, double K, double yr, double xr, double ke,
      out double kx, out double ky, out double k)
   {
      kx = Kx / yr;
      ky = Ky / xr;
      k = K * ke;
   }

   static bool GaussSolve(double[,] a, double[] b, out double[] x)
   {
      x = new double[3];
      double[,] m = (double[,])a.Clone();
      double[] v = (double[])b.Clone();
      const int n = 3;

      for (int col = 0; col < n; col++)
      {
         int pivot = col;
         for (int row = col + 1; row < n; row++)
            if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col]))
               pivot = row;

         double pivVal = m[pivot, col];
         if (!double.IsFinite(pivVal) || Math.Abs(pivVal) < 1e-15)
            return false;

         if (pivot != col)
         {
            for (int k2 = 0; k2 < n; k2++)
               (m[col, k2], m[pivot, k2]) = (m[pivot, k2], m[col, k2]);
            (v[col], v[pivot]) = (v[pivot], v[col]);
         }

         for (int row = col + 1; row < n; row++)
         {
            double factor = m[row, col] / m[col, col];
            for (int k2 = col; k2 < n; k2++)
               m[row, k2] -= factor * m[col, k2];
            v[row] -= factor * v[col];
         }
      }

      for (int row = n - 1; row >= 0; row--)
      {
         double sum = v[row];
         for (int k2 = row + 1; k2 < n; k2++)
            sum -= m[row, k2] * x[k2];
         x[row] = sum / m[row, row];
      }

      return true;
   }
}
