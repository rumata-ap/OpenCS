using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSmath
{
   [Serializable]
   public class HSpline : ISpline
   {
      public double[] X { get; set; }
      public double[] Y { get; set; }
      public double[] DY { get; set; }
      public double[] A { get; set; }
      public double[] B { get; set; }
      public double[] C { get; set; } = null!;
      public double[] D { get; set; } = null!;

      /// <summary>
      /// Конструктор класса сплайна Эрмита.
      /// </summary>
      /// <param name="x">Массив узлов по оси X.</param>
      /// <param name="y">Массив узлов по оси Y.</param>
      /// <param name="dy">Массив производных в узлах.</param>
      public HSpline(IEnumerable<double> x, IEnumerable<double> y, IEnumerable<double> dy)
      {
         if (x.Count() != y.Count() || x.Count() != dy.Count())
            throw new ArgumentException("Длины массивов x, y и dy должны совпадать.");

         double[] xa = x.ToArray();
         double[] ya = y.ToArray();
         double[] dya = dy.ToArray();

         // Сортировка входных данных по x
         var sorted = xa.Select((value, index) => new { x = value, y = ya[index], dy = dya[index] })
                       .OrderBy(point => point.x)
                       .ToArray();

         x = sorted.Select(point => point.x).ToArray();
         y = sorted.Select(point => point.y).ToArray();
         dy = sorted.Select(point => point.dy).ToArray();

         X = x.ToArray();
         Y = y.ToArray();
         DY = dy.ToArray();
         A = Y; B = DY;

      }

      /// <summary>
      /// Выполняет интерполяцию сплайном Эрмита.
      /// </summary>
      /// <param name="value">Точка, в которой вычисляется значение сплайна.</param>
      /// <param name="derivative">Производная в точке, в которой вычисляется значение сплайна.</param>
      /// <returns>Интерполированное значение в точке value.</returns>
      public double Interpolate(double value)
      {
         // Поиск интервала, содержащего  value
         int n = X.Length;
         int i = 0;
         if (value < X[0]) i = 0;
         else if (value >= X[n - 1]) i = n - 2;
         else
         {
            while (i < n - 1 && value > X[i + 1]) i++;
         }

         // Нормализуем t в интервал [0, 1]
         double t0 = X[i];
         double t1 = X[i + 1];
         double h = t1 - t0;
         double u = (value - t0) / h;

         // Значения в текущем интервале
         double y0 = Y[i];
         double y1 = Y[i + 1];
         double dy0 = DY[i] * h;
         double dy1 = DY[i + 1] * h;

         // Полиномы Эрмита
         double h00 = (1 + 2 * u) * (1 - u) * (1 - u);
         double h10 = u * (1 - u) * (1 - u);
         double h01 = u * u * (3 - 2 * u);
         double h11 = u * u * (u - 1);

         // Вычисляем значение сплайна
         return h00 * y0 + h10 * dy0 + h01 * y1 + h11 * dy1;
      }

      /// <summary>
      /// Выполняет производную в заданной точке сплайна Эрмита.
      /// </summary>
      /// <param name="value">Точка, в которой вычисляется значение производной сплайна.</param>
      /// <param name="interp_func">Интерполированное значение в точке value.</param>
      /// <returns>Значение производной в точке value.</returns>
      public double Derivative(double value, out double interp_func)
      {
         // Поиск интервала, содержащего  value
         int n = X.Length;
         int i = 0;
         if (value < X[0]) i = 0;
         else if (value >= X[n - 1]) i = n - 2;
         else
         {
            while (i < n - 1 && value > X[i + 1]) i++;
         }

         // Нормализуем t в интервал [0, 1]
         double t0 = X[i];
         double t1 = X[i + 1];
         double h = t1 - t0;
         double u = (value - t0) / h;

         // Значения в текущем интервале
         double y0 = Y[i];
         double y1 = Y[i + 1];
         double dy0 = DY[i] * h;
         double dy1 = DY[i + 1] * h;

         // Полиномы Эрмита
         double h00 = (1 + 2 * u) * (1 - u) * (1 - u);
         double h10 = u * (1 - u) * (1 - u);
         double h01 = u * u * (3 - 2 * u);
         double h11 = u * u * (u - 1);

         // Производные полиномов Эрмита
         double h00Prime = 6 * (u - 1) * u;
         double h10Prime = (1 - u) * (1 - 3 * u);
         double h01Prime = -6 * (u - 1) * u;
         double h11Prime = u * (3 * u - 2);

         interp_func = h00 * y0 + h10 * dy0 + h01 * y1 + h11 * dy1;
         // Вычисляем значение сплайна
         return (h00Prime * y0 + h10Prime * dy0 + h01Prime * y1 + h11Prime * dy1) / h;
      }

      /// <summary>
      /// Возвращает интеполирующую функцию сплайна Эрмита в точке.
      /// </summary>
      public Func<double, double> Interpolant()
      {
         return Interpolate;
      }

   }

}
