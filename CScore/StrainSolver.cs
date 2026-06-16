using System;

namespace CScore
{
   /// <summary>
   /// Итерационный решатель плоскости деформаций методом Ньютона-Рафсона.
   /// Находит (e0, ky, kz) такую, что CrossSection.Integral(k) ≈ (N, My, Mz).
   /// </summary>
   public class StrainSolver
   {
      public bool   Converged  { get; private set; }
      public int    Iterations { get; private set; }
      public double Residual   { get; private set; }

      readonly CrossSection _section;
      readonly CalcType     _calc;
      readonly bool         _ten;
      readonly bool         _ca;
      readonly double       _tol;
      readonly int          _maxIter;

      public StrainSolver(CrossSection section, CalcType calc = CalcType.C,
                          bool ten = true, bool ca = true,
                          double tol = 0.5, int maxIter = 60)
      {
         _section = section;
         _calc    = calc;
         _ten     = ten;
         _ca      = ca;
         _tol     = tol;
         _maxIter = maxIter;
      }

      /// <summary>
      /// Решает обратную задачу: при заданных target-усилиях N/My/Mz (кН, кН·м)
      /// находит кривизну k = (e0, ky, kz). Возвращает найденную Kurvature.
      /// </summary>
      public Kurvature Solve(double nTarget, double mxTarget, double myTarget)
      {
         Kurvature k = _section.Guess(new Load { N = nTarget, Mx = mxTarget, My = myTarget });
         if (!double.IsFinite(k.e0)) k.e0 = 0;
         if (!double.IsFinite(k.ky)) k.ky = 0;
         if (!double.IsFinite(k.kz)) k.kz = 0;
         const double h = 1e-7;

         for (int iter = 0; iter < _maxIter; iter++)
         {
            var f0 = _section.Integral(k, _calc, _ten, _ca);
            double r0 = f0.N  - nTarget;
            double r1 = f0.Mx - mxTarget;
            double r2 = f0.My - myTarget;

            Residual = Math.Sqrt(r0 * r0 + r1 * r1 + r2 * r2);
            Iterations = iter + 1;

            if (Residual < _tol) { Converged = true; break; }

            // Числовой Якобиан 3×3 (центральные разности)
            double[,] J = new double[3, 3];
            var axes = new[]
            {
               new Kurvature { e0 = h },
               new Kurvature { ky = h },
               new Kurvature { kz = h },
            };
            for (int j = 0; j < 3; j++)
            {
               var fp = _section.Integral(k + axes[j], _calc, _ten, _ca);
               var fm = _section.Integral(k - axes[j], _calc, _ten, _ca);
               J[0, j] = (fp.N  - fm.N)  / (2 * h);
               J[1, j] = (fp.Mx - fm.Mx) / (2 * h);
               J[2, j] = (fp.My - fm.My) / (2 * h);
            }

            // Решение 3×3 системы J·Δk = r методом Гаусса
            double[] rhs = [r0, r1, r2];
            if (!GaussSolve(J, rhs, out double[] dk))
               break; // сингулярная матрица — выходим

            k.e0 -= dk[0];
            k.ky -= dk[1];
            k.kz -= dk[2];
         }

         return k;
      }

      // Метод Гаусса с выбором ведущего элемента. Возвращает false при сингулярности.
      static bool GaussSolve(double[,] a, double[] b, out double[] x)
      {
         x = new double[3];
         // Копия для работы
         double[,] m = (double[,])a.Clone();
         double[]  v = (double[])b.Clone();
         int n = 3;

         for (int col = 0; col < n; col++)
         {
            // Поиск ведущего элемента
            int pivot = col;
            for (int row = col + 1; row < n; row++)
               if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col]))
                  pivot = row;

            double pivVal = m[pivot, col];
            if (!double.IsFinite(pivVal) || Math.Abs(pivVal) < 1e-15)
               return false;

            // Перестановка строк
            if (pivot != col)
            {
               for (int k2 = 0; k2 < n; k2++)
                  (m[col, k2], m[pivot, k2]) = (m[pivot, k2], m[col, k2]);
               (v[col], v[pivot]) = (v[pivot], v[col]);
            }

            // Прямой ход
            for (int row = col + 1; row < n; row++)
            {
               double factor = m[row, col] / m[col, col];
               for (int k2 = col; k2 < n; k2++)
                  m[row, k2] -= factor * m[col, k2];
               v[row] -= factor * v[col];
            }
         }

         // Обратный ход
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
}
