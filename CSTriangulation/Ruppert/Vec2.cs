using System;

namespace CSTriangulation.Ruppert
{
   /// <summary>
   /// Двумерный вектор (точка) с базовыми операциями.
   /// </summary>
   public sealed class Vec2
   {
      public double X { get; }
      public double Y { get; }

      public Vec2(double x, double y) { X = x; Y = y; }

      public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
      public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
      public static Vec2 operator *(Vec2 v, double s) => new(v.X * s, v.Y * s);
      public static Vec2 operator *(double s, Vec2 v) => v * s;
      public static Vec2 operator -(Vec2 v) => new(-v.X, -v.Y);

      public double Dot(Vec2 o) => X * o.X + Y * o.Y;
      public double Cross(Vec2 o) => X * o.Y - Y * o.X;
      public double Len2() => X * X + Y * Y;
      public double Len() => Math.Sqrt(Len2());
      public double Dist(Vec2 o) => (this - o).Len();
      public double Dist2(Vec2 o) => (this - o).Len2();
      public Vec2 Mid(Vec2 o) => new((X + o.X) * 0.5, (Y + o.Y) * 0.5);
      public Vec2 Norm()
      {
         double l = Len();
         return l > Eps ? new Vec2(X / l, Y / l) : new Vec2(0, 0);
      }

      public override string ToString() => $"({X:F6},{Y:F6})";

      public bool Equals(Vec2 other) =>
         other != null && Math.Abs(X - other.X) < Eps && Math.Abs(Y - other.Y) < Eps;

      public override int GetHashCode() => HashCode.Combine(Math.Round(X, 8), Math.Round(Y, 8));

      public const double Eps = 1e-9;
   }

   /// <summary>
   /// Геометрические примитивы для алгоритма Рупперта.
   /// </summary>
   public static class Geo
   {
      /// <summary>
      /// Ориентированная площадь треугольника (p0,p1,p2). Положительна для CCW.
      /// </summary>
      public static double Orient2D(Vec2 a, Vec2 b, Vec2 c) =>
         (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

      /// <summary>
      /// Тест описанной окружности Делоне. &gt;0 если d внутри CCW(a,b,c).
      /// </summary>
      public static double InCircle(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
      {
         double ax = a.X - d.X, ay = a.Y - d.Y, ar2 = ax * ax + ay * ay;
         double bx = b.X - d.X, by = b.Y - d.Y, br2 = bx * bx + by * by;
         double cx = c.X - d.X, cy = c.Y - d.Y, cr2 = cx * cx + cy * cy;
         return ax * (by * cr2 - cy * br2) - ay * (bx * cr2 - cx * br2) + ar2 * (bx * cy - cx * by);
      }

      /// <summary>
      /// Центр описанной окружности треугольника (a,b,c). null если треугольник вырожден.
      /// </summary>
      public static Vec2? Circumcenter(Vec2 a, Vec2 b, Vec2 c)
      {
         double ax = b.X - a.X, ay = b.Y - a.Y;
         double bx = c.X - a.X, by = c.Y - a.Y;
         double D = 2 * (ax * by - ay * bx);
         if (Math.Abs(D) < Vec2.Eps) return null;
         double ux = (by * (ax * ax + ay * ay) - ay * (bx * bx + by * by)) / D;
         double uy = (ax * (bx * bx + by * by) - bx * (ax * ax + ay * ay)) / D;
         return new Vec2(a.X + ux, a.Y + uy);
      }

      /// <summary>
      /// Проверяет строгое пересечение открытых отрезков (p1,p2) и (p3,p4).
      /// </summary>
      public static bool SegCross(Vec2 p1, Vec2 p2, Vec2 p3, Vec2 p4)
      {
         double d1 = Orient2D(p3, p4, p1), d2 = Orient2D(p3, p4, p2);
         double d3 = Orient2D(p1, p2, p3), d4 = Orient2D(p1, p2, p4);
         return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
      }

      /// <summary>
      /// Точка строгого пересечения отрезков. null если нет пересечения.
      /// </summary>
      public static Vec2? SegIntersect(Vec2 p1, Vec2 p2, Vec2 p3, Vec2 p4)
      {
         double d1 = Orient2D(p3, p4, p1), d2 = Orient2D(p3, p4, p2);
         double d3 = Orient2D(p1, p2, p3), d4 = Orient2D(p1, p2, p4);
         if (!((d1 > 0 && d2 < 0 || d1 < 0 && d2 > 0) &&
               (d3 > 0 && d4 < 0 || d3 < 0 && d4 > 0)))
            return null;
         double dn = d1 - d2;
         if (Math.Abs(dn) < Vec2.Eps) return null;
         double t = d1 / dn;
         return new Vec2(p1.X + t * (p2.X - p1.X), p1.Y + t * (p2.Y - p1.Y));
      }

      /// <summary>
      /// Проверяет, что p строго лежит на открытом отрезке (a,b), не совпадая с концами.
      /// </summary>
      public static bool OnSegStrict(Vec2 p, Vec2 a, Vec2 b)
      {
         Vec2 seg = b - a;
         double sl2 = seg.Len2();
         if (sl2 < Vec2.Eps * Vec2.Eps) return false;
         double sl = Math.Sqrt(sl2);
         double cross = Math.Abs((b.X - a.X) * (a.Y - p.Y) - (a.X - p.X) * (b.Y - a.Y));
         if (cross / sl > Vec2.Eps * 100) return false;
         Vec2 ap = p - a;
         double t = ap.Dot(seg) / sl2;
         return Vec2.Eps * 100 < t && t < 1 - Vec2.Eps * 100;
      }

      /// <summary>
      /// Площадь многоугольника (удвоенная), положительна для CCW.
      /// </summary>
      public static double PolyArea(Vec2[] pts)
      {
         double s = 0;
         int n = pts.Length;
         for (int i = 0; i < n; i++)
         {
            int j = (i + 1) % n;
            s += pts[i].X * pts[j].Y - pts[j].X * pts[i].Y;
         }
         return s * 0.5;
      }

      /// <summary>
      /// Гарантирует CCW порядок вершин.
      /// </summary>
      public static Vec2[] EnsureCCW(Vec2[] pts) =>
         PolyArea(pts) >= 0 ? pts : pts.Reverse().ToArray();

      /// <summary>
      /// Гарантирует CW порядок вершин.
      /// </summary>
      public static Vec2[] EnsureCW(Vec2[] pts) =>
         PolyArea(pts) <= 0 ? pts : pts.Reverse().ToArray();

      /// <summary>
      /// Проверяет принадлежность точки многоугольнику (ray-casting).
      /// </summary>
      public static bool PointInPoly(Vec2 p, Vec2[] poly)
      {
         int n = poly.Length;
         bool inside = false;
         int j = n - 1;
         for (int i = 0; i < n; i++)
         {
            double yi = poly[i].Y, yj = poly[j].Y;
            if ((yi > p.Y) != (yj > p.Y))
            {
               double xi = poly[i].X, xj = poly[j].X;
               double xf = (xj - xi) * (p.Y - yi) / (yj - yi + 1e-30) + xi;
               if (p.X < xf) inside = !inside;
            }
            j = i;
         }
         return inside;
      }

      /// <summary>
      /// Минимальный угол треугольника в градусах.
      /// </summary>
      public static double TriMinAngleDeg(Vec2 a, Vec2 b, Vec2 c)
      {
         double s0 = a.Dist(b), s1 = b.Dist(c), s2 = c.Dist(a);
         double[] s = [s0, s1, s2];
         Array.Sort(s);
         if (s[0] < Vec2.Eps) return 0.0;
         double cosA = (s[1] * s[1] + s[2] * s[2] - s[0] * s[0]) / (2 * s[1] * s[2]);
         return Math.Acos(Math.Max(-1.0, Math.Min(1.0, cosA))) * 180.0 / Math.PI;
      }

      /// <summary>
      /// Площадь треугольника (удвоенная, положительная).
      /// </summary>
      public static double TriArea(Vec2 a, Vec2 b, Vec2 c) =>
         Math.Abs(Orient2D(a, b, c)) * 0.5;
   }
}