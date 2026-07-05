using System;
using System.Collections.Generic;

namespace CSTriangulation
{
   /// <summary>
   /// Проверки отрезков и треугольников для алгоритма продвижения фронта (§3.5).
   /// В отличие от <see cref="GeometryUtils"/>, здесь пересечение отрезков
   /// корректно ловит коллинеарное наложение (не только строгое скрещивание).
   /// </summary>
   internal static class SegmentGeometry
   {
      const double Eps = 1e-9;

      /// <summary>
      /// Пересечение отрезков AB и CD: строгое скрещивание ИЛИ коллинеарное наложение (§3.5, шаги 2-3).
      /// Общий конец — это стык, а не пересечение; вызывающий код обязан отфильтровать
      /// такие пары ДО вызова (по совпадению индексов узлов), см. "Практическое правило" §3.5.
      /// </summary>
      public static bool SegmentsIntersect(double ax, double ay, double bx, double by,
         double cx, double cy, double dx, double dy)
      {
         double d1 = GeometryUtils.Orient(cx, cy, dx, dy, ax, ay);
         double d2 = GeometryUtils.Orient(cx, cy, dx, dy, bx, by);
         double d3 = GeometryUtils.Orient(ax, ay, bx, by, cx, cy);
         double d4 = GeometryUtils.Orient(ax, ay, bx, by, dx, dy);

         if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return true;

         // Коллинеарное наложение — недопустимо (§3.5, шаг 3).
         if (Math.Abs(d1) < Eps && OnSegment(cx, cy, dx, dy, ax, ay)) return true;
         if (Math.Abs(d2) < Eps && OnSegment(cx, cy, dx, dy, bx, by)) return true;
         if (Math.Abs(d3) < Eps && OnSegment(ax, ay, bx, by, cx, cy)) return true;
         if (Math.Abs(d4) < Eps && OnSegment(ax, ay, bx, by, dx, dy)) return true;

         return false;
      }

      static bool OnSegment(double ax, double ay, double bx, double by, double px, double py)
      {
         return px >= Math.Min(ax, bx) - Eps && px <= Math.Max(ax, bx) + Eps &&
                py >= Math.Min(ay, by) - Eps && py <= Math.Max(ay, by) + Eps;
      }

      /// <summary>Отрезок (A,B) полностью внутри треугольника — недопустимо (§3.5, "Проверка: отрезок внутри треугольника").</summary>
      public static bool SegmentFullyInsideTriangle(double ax, double ay, double bx, double by,
         double x0, double y0, double x1, double y1, double x2, double y2)
      {
         return GeometryUtils.PointInTriangle(x0, y0, x1, y1, x2, y2, ax, ay) &&
                GeometryUtils.PointInTriangle(x0, y0, x1, y1, x2, y2, bx, by);
      }

      /// <summary>Страховочная проверка наложения нового треугольника на уже построенные (§3.5, CentroidCovered).</summary>
      public static bool CentroidCovered(int i, int j, int k, List<double[]> nodes, List<(int, int, int)> triangles)
      {
         double px = nodes[i][0], py = nodes[i][1];
         double qx = nodes[j][0], qy = nodes[j][1];
         double rx = nodes[k][0], ry = nodes[k][1];
         double cx = (px + qx + rx) / 3.0, cy = (py + qy + ry) / 3.0;

         foreach (var (a, b, c) in triangles)
         {
            double ax = nodes[a][0], ay = nodes[a][1];
            double bx = nodes[b][0], by = nodes[b][1];
            double ccx = nodes[c][0], ccy = nodes[c][1];
            double ecx = (ax + bx + ccx) / 3.0, ecy = (ay + by + ccy) / 3.0;

            if (GeometryUtils.PointInTriangle(px, py, qx, qy, rx, ry, ecx, ecy)) return true;
            if (GeometryUtils.PointInTriangle(ax, ay, bx, by, ccx, ccy, cx, cy)) return true;
         }
         return false;
      }
   }
}
