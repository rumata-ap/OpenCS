using System;
using System.Collections.Generic;

namespace CScore
{
   /// <summary>Результат итерационного поиска НДС оболочечного сечения.</summary>
   public class ShellStrainSolverResult
   {
      /// <summary>Найденное деформационное состояние ε*.</summary>
      public ShellStrainState StrainState { get; set; } = ShellStrainState.Zero;
      /// <summary>Результирующие усилия R(ε*) с детализацией и жёсткостями.</summary>
      public ShellResult Forces { get; set; } = new();
      /// <summary>Сходимость достигнута в пределах допусков.</summary>
      public bool Converged { get; set; }
      /// <summary>Число выполненных итераций.</summary>
      public int Iterations { get; set; }
      /// <summary>Норма невязки ‖R(ε*) − S‖ на последней итерации.</summary>
      public double Residual { get; set; }
   }

   /// <summary>
   /// Итерационный поиск НДС пластины (Ньютон–Рафсон 6×6 с численным якобианом).
   /// Находит ε* = [ε₀x, ε₀y, γ₀xy, κx, κy, κxy] при заданных усилиях
   /// S = [Nx, Ny, Nxy, Mx, My, Mxy]. Порт GreenSectionPy ShellSolver.
   /// </summary>
   public class ShellStrainSolver
   {
      readonly PlateSection _section;
      readonly Diagramm _cDiag;
      readonly Diagramm _rDiag;
      readonly IReadOnlyList<Diagramm?>? _layerDiags;
      readonly double _tolRes;
      readonly double _tolStep;
      readonly int _maxIter;
      readonly double _hDiff;
      readonly bool _central;

      public ShellStrainSolver(PlateSection section, Diagramm cDiag, Diagramm rDiag,
         IReadOnlyList<Diagramm?>? layerDiags = null,
         double tolRes = 1e-3, double tolStep = 1e-10, int maxIter = 50,
         double hDiff = 1e-7, bool centralJacobian = false)
      {
         _section = section; _cDiag = cDiag; _rDiag = rDiag; _layerDiags = layerDiags;
         _tolRes = tolRes; _tolStep = tolStep; _maxIter = maxIter;
         _hDiff = hDiff; _central = centralJacobian;
      }

      /// <summary>Вектор усилий R(ε) = [Nx,Ny,Nxy,Mx,My,Mxy].</summary>
      double[] Eval(double[] e, out ShellResult res)
      {
         var state = ShellStrainState.FromArray(e);
         res = _section.Compute(state, _cDiag, _rDiag, _layerDiags, computeStiffness: false);
         return new[] { res.Nx, res.Ny, res.Nxy, res.Mx, res.My, res.Mxy };
      }

      /// <summary>Упругая оценка НДС по целевым усилиям (E=30 ГПа): для верного знака деформаций.</summary>
      double[] ElasticGuess(double[] s)
      {
         double h = Math.Max(_section.H, 1e-6);
         double E = 30_000_000.0;       // кПа
         double EA = E * h;             // кН/м
         double EI = E * h * h * h / 12.0;
         return new[]
         {
            EA != 0 ? s[0] / EA : 0.0,
            EA != 0 ? s[1] / EA : 0.0,
            0.0,
            EI != 0 ? s[3] / EI : 0.0,
            EI != 0 ? s[4] / EI : 0.0,
            0.0,
         };
      }

      /// <summary>Найти НДС при заданных усилиях target [Nx,Ny,Nxy,Mx,My,Mxy].</summary>
      public ShellStrainSolverResult Solve(double[] target, double[]? guess = null)
      {
         double[] S = (double[])target.Clone();
         double[] e = guess != null ? (double[])guess.Clone() : ElasticGuess(S);

         ShellResult shellRes = new();
         for (int it = 1; it <= _maxIter; it++)
         {
            double[] Rv = Eval(e, out shellRes);
            double[] f = Sub(Rv, S);
            double res = Norm(f);

            if (res < _tolRes)
               return Finalize(e, true, it, res);

            // Численный якобиан 6×6
            double[,] J = new double[6, 6];
            for (int j = 0; j < 6; j++)
            {
               double hj = _hDiff * Math.Max(1.0, Math.Abs(e[j]));
               double[] ep = (double[])e.Clone(); ep[j] += hj;
               double[] Rp = Eval(ep, out _);
               if (_central)
               {
                  double[] em = (double[])e.Clone(); em[j] -= hj;
                  double[] Rm = Eval(em, out _);
                  for (int i = 0; i < 6; i++) J[i, j] = (Rp[i] - Rm[i]) / (2.0 * hj);
               }
               else
               {
                  for (int i = 0; i < 6; i++) J[i, j] = (Rp[i] - Rv[i]) / hj;
               }
            }

            // Шаг Ньютона J·Δe = −f; при вырождении — регуляризация Тихонова
            double[] negF = Neg(f);
            if (!Solve6x6(J, negF, out double[] de))
               de = TikhonovStep(J, f, 1e-6);

            // Backtracking line search
            double alpha = 1.0;
            const double minAlpha = 1.0 / (1 << 20);
            while (true)
            {
               double[] eTry = AddScaled(e, de, alpha);
               double[] RvTry = Eval(eTry, out _);
               if (Norm(Sub(RvTry, S)) < res) break;
               alpha *= 0.5;
               if (alpha < minAlpha) break;
            }
            e = AddScaled(e, de, alpha);

            if (Norm(de) * alpha < _tolStep)
            {
               double[] Rf = Eval(e, out shellRes);
               double rf = Norm(Sub(Rf, S));
               return Finalize(e, rf < _tolRes, it, rf);
            }
         }

         double[] Rend = Eval(e, out shellRes);
         double rend = Norm(Sub(Rend, S));
         return Finalize(e, false, _maxIter, rend);
      }

      /// <summary>
      /// Решить список наборов усилий. Каждый сошедшийся результат — начальное
      /// приближение для следующего (тёплый старт). Последовательный режим.
      /// </summary>
      public IReadOnlyList<ShellStrainSolverResult> SolveMany(
         IEnumerable<double[]> targets, double[]? guess = null)
      {
         var results = new List<ShellStrainSolverResult>();
         double[]? cur = guess;
         foreach (var t in targets)
         {
            var r = Solve(t, cur);
            results.Add(r);
            if (r.Converged) cur = r.StrainState.ToArray();
         }
         return results;
      }

      ShellStrainSolverResult Finalize(double[] e, bool conv, int it, double res)
      {
         var state = ShellStrainState.FromArray(e);
         var full = _section.Compute(state, _cDiag, _rDiag, _layerDiags, computeStiffness: true);
         return new ShellStrainSolverResult
         {
            StrainState = state, Forces = full,
            Converged = conv, Iterations = it, Residual = res,
         };
      }

      // ── Линейная алгебра ────────────────────────────────────────────────────

      // Гаусс 6×6 с частичным выбором ведущего элемента. false при сингулярности.
      static bool Solve6x6(double[,] a, double[] b, out double[] x)
      {
         const int n = 6;
         x = new double[n];
         double[,] m = (double[,])a.Clone();
         double[] v = (double[])b.Clone();
         for (int col = 0; col < n; col++)
         {
            int piv = col;
            for (int r = col + 1; r < n; r++)
               if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col])) piv = r;
            double pv = m[piv, col];
            if (!double.IsFinite(pv) || Math.Abs(pv) < 1e-15) return false;
            if (piv != col)
            {
               for (int k = 0; k < n; k++) (m[col, k], m[piv, k]) = (m[piv, k], m[col, k]);
               (v[col], v[piv]) = (v[piv], v[col]);
            }
            for (int r = col + 1; r < n; r++)
            {
               double fct = m[r, col] / m[col, col];
               for (int k = col; k < n; k++) m[r, k] -= fct * m[col, k];
               v[r] -= fct * v[col];
            }
         }
         for (int r = n - 1; r >= 0; r--)
         {
            double s = v[r];
            for (int k = r + 1; k < n; k++) s -= m[r, k] * x[k];
            x[r] = s / m[r, r];
         }
         return true;
      }

      // Регуляризованный МНК: (JᵀJ + λI)·Δe = −Jᵀf. При неудаче — нулевой шаг.
      static double[] TikhonovStep(double[,] J, double[] f, double lam)
      {
         const int n = 6;
         double[,] jtj = new double[n, n];
         double[] jtf = new double[n];
         for (int i = 0; i < n; i++)
         {
            for (int j = 0; j < n; j++)
            {
               double s = 0;
               for (int k = 0; k < n; k++) s += J[k, i] * J[k, j];
               jtj[i, j] = s + (i == j ? lam : 0.0);
            }
            double sf = 0;
            for (int k = 0; k < n; k++) sf += J[k, i] * f[k];
            jtf[i] = -sf;
         }
         return Solve6x6(jtj, jtf, out double[] de) ? de : new double[n];
      }

      static double[] Sub(double[] a, double[] b)
      { var r = new double[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = a[i] - b[i]; return r; }
      static double[] Neg(double[] a)
      { var r = new double[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = -a[i]; return r; }
      static double[] AddScaled(double[] a, double[] d, double s)
      { var r = new double[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = a[i] + s * d[i]; return r; }
      static double Norm(double[] a)
      { double s = 0; foreach (double x in a) s += x * x; return Math.Sqrt(s); }
   }
}
