
namespace CSmath
{
   /// <summary>
   /// Класс, реализующий сплайн Акимы.
   /// </summary>
   public class ASpline : ISpline
   {
      internal double a0;
      internal double b0;
      internal double c0;
      internal double an;
      internal double bn;
      internal double cn;
      internal List<double> xex;
      internal List<double> yex;
      internal List<double> m;

      public double[] X { get; set; }
      public double[] Y { get; set; }
      public double[] DY { get; set; }
      public double[] A { get; set; }
      public double[] B { get; set; }
      public double[] C { get; set; }
      public double[] D { get; set; }

      /// <summary>
      /// Конструктор класса сплайна Акимы.
      /// </summary>
      /// <param name="x">Массив узлов по оси X.</param>
      /// <param name="y">Массив узлов по оси Y.</param>
      public ASpline(IEnumerable<double> x, IEnumerable<double> y)
      {
         if (x.Count() != y.Count())
            throw new ArgumentException("Длины массивов x, y и dy должны совпадать.");
         if (x.Count() < 5)
            throw new ArgumentException("Массивы x и y должны содержать минимум 5 элементов.");

         double[] xa = x.ToArray();
         double[] ya = y.ToArray();

         // Сортировка входных данных по x
         var sorted = xa.Select((value, index) => new { x = value, y = ya[index] })
                       .OrderBy(point => point.x)
                       .ToArray();

         x = sorted.Select(point => point.x).ToArray();
         y = sorted.Select(point => point.y).ToArray();

         X = x.ToArray(); xex = x.ToList();
         Y = y.ToArray(); yex = y.ToList();
         A = Y; m = new List<double>();

         ComputeSplineCoefficients();
      }

      /// <summary>
      /// Вычисление коэффициентов экстраполяционных полиномов(парабол).
      /// </summary>
      void ComputeBC2()
      {
         int n = X.Length;

         double x1 = X[n - 3]; double y1 = Y[n - 3];
         double x2 = X[n - 2]; double y2 = Y[n - 2];
         double x3 = X[n - 1]; double y3 = Y[n - 1];

         double d_x = x3 - x1;
         double x4 = x2 + d_x; xex.Add(x4);
         double x5 = x3 + d_x; xex.Add(x5);

         double d_y = (y3 - y2) / (x3 - x2) - (y2 - y1) / (x2 - x1);
         double y4 = y2 + d_y * (x4 - x3) + (x4 - x2) * (y3 - y2) / (x3 - x2); yex.Add(y4);
         double y5 = y3 + d_y * (x5 - x4) + (x5 - x3) * (y4 - y3) / (x4 - x3); yex.Add(y5);

         Matrix k = new Matrix(3, 3);
         k[0, 0] = x3 * x3; k[0, 1] = x3; k[0, 2] = 1;
         k[1, 0] = x4 * x4; k[1, 1] = x4; k[1, 2] = 1;
         k[2, 0] = x5 * x5; k[2, 1] = x5; k[2, 2] = 1;

         Vector ye = new Vector(new double[] { y3, y4, y5 });
         Vector abc = k.Inverse() * ye;
         an = abc[0]; bn = abc[1]; cn = abc[2];

         x1 = X[2]; y1 = Y[2];
         x2 = X[1]; y2 = Y[1];
         x3 = X[0]; y3 = Y[0];

         d_x = x3 - x1;
         x4 = x2 + d_x; xex.Insert(0, x4);
         x5 = x3 + d_x; xex.Insert(0, x5);

         d_y = (y3 - y2) / (x3 - x2) - (y2 - y1) / (x2 - x1);
         y4 = y2 + d_y * (x4 - x3) + (x4 - x2) * (y3 - y2) / (x3 - x2); yex.Insert(0, y4);
         y5 = y3 + d_y * (x5 - x4) + (x5 - x3) * (y4 - y3) / (x4 - x3); yex.Insert(0, y5);

         k[0, 0] = x3 * x3; k[0, 1] = x3; k[0, 2] = 1;
         k[1, 0] = x4 * x4; k[1, 1] = x4; k[1, 2] = 1;
         k[2, 0] = x5 * x5; k[2, 1] = x5; k[2, 2] = 1;

         ye = new Vector(new double[] { y3, y4, y5 });
         abc = k.Inverse() * ye;
         a0 = abc[0]; b0 = abc[1]; c0 = abc[2];
      }

      /// <summary>
      /// Вычисление уклонов.
      /// </summary>
      void ComputeSlopes()
      {
         //уклоны отрезков
         for (int i = 0; i < xex.Count - 2; i++)
         {
            m.Add((yex[i + 1] - yex[i]) / (xex[i + 1] - xex[i]));
         }
         //уклоны в вершинах
         int n = m.Count; DY = new double[X.Length];
         for (int i = 2; i < n - 2; i++)
         {
            double ne = Math.Abs(m[i + 1] - m[i]) + Math.Abs(m[i - 1] - m[i - 2]);
            if (ne > 0)
            {
               DY[i - 2] = (Math.Abs(m[i + 1] - m[i]) * m[i - 1] + Math.Abs(m[i - 1] - m[i - 2]) * m[i]) / ne;
            }
            else
            {
               DY[i - 2] = 0.5 * (m[i + 1] - m[i]);
            }
         }
      }

      /// <summary>
      /// Вычисление коэффициентов уравнения сплайна.
      /// </summary>
      void ComputeSplineCoefficients()
      {
         ComputeBC2();
         ComputeSlopes();

         int n = X.Length - 1;
         A = new double[n];
         B = new double[n];
         C = new double[n];
         D = new double[n];
         for (int i = 0; i < n; i++)
         {
            A[i] = Y[i];
            B[i] = DY[i];
            C[i] = (3 * m[i + 2] - 2 * DY[i] - DY[i + 1]) / (X[i + 1] - X[i]);
            D[i] = (DY[i] + DY[i + 1] - 2 * m[i + 2]) / Math.Pow(X[i + 1] - X[i], 2);
         }
      }

      /// <summary>
      /// Выполняет интерполяцию сплайном Акимы.
      /// </summary>
      /// <param name="xi">Точка, в которой вычисляется значение сплайна.</param>
      /// <returns>Интерполированное значение в точке value.</returns>
      public double Interpolate(double xi)
      {
         // Поиск интервала, содержащего  value
         int n = X.Length;
         int i = 0;
         if (xi < X[0])
         {
            return a0 * xi * xi + b0 * xi + c0;
         }
         else if (xi >= X[n - 1])
         {
            return an * xi * xi + bn * xi + cn;
         }
         else
         {
            while (i < n - 1 && xi >= X[i + 1]) i++;
         }

         double dx = xi - X[i];
         return A[i] + B[i] * dx + C[i] * dx * dx + D[i] * dx * dx * dx;
      }

      /// <summary>
      /// Выполняет интерполяцию сплайном Эрмита.
      /// </summary>
      /// <param name="xi">Точка, в которой вычисляется значение сплайна.</param>
      /// <param name="interp_func">Производная в точке, в которой вычисляется значение сплайна.</param>
      /// <returns>Интерполированное значение в точке value.</returns>
      public double Derivative(double xi, out double interp_func)
      {
         // Поиск интервала, содержащего  value
         int n = X.Length;
         int i = 0;
         if (xi < X[0])
         {
            interp_func = a0 * xi * xi + b0 * xi + c0;
            return 2 * a0 * xi + b0;
         }
         else if (xi >= X[n - 1])
         {
            interp_func = an * xi * xi + bn * xi + cn;
            return 2 * an * xi + bn;
         }
         else
         {
            while (i < n - 1 && xi >= X[i + 1]) i++;
         }

         double dx = xi - X[i];
         // Вычисляем значение сплайна в точке
         interp_func = A[i] + B[i] * dx + C[i] * dx * dx + D[i] * dx * dx * dx;
         // Вычисляем значение производной в точке
         return B[i] + 2 * C[i] * xi + 3 * D[i] * xi * xi - 6 * D[i] * xi * X[i] - 2 * C[i] * X[i] + 3 * D[i] * X[i] * X[i];
      }

      /// <summary>
      /// Возвращает интеполирующую функцию сплайна Акимы в точке.
      /// </summary>
      public Func<double, double> Interpolant()
      {
         return Interpolate;
      }

   }

}
