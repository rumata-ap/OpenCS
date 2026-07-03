namespace CScore;

/// <summary>
/// Решатель плоскости деформаций для произвольного <see cref="ILimitSection"/>.
/// Начальное приближение берётся из геометрического сечения <see cref="CrossSection.Guess"/>.
/// </summary>
public sealed class LimitSectionStrainSolver
{
   public bool Converged { get; private set; }
   public int Iterations { get; private set; }
   public double Residual { get; private set; }

   readonly ILimitSection _section;
   readonly CrossSection _guessSection;
   readonly CalcType _calc;
   readonly double _tol;
   readonly int _maxIter;
   readonly double _h;

   public LimitSectionStrainSolver(
      ILimitSection section,
      CrossSection guessSection,
      CalcType calc = CalcType.C,
      double tol = 0.5,
      int maxIter = 60,
      double h = 1e-7)
   {
      _section = section ?? throw new ArgumentNullException(nameof(section));
      _guessSection = guessSection ?? throw new ArgumentNullException(nameof(guessSection));
      _calc = calc;
      _tol = tol;
      _maxIter = maxIter;
      _h = h;
   }

   /// <summary>Находит плоскость деформаций при заданных усилиях.</summary>
   public Kurvature Solve(double nTarget, double mxTarget, double myTarget)
   {
      Kurvature k = _guessSection.Guess(new Load { N = nTarget, Mx = mxTarget, My = myTarget });
      if (!double.IsFinite(k.e0)) k.e0 = 0;
      if (!double.IsFinite(k.ky)) k.ky = 0;
      if (!double.IsFinite(k.kz)) k.kz = 0;

      for (int iter = 0; iter < _maxIter; iter++)
      {
         var f0 = _section.Integral(k, _calc);
         double r0 = f0.N - nTarget;
         double r1 = f0.Mx - mxTarget;
         double r2 = f0.My - myTarget;

         Residual = Math.Sqrt(r0 * r0 + r1 * r1 + r2 * r2);
         Iterations = iter + 1;

         if (Residual < _tol)
         {
            Converged = true;
            break;
         }

         double[,] j = new double[3, 3];
         var axes = new[]
         {
            new Kurvature { e0 = _h },
            new Kurvature { ky = _h },
            new Kurvature { kz = _h },
         };
         for (int col = 0; col < 3; col++)
         {
            var fp = _section.Integral(k + axes[col], _calc);
            var fm = _section.Integral(k - axes[col], _calc);
            j[0, col] = (fp.N - fm.N) / (2 * _h);
            j[1, col] = (fp.Mx - fm.Mx) / (2 * _h);
            j[2, col] = (fp.My - fm.My) / (2 * _h);
         }

         double[] rhs = [r0, r1, r2];
         if (!GaussSolve(j, rhs, out double[] dk))
            break;

         k.e0 -= dk[0];
         k.ky -= dk[1];
         k.kz -= dk[2];
      }

      return k;
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
