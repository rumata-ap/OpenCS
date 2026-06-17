using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSmath
{
   public class CSpline : ISpline
   {
      public double[] X { get; set; }
      public double[] Y { get; set; }
      public double[] DY { get; set; } = null!;
      public double[] A { get; set; }
      public double[] B { get; set; }
      public double[] C { get; set; }
      public double[] D { get; set; }

      public CSpline(IEnumerable<double> x, IEnumerable<double> y)
      {
         if (x.Count() != y.Count())
            throw new ArgumentException("Длины массивов x и y должны совпадать.");
         if (x.Count() < 3)
            throw new ArgumentException("Массивы x и y должны содержать хотя бы 3 элемента.");

         double[] xa = x.ToArray();
         double[] ya = y.ToArray();

         // Сортировка входных данных по x
         var sorted = xa.Select((value, index) => new { x = value, y = ya[index] })
                       .OrderBy(point => point.x)
                       .ToArray();

         x = sorted.Select(point => point.x).ToArray();
         y = sorted.Select(point => point.y).ToArray();

         X = x.ToArray();
         Y = y.ToArray();

         int n = X.Length;
         A = new double[n - 1];
         B = new double[n - 1];
         C = new double[n];
         D = new double[n - 1];

         ComputeSplineCoefficients();
      }

      private void ComputeSplineCoefficients()
      {
         int n = X.Length - 1;
         double[] h = new double[n];
         for (int i = 0; i < n; i++)
            h[i] = X[i + 1] - X[i];

         double[] alpha = new double[n];
         for (int i = 1; i < n; i++)
         {
            alpha[i] = 3.0 / h[i] * (Y[i + 1] - Y[i]) - 3.0 / h[i - 1] * (Y[i] - Y[i - 1]);
         }

         double[] l = new double[n + 1];
         double[] mu = new double[n + 1];
         double[] z = new double[n + 1];

         l[0] = 1;
         z[0] = 0;
         mu[0] = 0;

         for (int i = 1; i < n; i++)
         {
            l[i] = 2 * (X[i + 1] - X[i - 1]) - h[i - 1] * mu[i - 1];
            mu[i] = h[i] / l[i];
            z[i] = (alpha[i] - h[i - 1] * z[i - 1]) / l[i];
         }

         l[n] = 1;
         z[n] = 0;

         for (int j = n - 1; j >= 0; j--)
         {
            C[j] = z[j] - mu[j] * C[j + 1];
            B[j] = (Y[j + 1] - Y[j]) / h[j] - h[j] * (C[j + 1] + 2 * C[j]) / 3.0;
            D[j] = (C[j + 1] - C[j]) / (3 * h[j]);
            A[j] = Y[j];
         }
      }

      public double Interpolate(double xi)
      {
         int n = X.Length;

         // Найти интервал, к которому принадлежит xi
         int i = Array.BinarySearch(X, xi);
         if (i < 0) i = ~i - 1;

         if (i < 0) i = 0;
         if (i >= n - 1) i = n - 2;

         double dx = xi - X[i];
         return A[i] + B[i] * dx + C[i] * dx * dx + D[i] * dx * dx * dx;
      }

      public double Derivative(double xi, out double interp_func)
      {
         int n = X.Length;

         // Найти интервал, к которому принадлежит xi
         int i = Array.BinarySearch(X, xi);
         if (i < 0) i = ~i - 1;

         if (i < 0) i = 0;
         if (i >= n - 1) i = n - 2;

         double dx = xi - X[i];
         interp_func = A[i] + B[i] * dx + C[i] * dx * dx + D[i] * dx * dx * dx;
         return B[i] + 2 * C[i] * xi + 3 * D[i] * xi * xi - 6 * D[i] * xi * X[i] - 2 * C[i] * X[i] + 3 * D[i] * X[i] * X[i]; ;
      }
   }
}
