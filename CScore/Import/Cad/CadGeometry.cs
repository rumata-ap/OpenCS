namespace CScore.Import.Cad;

/// <summary>Плоская геометрия контуров чертежа: площадь, вхождение, дуги, самопересечения.</summary>
public static class CadGeometry
{
   /// <summary>Площадь со знаком (формула Гаусса) по вершинам без повтора первой.</summary>
   public static double SignedArea(IReadOnlyList<CadPoint> p)
   {
      double s = 0;
      for (int i = 0, n = p.Count; i < n; i++)
      {
         var a = p[i];
         var b = p[(i + 1) % n];
         s += a.X * b.Y - b.X * a.Y;
      }
      return s / 2;
   }

   /// <summary>Центр тяжести полигона (без повтора первой вершины); для вырожденного — среднее вершин.</summary>
   public static CadPoint Centroid(IReadOnlyList<CadPoint> p)
   {
      double a = 0, cx = 0, cy = 0;
      for (int i = 0, n = p.Count; i < n; i++)
      {
         var u = p[i];
         var v = p[(i + 1) % n];
         double c = u.X * v.Y - v.X * u.Y;
         a += c;
         cx += (u.X + v.X) * c;
         cy += (u.Y + v.Y) * c;
      }
      if (Math.Abs(a) < 1e-300)
         return new(p.Average(q => q.X), p.Average(q => q.Y));
      return new(cx / (3 * a), cy / (3 * a));
   }

   /// <summary>Точка строго внутри полигона (луч, чётность пересечений).</summary>
   public static bool Inside(IReadOnlyList<CadPoint> p, double x, double y)
   {
      bool inside = false;
      for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
      {
         var a = p[i];
         var b = p[j];
         if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
            inside = !inside;
      }
      return inside;
   }

   /// <summary>Расстояние от точки до границы полигона.</summary>
   public static double DistanceToBoundary(IReadOnlyList<CadPoint> p, double x, double y)
   {
      double best = double.MaxValue;
      for (int i = 0, n = p.Count; i < n; i++)
      {
         var a = p[i];
         var b = p[(i + 1) % n];
         double dx = b.X - a.X, dy = b.Y - a.Y;
         double l2 = dx * dx + dy * dy;
         double t = l2 > 0 ? Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / l2, 0, 1) : 0;
         double ex = a.X + t * dx - x, ey = a.Y + t * dy - y;
         best = Math.Min(best, ex * ex + ey * ey);
      }
      return Math.Sqrt(best);
   }

   /// <summary>
   /// Полигон <paramref name="inner"/> лежит в <paramref name="outer"/>: габарит внутри и большинство вершин
   /// строго внутри (вершины на общей границе не решают вхождение).
   /// </summary>
   public static bool Contains(CadPolygon outer, CadPolygon inner)
   {
      if (!outer.Bounds.Contains(inner.Bounds) || Math.Abs(inner.Area) >= Math.Abs(outer.Area)) return false;
      int inside = 0;
      foreach (var v in inner.Points)
         if (Inside(outer.Points, v.X, v.Y)) inside++;
      return inside * 2 > inner.Points.Count;
   }

   /// <summary>Наибольший центральный угол одного отрезка ломаной дуги — чтобы малые скругления профилей не пропадали.</summary>
   public const double MaxArcStep = Math.PI / 18; // 10°

   /// <summary>Число отрезков ломаной дуги радиуса r с центральным углом sweep: по хорде и не больше 10° на отрезок.</summary>
   public static int ArcSegments(double r, double sweep, double chord)
   {
      sweep = Math.Abs(sweep);
      int byAngle = (int)Math.Ceiling(sweep / MaxArcStep - 1e-9);
      int byChord = chord > 0 && r > 0
         ? (int)Math.Ceiling(sweep / (2 * Math.Asin(Math.Min(1, chord / (2 * r)))) - 1e-9)
         : 0;
      return Math.Clamp(Math.Max(byAngle, byChord), 1, 720);
   }

   /// <summary>Ломаная окружности против часовой стрелки; число сторон — по хорде и не меньше 36 (10°).</summary>
   public static List<CadPoint> CirclePoints(double cx, double cy, double r, double chord)
   {
      int n = ArcSegments(r, 2 * Math.PI, chord);
      var pts = new List<CadPoint>(n);
      for (int i = 0; i < n; i++)
      {
         double a = 2 * Math.PI * i / n;
         pts.Add(new(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
      }
      return pts;
   }

   /// <summary>
   /// Промежуточные точки дуги сегмента полилинии с кривизной bulge (tg четверти центрального угла),
   /// без начальной и конечной точек. Длина хорды аппроксимации — <paramref name="chord"/>, шаг не больше 10°.
   /// </summary>
   public static IEnumerable<CadPoint> BulgeArc(CadPoint p1, CadPoint p2, double bulge, double chord)
   {
      double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
      double c = Math.Sqrt(dx * dx + dy * dy);
      if (c < 1e-12 || Math.Abs(bulge) < 1e-9) yield break;
      double theta = 4 * Math.Atan(bulge);                       // центральный угол со знаком
      double r = c / (2 * Math.Sin(Math.Abs(theta) / 2));
      // Центр: от середины хорды по нормали влево на r·cos(θ/2)·sign.
      double h = r * Math.Cos(theta / 2) * Math.Sign(bulge);
      double mx = (p1.X + p2.X) / 2, my = (p1.Y + p2.Y) / 2;
      double cx = mx - h * dy / c, cy = my + h * dx / c;
      double a1 = Math.Atan2(p1.Y - cy, p1.X - cx);
      int n = Math.Max(2, ArcSegments(r, theta, chord));
      for (int i = 1; i < n; i++)
      {
         double a = a1 + theta * i / n;
         yield return new(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
      }
   }

   /// <summary>Есть ли у полигона пересекающиеся несмежные стороны.</summary>
   public static bool SelfIntersects(IReadOnlyList<CadPoint> p)
   {
      int n = p.Count;
      if (n < 4) return false;
      for (int i = 0; i < n; i++)
      {
         var a1 = p[i];
         var a2 = p[(i + 1) % n];
         for (int j = i + 2; j < n; j++)
         {
            if (i == 0 && j == n - 1) continue; // смежные через замыкание
            var b1 = p[j];
            var b2 = p[(j + 1) % n];
            if (SegmentsCross(a1, a2, b1, b2)) return true;
         }
      }
      return false;
   }

   static bool SegmentsCross(CadPoint a, CadPoint b, CadPoint c, CadPoint d)
   {
      double d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
      return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
   }

   static double Cross(CadPoint o, CadPoint a, CadPoint b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
}
