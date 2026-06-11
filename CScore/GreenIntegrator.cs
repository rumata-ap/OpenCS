using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore
{
   /// <summary>
   /// Интегрирование двойных интегралов по замкнутым полигональным областям
   /// методом теоремы Грина с квадратурой Гаусса–Лежандра.
   /// ∬_D f(x,y) dA = −∮_∂D Q(x,y) dx, где Q(x,y) = ∫₀ʸ f(x,t) dt.
   /// </summary>
   public sealed class GreenIntegrator
   {
      // ---------------------------------------------------------------
      // GL-таблица: узлы и веса квадратуры Гаусса–Лежандра на [-1,1]
      // ---------------------------------------------------------------
      private static readonly Dictionary<int, (double[] Pts, double[] Wts)> GlTable = new()
      {
         [1]  = (new[] { 0.0 },
                 new[] { 2.0 }),
         [2]  = (new[] { -0.5773502691896257,  0.5773502691896257 },
                 new[] {  1.0,                  1.0 }),
         [3]  = (new[] { -0.7745966692414834,  0.0,                0.7745966692414834 },
                 new[] {  0.5555555555555556,   0.8888888888888888, 0.5555555555555556 }),
         [4]  = (new[] { -0.8611363115940526, -0.3399810435848563,  0.3399810435848563,  0.8611363115940526 },
                 new[] {  0.3478548451374538,   0.6521451548625461,  0.6521451548625461,  0.3478548451374538 }),
         [5]  = (new[] { -0.9061798459386640, -0.5384693101056831,  0.0,                 0.5384693101056831,  0.9061798459386640 },
                 new[] {  0.2369268850561891,   0.4786286704993665,  0.5688888888888889,  0.4786286704993665,  0.2369268850561891 }),
         [6]  = (new[] { -0.9324695142031521, -0.6612093864662645, -0.2386191860831969,  0.2386191860831969,  0.6612093864662645,  0.9324695142031521 },
                 new[] {  0.1713244923791704,   0.3607615730481386,  0.4679139345726910,  0.4679139345726910,  0.3607615730481386,  0.1713244923791704 }),
         [7]  = (new[] { -0.9491079123427585, -0.7415311855993945, -0.4058451513773972,  0.0,                 0.4058451513773972,  0.7415311855993945,  0.9491079123427585 },
                 new[] {  0.1294849661688697,   0.2797053914892767,  0.3818300505051189,  0.4179591836734694,  0.3818300505051189,  0.2797053914892767,  0.1294849661688697 }),
         [8]  = (new[] { -0.9602898564975363, -0.7966664774136267, -0.5255324099163290, -0.1834346424956498,  0.1834346424956498,  0.5255324099163290,  0.7966664774136267,  0.9602898564975363 },
                 new[] {  0.1012285362903763,   0.2223810344533745,  0.3137066458778873,  0.3626837833783620,  0.3626837833783620,  0.3137066458778873,  0.2223810344533745,  0.1012285362903763 }),
         [9]  = (new[] { -0.9681602395076261, -0.8360311073266358, -0.6133714327005904, -0.3242534234038089,  0.0,                 0.3242534234038089,  0.6133714327005904,  0.8360311073266358,  0.9681602395076261 },
                 new[] {  0.0812743883615744,   0.1806481606948574,  0.2606106964029354,  0.3123470770400029,  0.3302393550012598,  0.3123470770400029,  0.2606106964029354,  0.1806481606948574,  0.0812743883615744 }),
         [10] = (new[] { -0.9739065285171717, -0.8650633666889845, -0.6794095682990244, -0.4333953941292472, -0.1488743389816312,  0.1488743389816312,  0.4333953941292472,  0.6794095682990244,  0.8650633666889845,  0.9739065285171717 },
                 new[] {  0.0666713443086881,   0.1494513491505806,  0.2190863625159820,  0.2692667193099963,  0.2955242247147529,  0.2955242247147529,  0.2692667193099963,  0.2190863625159820,  0.1494513491505806,  0.0666713443086881 }),
      };

      private readonly List<(double X, double Y)> _outer;
      private readonly List<List<(double X, double Y)>> _holes;
      private readonly int _outerN;
      private readonly int _innerN;

      /// <summary>
      /// Создаёт интегратор для области с внешним контуром и отверстиями.
      /// Контуры должны быть незамкнутыми (последняя точка ≠ первая).
      /// </summary>
      /// <param name="outer">Вершины внешнего контура (без замыкающей точки).</param>
      /// <param name="holes">Вершины отверстий (без замыкающей точки). Null = нет отверстий.</param>
      /// <param name="outerGaussN">Порядок квадратуры вдоль рёбер контура (1..10, по умолчанию 5).</param>
      /// <param name="innerGaussN">Порядок квадратуры для антипроизводной Q (1..10, по умолчанию 5).</param>
      public GreenIntegrator(
         IReadOnlyList<(double X, double Y)> outer,
         IReadOnlyList<IReadOnlyList<(double X, double Y)>>? holes = null,
         int outerGaussN = 5,
         int innerGaussN = 5)
      {
         _outer  = EnsureCCW(outer);
         _holes  = holes?.Select(h => EnsureCW(h)).ToList()
                   ?? new List<List<(double X, double Y)>>();
         _outerN = Math.Max(1, Math.Min(outerGaussN, 10));
         _innerN = Math.Max(1, Math.Min(innerGaussN, 10));
      }

      // ---------------------------------------------------------------
      // Публичный метод: N, Mx = ∬σy dA, My = ∬σx dA за один проход
      // ---------------------------------------------------------------

      /// <summary>
      /// Вычисляет (N, Mx, My) = (∬σ dA, ∬σ·y dA, ∬σ·x dA) за один обход контура.
      /// </summary>
      /// <param name="sigma">Функция σ(x,y) — напряжение в точке.</param>
      /// <param name="critEps">Критические деформации (изломы диаграммы). Null = без разбиения.</param>
      /// <param name="epsFunc">Функция ε(x,y) — деформация в точке. Null = без разбиения рёбер.</param>
      public (double N, double Mx, double My) IntegrateN_Mx_My(
         Func<double, double, double> sigma,
         double[]? critEps = null,
         Func<double, double, double>? epsFunc = null)
      {
         double sumN = 0, sumMx = 0, sumMy = 0;

         var allContours = new List<IReadOnlyList<(double X, double Y)>>(_holes.Count + 1);
         allContours.Add(_outer);
         allContours.AddRange(_holes);

         var (glPts, glWts) = GlTable[_outerN];

         foreach (var contour in allContours)
         {
            int n = contour.Count;
            for (int i = 0; i < n; i++)
            {
               var (x0, y0) = contour[i];
               var (x1, y1) = contour[(i + 1) % n];
               double dx = x1 - x0;
               if (dx == 0.0) continue; // вертикальное ребро: ∫Q dx = 0

               var edgeTs = EdgeBreakpointsT(x0, y0, x1, y1, epsFunc, critEps);
               var bpts = new double[edgeTs.Count + 2];
               bpts[0] = 0.0;
               for (int j = 0; j < edgeTs.Count; j++) bpts[j + 1] = edgeTs[j];
               bpts[^1] = 1.0;

               for (int s = 0; s < bpts.Length - 1; s++)
               {
                  double ta = bpts[s], tb = bpts[s + 1];
                  if (ta == tb) continue;
                  double half = 0.5 * (tb - ta);
                  double mid  = 0.5 * (ta + tb);

                  for (int j = 0; j < glPts.Length; j++)
                  {
                     double t = mid + half * glPts[j];
                     double x = x0 + t * dx;
                     double y = y0 + t * (y1 - y0);

                     double qN  = MakeQ_N(x, y, sigma, critEps, epsFunc);
                     double qMx = MakeQ_Mx(x, y, sigma, critEps, epsFunc);
                     // Q_My(x,y) = x · Q_N(x,y) — аналитически

                     double w = glWts[j] * half * dx;
                     sumN  += w * qN;
                     sumMx += w * qMx;
                     sumMy += w * x * qN;
                  }
               }
            }
         }

         return (-sumN, -sumMx, -sumMy);
      }

      // ---------------------------------------------------------------
      // Антипроизводные Q
      // ---------------------------------------------------------------

      /// <summary>Q_N(x,y) = ∫₀ʸ σ(x,t) dt</summary>
      private double MakeQ_N(double x, double y,
         Func<double, double, double> sigma,
         double[]? critEps, Func<double, double, double>? epsFunc)
      {
         if (y == 0.0) return 0.0;
         double[] bpts = InnerBreakpoints(x, y, epsFunc, critEps);
         return GaussPiecewise(t => sigma(x, t), bpts, _innerN);
      }

      /// <summary>Q_Mx(x,y) = ∫₀ʸ t·σ(x,t) dt</summary>
      private double MakeQ_Mx(double x, double y,
         Func<double, double, double> sigma,
         double[]? critEps, Func<double, double, double>? epsFunc)
      {
         if (y == 0.0) return 0.0;
         double[] bpts = InnerBreakpoints(x, y, epsFunc, critEps);
         return GaussPiecewise(t => t * sigma(x, t), bpts, _innerN);
      }

      // ---------------------------------------------------------------
      // Разбиение по критическим деформациям
      // ---------------------------------------------------------------

      /// <summary>
      /// Точки разбиения для ∫₀ʸ — значения t ∈ (0,y), где ε(x,t) = ε*.
      /// Порядок: от 0 к y (для y &lt; 0 — по убыванию).
      /// </summary>
      private static double[] InnerBreakpoints(double x, double y,
         Func<double, double, double>? epsFunc, double[]? critEps)
      {
         if (epsFunc == null || critEps == null || critEps.Length == 0)
            return new[] { 0.0, y };

         // dε/dy = epsFunc(x,1) - epsFunc(x,0) (точно для линейной ε)
         double kappa = epsFunc(x, 1.0) - epsFunc(x, 0.0);
         if (Math.Abs(kappa) < 1e-14)
            return new[] { 0.0, y };

         double epsAtZero = epsFunc(x, 0.0);
         double lo = Math.Min(0.0, y), hi = Math.Max(0.0, y);
         var interior = new List<double>();
         foreach (var epsStar in critEps)
         {
            double t = (epsStar - epsAtZero) / kappa;
            if (t > lo && t < hi)
               interior.Add(t);
         }
         interior.Sort();
         if (y < 0.0) interior.Reverse();

         var result = new double[interior.Count + 2];
         result[0] = 0.0;
         for (int i = 0; i < interior.Count; i++) result[i + 1] = interior[i];
         result[^1] = y;
         return result;
      }

      /// <summary>
      /// Параметры t ∈ (0,1) вдоль ребра (x0,y0)→(x1,y1), где ε(x(t),y(t)) = ε*.
      /// </summary>
      private static List<double> EdgeBreakpointsT(double x0, double y0, double x1, double y1,
         Func<double, double, double>? epsFunc, double[]? critEps)
      {
         if (epsFunc == null || critEps == null || critEps.Length == 0)
            return new List<double>();

         double A = epsFunc(x0, y0);
         double B = epsFunc(x1, y1) - A; // dε/dt вдоль ребра (точно для линейной ε)
         if (Math.Abs(B) < 1e-14)
            return new List<double>();

         var result = new List<double>();
         foreach (var epsStar in critEps)
         {
            double t = (epsStar - A) / B;
            if (t > 1e-10 && t < 1.0 - 1e-10)
               result.Add(t);
         }
         result.Sort();
         return result;
      }

      // ---------------------------------------------------------------
      // Квадратура Гаусса–Лежандра
      // ---------------------------------------------------------------

      /// <summary>∫ₐᵇ f(t) dt квадратурой GL порядка n.</summary>
      private static double GaussOnInterval(Func<double, double> f, double a, double b, int n)
      {
         if (a == b) return 0.0;
         var (pts, wts) = GlTable[n];
         double mid = 0.5 * (a + b), half = 0.5 * (b - a);
         double result = 0.0;
         for (int i = 0; i < pts.Length; i++)
            result += wts[i] * f(mid + half * pts[i]);
         return result * half;
      }

      /// <summary>Интегрирование по кускам: breakpoints задают границы в нужном порядке.</summary>
      private static double GaussPiecewise(Func<double, double> f, double[] breakpoints, int n)
      {
         if (breakpoints.Length < 2) return 0.0;
         double result = 0.0;
         for (int i = 0; i < breakpoints.Length - 1; i++)
         {
            double a = breakpoints[i], b = breakpoints[i + 1];
            if (a != b) result += GaussOnInterval(f, a, b, n);
         }
         return result;
      }

      // ---------------------------------------------------------------
      // Геометрия полигонов
      // ---------------------------------------------------------------

      /// <summary>Ориентированная площадь полигона. Положительная → CCW.</summary>
      private static double SignedArea(IReadOnlyList<(double X, double Y)> v)
      {
         int n = v.Count;
         double s = 0.0;
         for (int i = 0; i < n; i++)
         {
            var (x0, y0) = v[i];
            var (x1, y1) = v[(i + 1) % n];
            s += (x1 - x0) * (y0 + y1);
         }
         return -0.5 * s;
      }

      private static List<(double X, double Y)> EnsureCCW(IReadOnlyList<(double X, double Y)> v)
      {
         var list = new List<(double X, double Y)>(v);
         if (SignedArea(list) < 0) list.Reverse();
         return list;
      }

      private static List<(double X, double Y)> EnsureCW(IReadOnlyList<(double X, double Y)> v)
      {
         var list = new List<(double X, double Y)>(v);
         if (SignedArea(list) > 0) list.Reverse();
         return list;
      }
   }
}
