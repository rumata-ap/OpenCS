
namespace CSmath
{
   [Serializable]
   public class LSpline : ISpline
   {
      public double[] X { get; set; }
      public double[] Y { get; set; }
      public double[] DY { get; set; } = null!;
      public double[] A { get; set; } = null!;
      public double[] B { get; set; } = null!;
      public double[] C { get; set; } = null!;
      public double[] D { get; set; } = null!;

      public LSpline(double[] x, double[] y)
      {
         if (x.Length != y.Length)
            throw new ArgumentException("Длины массивов x и y должны совпадать.");

         if (x.Length < 2)
            throw new ArgumentException("Для линейной интерполяции требуется как минимум 2 точки.");

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

         ComputeSplineCoefficients();
      }

      /// <summary>
      /// Вычисление коэффициентов уравнения сплайна.
      /// </summary>
      void ComputeSplineCoefficients()
      {
         int n = X.Length;
         A = new double[n];
         B = new double[n];
         DY = new double[n];
         for (int i = 1; i < n - 1; i++)
         {
            DY[i] = (Y[i + 1] - Y[i - 1]) / (X[i + 1] - X[i -1]);
         }
         for (int i = 0; i < n - 1; i++)
         {
            A[i] = Y[i];
            B[i] = (Y[i + 1] - Y[i]) / (X[i + 1] - X[i]);
         }
         A[n - 1] = Y[n - 1];
         B[n - 1] = B[n - 2];
         DY[0] = B[0]; DY[n - 1] = B[n - 2];
      }

      public double Interpolate(double value)
      {
         int i = 0;
         int n = X.Length;
         // Если значение меньше минимального x — экстраполяция влево
         if (value < X[0])
         {
            return A[0] + B[0] * (value - X[0]);
         }

         // Если значение больше максимального x — экстраполяция вправо
         else if (value > X[X.Length - 1])
         {
            int last = X.Length - 1;
            return A[last] + B[last] * (value - X[last]);
         }
         else
         {
            // Найти соответствующий интервал для интерполяции
            while (i < n - 1 && value > X[i + 1]) i++;
         }
       
         return A[i] + B[i] * (value - X[i]);
      }

      public double Derivative(double value, out double interp_func)
      {
         int i = 0;
         int n = X.Length;
         // Если значение меньше минимального x — экстраполяция влево
         if (value < X[0])
         {
            interp_func = A[0] + B[0] * (value - X[0]);
            return B[0];
         }

         // Если значение больше максимального x — экстраполяция вправо
         else if (value > X[X.Length - 1])
         {
            int last = X.Length - 1;
            interp_func = A[last] + B[last] * (value - X[last]);
            return B[last];
         }
         else
         {
            // Найти соответствующий интервал для интерполяции
            while (i < n - 1 && value >= X[i + 1]) i++;
         }

         interp_func = A[i] + B[i] * (value - X[i]);
         return B[i];

      }
   }
}
