using System;

namespace CSTriangulation
{
   /// <summary>
   /// Вспомогательные геометрические функции для триангуляции.
   /// </summary>
   public static class GeometryUtils
   {
      /// <summary>
      /// Вычисляет угол при вершине q между лучами q→p и q→r (радианы, [0, π]).
      /// </summary>
      public static double AngleAtVertex(double px, double py, double qx, double qy, double rx, double ry)
      {
         double v1x = px - qx, v1y = py - qy;
         double v2x = rx - qx, v2y = ry - qy;
         double dot = v1x * v2x + v1y * v2y;
         double cross = v1x * v2y - v1y * v2x;
         return Math.Abs(Math.Atan2(cross, dot));
      }

      /// <summary>
      /// Проверяет, лежит ли точка p в секторе, образованном лучами q→r и q→p_base.
      /// </summary>
      public static bool PointInSector(double px, double py, double qx, double qy,
         double rx, double ry, double tx, double ty)
      {
         double crossQR = (rx - qx) * (ty - qy) - (ry - qy) * (tx - qx);
         double crossQP = (px - qx) * (ty - qy) - (py - qy) * (tx - qx);
         if (crossQR * crossQP < 0) return false;

         double v1x = px - qx, v1y = py - qy;
         double v2x = rx - qx, v2y = ry - qy;
         double crossBase = v1x * v2y - v1y * v2x;

         if (crossBase >= 0)
            return crossQP >= 0 && crossQR >= 0;
         else
            return crossQP <= 0 && crossQR <= 0;
      }

      /// <summary>
      /// Проверяет строгое пересечение открытых отрезков AB и CD.
      /// </summary>
      public static bool SegmentsIntersect(double ax, double ay, double bx, double by,
         double cx, double cy, double dx, double dy)
      {
         double denom = (bx - ax) * (dy - cy) - (by - ay) * (dx - cx);
         if (Math.Abs(denom) < 1e-14) return false;

         double t = ((cx - ax) * (dy - cy) - (cy - ay) * (dx - cx)) / denom;
         double u = ((cx - ax) * (by - ay) - (cy - ay) * (bx - ax)) / denom;

         return t > 1e-10 && t < 1.0 - 1e-10 && u > 1e-10 && u < 1.0 - 1e-10;
      }

      /// <summary>
      /// Проверяет, лежит ли точка (px,py) внутри или на границе треугольника (x0,y0)-(x1,y1)-(x2,y2).
      /// </summary>
      public static bool PointInTriangle(double x0, double y0, double x1, double y1,
         double x2, double y2, double px, double py)
      {
         double d0 = (x1 - x0) * (py - y0) - (y1 - y0) * (px - x0);
         double d1 = (x2 - x1) * (py - y1) - (y2 - y1) * (px - x1);
         double d2 = (x0 - x2) * (py - y2) - (y0 - y2) * (px - x2);
         bool hasNeg = (d0 < 0) || (d1 < 0) || (d2 < 0);
         bool hasPos = (d0 > 0) || (d1 > 0) || (d2 > 0);
         return !(hasNeg && hasPos);
      }

      /// <summary>
      /// Проверяет, лежит ли точка внутри замкнутого многоугольника (ray-casting).
      /// </summary>
      public static bool PointInPolygon(double px, double py, double[][] polygon)
      {
         int n = polygon.Length;
         bool inside = false;
         int j = n - 1;
         for (int i = 0; i < n; i++)
         {
            double xi = polygon[i][0], yi = polygon[i][1];
            double xj = polygon[j][0], yj = polygon[j][1];
            if (((yi > py) != (yj > py)) && (px < (xj - xi) * (py - yi) / (yj - yi) + xi))
               inside = !inside;
            j = i;
         }
         return inside;
      }

      /// <summary>
      /// Проверяет, лежит ли точка внутри замкнутого многоугольника (ray-casting).
      /// Вариант со списком кортежей.
      /// </summary>
      public static bool PointInPolygon(double px, double py, List<(double X, double Y)> polygon)
      {
         int n = polygon.Count;
         bool inside = false;
         int j = n - 1;
         for (int i = 0; i < n; i++)
         {
            double xi = polygon[i].X, yi = polygon[i].Y;
            double xj = polygon[j].X, yj = polygon[j].Y;
            if (((yi > py) != (yj > py)) && (px < (xj - xi) * (py - yi) / (yj - yi) + xi))
               inside = !inside;
            j = i;
         }
         return inside;
      }

      /// <summary>
      /// Вычисляет удвоенную ориентированную площадь многоугольника (положительна для CCW).
      /// </summary>
      public static double SignedArea(double[][] poly)
      {
         double s = 0;
         int n = poly.Length;
         for (int i = 0; i < n; i++)
         {
            int j = (i + 1) % n;
            s += poly[i][0] * poly[j][1] - poly[j][0] * poly[i][1];
         }
         return 0.5 * s;
      }

      /// <summary>
      /// Вычисляет удвоенную ориентированную площадь контура по индексам узлов.
      /// </summary>
      public static double SignedArea(List<int> contour, List<double[]> nodes)
      {
         double s = 0;
         int n = contour.Count;
         for (int i = 0; i < n; i++)
         {
            int j = (i + 1) % n;
            double xi = nodes[contour[i]][0], yi = nodes[contour[i]][1];
            double xj = nodes[contour[j]][0], yj = nodes[contour[j]][1];
            s += xi * yj - xj * yi;
         }
         return 0.5 * s;
      }
   }
}