namespace CScore;

/// <summary>Решение вырожденной системы Ньютона 3×3 по осям, у которых есть жёсткость.</summary>
internal static class ReducedJacobianSolve
{
   /// <summary>
   /// Ньютон по невырожденным степеням свободы, когда полный якобиан вырожден из-за нулевых
   /// строки и столбца: сечение без жёсткости по одной оси (бетонный прямоугольник сеткой nx = 1 —
   /// все волокна на x = 0, My ≡ 0 при любом kz). Раньше такой случай уходил в запасной шаг
   /// 1e-6·невязка, и плоскость находилась только для крошечных моментов (бетон 300×500 давал
   /// Mx_lim = 0,37 кН·м вместо ≈ 18 кН·м). Ось без жёсткости не двигается.
   /// </summary>
   public static bool TrySolve(double[,] a, double[] b, out double[] x)
   {
      x = new double[3];
      double scale = 0;
      foreach (double v in a) scale = Math.Max(scale, Math.Abs(v));
      if (!(scale > 0) || !double.IsFinite(scale)) return false;
      double eps = scale * 1e-12;

      var keep = new List<int>(3);
      for (int i = 0; i < 3; i++)
      {
         bool rowZero = true, colZero = true;
         for (int c = 0; c < 3; c++)
         {
            if (Math.Abs(a[i, c]) > eps) rowZero = false;
            if (Math.Abs(a[c, i]) > eps) colZero = false;
         }
         if (!(rowZero && colZero)) keep.Add(i);
      }
      if (keep.Count is 0 or 3) return false;

      // Невязку по выброшенной оси уменьшенная система не устраняет: если она не нулевая
      // (момент вокруг оси без жёсткости), Ньютон не сойдётся, и точка останется недостижимой.
      int m = keep.Count;
      var ar = new double[m, m];
      var br = new double[m];
      for (int r = 0; r < m; r++)
      {
         br[r] = b[keep[r]];
         for (int c = 0; c < m; c++) ar[r, c] = a[keep[r], keep[c]];
      }

      if (!SolveSmall(ar, br, out var xr)) return false;
      for (int r = 0; r < m; r++) x[keep[r]] = xr[r];
      return true;
   }

   static bool SolveSmall(double[,] a, double[] b, out double[] x)
   {
      int n = b.Length;
      x = new double[n];
      if (n == 1)
      {
         if (!(Math.Abs(a[0, 0]) > 1e-15)) return false;
         x[0] = b[0] / a[0, 0];
         return double.IsFinite(x[0]);
      }
      double det = a[0, 0] * a[1, 1] - a[0, 1] * a[1, 0];
      if (!double.IsFinite(det) || Math.Abs(det) < 1e-30) return false;
      x[0] = (b[0] * a[1, 1] - a[0, 1] * b[1]) / det;
      x[1] = (a[0, 0] * b[1] - a[1, 0] * b[0]) / det;
      return double.IsFinite(x[0]) && double.IsFinite(x[1]);
   }
}
