using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore
{
   /// <summary>
   /// Результат ячейки сетки — полигон с площадью и центроидом.
   /// </summary>
   public class GridCell
   {
      public List<(double X, double Y)> Vertices { get; set; } = [];
      public double Area { get; set; }
      public (double X, double Y) Centroid { get; set; }
   }

   /// <summary>
   /// Статический класс с методами разбиения контура на полосы/ячейки
   /// по ортогональной сетке с помощью алгоритма Сазерленда–Ходжмана.
   /// </summary>
   public static class GridSplit
   {
      const double Tol = 1e-9;
      const double MinArea = 1e-14;

      static List<(double X, double Y)> ClipByHalfPlane(
         List<(double X, double Y)> verts, double px, double py, double nx, double ny)
      {
         if (verts.Count == 0)
            return [];

         int n = verts.Count;
         var output = new List<(double, double)>(n + 2);

         for (int i = 0; i < n; i++)
         {
            var curr = verts[i];
            var nxt = verts[(i + 1) % n];
            double cs = (curr.X - px) * nx + (curr.Y - py) * ny;
            double ns = (nxt.X - px) * nx + (nxt.Y - py) * ny;

            if (cs >= 0)
               output.Add(curr);

            if ((cs >= 0) != (ns >= 0))
            {
               double dx = nxt.X - curr.X;
               double dy = nxt.Y - curr.Y;
               double denom = dx * nx + dy * ny;
               if (Math.Abs(denom) > 1e-14)
               {
                  double t = -cs / denom;
                  output.Add((curr.X + t * dx, curr.Y + t * dy));
               }
            }
         }

         return output;
      }

      static List<(double X, double Y)> ClipByRect(
         List<(double X, double Y)> verts, double x0, double x1, double y0, double y1)
      {
         var v = ClipByHalfPlane(verts, x0, 0, 1, 0);
         v = ClipByHalfPlane(v, x1, 0, -1, 0);
         v = ClipByHalfPlane(v, 0, y0, 0, 1);
         v = ClipByHalfPlane(v, 0, y1, 0, -1);
         return v;
      }

      static double SignedArea(List<(double X, double Y)> verts)
      {
         int n = verts.Count;
         double s = 0;
         for (int i = 0; i < n; i++)
         {
            var (x0, y0) = verts[i];
            var (x1, y1) = verts[(i + 1) % n];
            s += x0 * y1 - x1 * y0;
         }
         return 0.5 * s;
      }

      static (double X, double Y) CentroidXY(List<(double X, double Y)> verts)
      {
         if (verts.Count == 0) return (0, 0);
         double sx = 0, sy = 0;
         for (int i = 0; i < verts.Count; i++)
         {
            sx += verts[i].X;
            sy += verts[i].Y;
         }
         return (sx / verts.Count, sy / verts.Count);
      }

      static bool PointInPoly(double px, double py, List<(double X, double Y)> verts)
      {
         int n = verts.Count;
         bool inside = false;
         int j = n - 1;
         for (int i = 0; i < n; i++)
         {
            double xi = verts[i].X, yi = verts[i].Y;
            double xj = verts[j].X, yj = verts[j].Y;
            if (((yi > py) != (yj > py)) && (px < (xj - xi) * (py - yi) / (yj - yi) + xi))
               inside = !inside;
            j = i;
         }
         return inside;
      }

      static List<(double X, double Y)> RemoveSpikes(List<(double X, double Y)> verts)
      {
         if (verts.Count < 3) return verts;

         bool changed = true;
         while (changed && verts.Count >= 3)
         {
            changed = false;
            int n = verts.Count;
            var keep = new bool[n];
            for (int i = 0; i < n; i++) keep[i] = true;

            for (int i = 0; i < n; i++)
            {
               if (!keep[i]) continue;
               var a = verts[(i - 1 + n) % n];
               var b = verts[i];
               var c = verts[(i + 1) % n];

               double abx = b.X - a.X, aby = b.Y - a.Y;
               double bcx = c.X - b.X, bcy = c.Y - b.Y;
               double cross = abx * bcy - aby * bcx;
               double dot = abx * bcx + aby * bcy;
                double mag = Math.Abs(abx) + Math.Abs(aby) + Math.Abs(bcx) + Math.Abs(bcy);
                if (mag == 0) mag = 1.0;

               if (Math.Abs(cross) < Tol * mag && dot < -Tol)
               {
                  keep[i] = false;
                  changed = true;
               }
            }

            if (changed)
            {
               var nv = new List<(double, double)>(n);
               for (int i = 0; i < n; i++)
                  if (keep[i]) nv.Add(verts[i]);
               verts = nv;
            }
         }
         return verts;
      }

      static void DoSplitWound(List<(double X, double Y)> verts, double x0, double x1,
         double y0, double y1, List<List<(double X, double Y)>> output)
      {
         int n = verts.Count;
         if (n < 3) return;
         if (SignedArea(verts) <= 0) return;

         for (int k1 = 0; k1 < n; k1++)
         {
            int k2_next = (k1 + 1) % n;
            var (a1x, a1y) = verts[k1];
            var (b1x, b1y) = verts[k2_next];

            for (int k2 = k1 + 1; k2 < n; k2++)
            {
               int k2n = (k2 + 1) % n;
               var (a2x, a2y) = verts[k2];
               var (b2x, b2y) = verts[k2n];

               string axis1 = null; double bv1 = 0, c1s = 0, c1e = 0;
               string axis2 = null; double bv2 = 0, c2s = 0, c2e = 0;

               if (Math.Abs(a1x - x0) < Tol && Math.Abs(b1x - x0) < Tol && Math.Abs(a1y - b1y) > Tol)
               { axis1 = "x"; bv1 = x0; c1s = a1y; c1e = b1y; }
               else if (Math.Abs(a1x - x1) < Tol && Math.Abs(b1x - x1) < Tol && Math.Abs(a1y - b1y) > Tol)
               { axis1 = "x"; bv1 = x1; c1s = a1y; c1e = b1y; }
               else if (Math.Abs(a1y - y0) < Tol && Math.Abs(b1y - y0) < Tol && Math.Abs(a1x - b1x) > Tol)
               { axis1 = "y"; bv1 = y0; c1s = a1x; c1e = b1x; }
               else if (Math.Abs(a1y - y1) < Tol && Math.Abs(b1y - y1) < Tol && Math.Abs(a1x - b1x) > Tol)
               { axis1 = "y"; bv1 = y1; c1s = a1x; c1e = b1x; }

               if (axis1 == null) continue;

               if (Math.Abs(a2x - x0) < Tol && Math.Abs(b2x - x0) < Tol && Math.Abs(a2y - b2y) > Tol)
               { axis2 = "x"; bv2 = x0; c2s = a2y; c2e = b2y; }
               else if (Math.Abs(a2x - x1) < Tol && Math.Abs(b2x - x1) < Tol && Math.Abs(a2y - b2y) > Tol)
               { axis2 = "x"; bv2 = x1; c2s = a2y; c2e = b2y; }
               else if (Math.Abs(a2y - y0) < Tol && Math.Abs(b2y - y0) < Tol && Math.Abs(a2x - b2x) > Tol)
               { axis2 = "y"; bv2 = y0; c2s = a2x; c2e = b2x; }
               else if (Math.Abs(a2y - y1) < Tol && Math.Abs(b2y - y1) < Tol && Math.Abs(a2x - b2x) > Tol)
               { axis2 = "y"; bv2 = y1; c2s = a2x; c2e = b2x; }

               if (axis2 == null || axis1 != axis2 || Math.Abs(bv1 - bv2) > Tol) continue;
               if ((c1e - c1s) * (c2e - c2s) >= 0) continue;

               double lo = Math.Max(Math.Min(c1s, c1e), Math.Min(c2s, c2e));
               double hi = Math.Min(Math.Max(c1s, c1e), Math.Max(c2s, c2e));
               if (hi <= lo + Tol) continue;

               int i1 = k1, i2 = k2;
               double sc1s = c1s, sc1e = c1e, sc2s = c2s, sc2e = c2e;

               if (Math.Abs(sc1e - sc1s) > Math.Abs(sc2e - sc2s))
               {
                  (i1, i2) = (i2, i1);
                   (sc1s, sc1e, sc2s, sc2e) = (sc2s, sc2e, sc1s, sc1e);
               }

               var ptA = verts[i1];
               var ptB = verts[(i1 + 1) % n];

                List<(double coord, (double X, double Y) pt)> toInsert;
                if (sc2e > sc2s)
                   toInsert = [(sc1s, ptA), (sc1e, ptB)];
                else
                   toInsert = [(sc1s, ptA), (sc1e, ptB)];
                toInsert.Sort((a, b) => a.coord.CompareTo(b.coord));

               if (sc2e < sc2s)
                  toInsert.Reverse();

               var newVerts = new List<(double X, double Y)>(verts);
               int pos = i2 + 1;
               for (int ti = 0; ti < toInsert.Count; ti++)
               {
                  newVerts.Insert(pos, toInsert[ti].pt);
                  pos++;
               }

               SplitAtDuplicates(newVerts, x0, x1, y0, y1, output);
               return;
            }
         }

         output.Add(verts);
      }

      static void SplitAtDuplicates(List<(double X, double Y)> verts, double x0, double x1,
         double y0, double y1, List<List<(double X, double Y)>> output)
      {
         int n = verts.Count;
         for (int i = 0; i < n; i++)
         {
            for (int j = i + 1; j < n; j++)
            {
               if (Math.Abs(verts[i].X - verts[j].X) < Tol &&
                   Math.Abs(verts[i].Y - verts[j].Y) < Tol)
               {
                  var sub1 = verts.GetRange(i, j - i);
                  var sub2 = new List<(double X, double Y)>(n - (j - i));
                  sub2.AddRange(verts.GetRange(j, n - j));
                  sub2.AddRange(verts.GetRange(0, i));
                  if (sub1.Count >= 3 && sub2.Count >= 3)
                  {
                     DoSplitWound(sub1, x0, x1, y0, y1, output);
                     DoSplitWound(sub2, x0, x1, y0, y1, output);
                     return;
                  }
               }
            }
         }
         output.Add(verts);
      }

      static List<List<(double X, double Y)>> SplitWoundPolygon(
         List<(double X, double Y)> verts, double x0, double x1, double y0, double y1)
      {
         var output = new List<List<(double X, double Y)>>();
         DoSplitWound(new List<(double X, double Y)>(verts), x0, x1, y0, y1, output);
         return output.Where(p => p.Count >= 3 && SignedArea(p) > 0).ToList();
      }

      static double NetArea(List<(double X, double Y)> outer, List<List<(double X, double Y)>> holes)
      {
         double A6sum = 0;
         double cxNum = 0, cyNum = 0;

         void ProcessRing(List<(double X, double Y)> ring)
         {
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
               double x0 = ring[i].X, y0 = ring[i].Y;
               double x1 = ring[(i + 1) % n].X, y1 = ring[(i + 1) % n].Y;
               double c = x0 * y1 - x1 * y0;
               A6sum += c;
               cxNum += (x0 + x1) * c;
               cyNum += (y0 + y1) * c;
            }
         }

         ProcessRing(outer);
         if (holes != null)
            foreach (var h in holes) ProcessRing(h);

         if (Math.Abs(A6sum) < 1e-14)
            return 0;

         return 0.5 * A6sum;
      }

      static (double cx, double cy) NetCentroid(List<(double X, double Y)> outer,
         List<List<(double X, double Y)>> holes)
      {
         double A6sum = 0;
         double cxNum = 0, cyNum = 0;

         void ProcessRing(List<(double X, double Y)> ring)
         {
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
               double x0 = ring[i].X, y0 = ring[i].Y;
               double x1 = ring[(i + 1) % n].X, y1 = ring[(i + 1) % n].Y;
               double c = x0 * y1 - x1 * y0;
               A6sum += c;
               cxNum += (x0 + x1) * c;
               cyNum += (y0 + y1) * c;
            }
         }

         ProcessRing(outer);
         if (holes != null)
            foreach (var h in holes) ProcessRing(h);

         if (Math.Abs(A6sum) < 1e-14)
            return (0, 0);

         return (cxNum / (3.0 * A6sum), cyNum / (3.0 * A6sum));
      }

      static List<double> GridLines(double valMin, double valMax, double step, double origin)
      {
         int iLo = (int)Math.Floor((valMin - origin) / step);
         int iHi = (int)Math.Ceiling((valMax - origin) / step);
         var raw = new List<double>();
         var set = new HashSet<double>();
         for (int i = iLo; i <= iHi; i++)
         {
            double v = Math.Round(origin + i * step, 12);
            set.Add(v);
         }
         set.Add(valMin);
         set.Add(valMax);
         raw = set.ToList();
         raw.Sort();
         var result = new List<double> { raw[0] };
         for (int i = 1; i < raw.Count; i++)
            if (raw[i] - result[result.Count - 1] > Tol)
               result.Add(raw[i]);
         return result;
      }

      /// <summary>
      /// Разбивает контур области на волокна методом ортогональной сетки
      /// (алгоритм Сазерленда–Ходжмана). Поддерживает разбиение на полосы
      /// (только по X или только по Y) или на прямоугольные ячейки.
      /// </summary>
      /// <param name="region">Область сечения для разбиения.</param>
      /// <param name="nx">Количество участков деления по оси X (0 — без разрезов по X).</param>
      /// <param name="ny">Количество участков деления по оси Y (0 — без разрезов по Y).</param>
      /// <returns>Массив волокон Fiber.</returns>
      public static Fiber[] Slice(Region region, int nx = 0, int ny = 0)
      {
         if (nx == 0 && ny == 0)
            throw new ArgumentException("Необходимо задать хотя бы один из параметров: nx или ny.");

         var hullPts = new List<(double X, double Y)>();
         for (int i = 0; i < region.Hull.X.Count - 1; i++)
            hullPts.Add((region.Hull.X[i], region.Hull.Y[i]));

         double xMin = hullPts.Min(p => p.X);
         double xMax = hullPts.Max(p => p.X);
         double yMin = hullPts.Min(p => p.Y);
         double yMax = hullPts.Max(p => p.Y);
         double B = xMax - xMin;
         double H = yMax - yMin;

         List<double> xLines, yLines;
         if (nx > 0)
         {
            double dx = B / nx;
            xLines = GridLines(xMin, xMax, dx, xMin);
         }
         else
         {
            xLines = [xMin, xMax];
         }

         if (ny > 0)
         {
            double dy = H / ny;
            yLines = GridLines(yMin, yMax, dy, yMin);
         }
         else
         {
            yLines = [yMin, yMax];
         }

         var holesPts = new List<List<(double X, double Y)>>();
         if (region.Holes != null)
         {
            foreach (var hole in region.Holes)
            {
               var hPts = new List<(double X, double Y)>();
               for (int i = 0; i < hole.X.Count - 1; i++)
                  hPts.Add((hole.X[i], hole.Y[i]));
               holesPts.Add(hPts);
            }
         }

         double E = region.Material == null ? 0 : region.Material.E;
         var fibers = new List<Fiber>();
         int fiberNum = 1;

         for (int i = 0; i < xLines.Count - 1; i++)
         {
            double cx0 = xLines[i];
            double cx1 = xLines[i + 1];
            if (cx1 - cx0 < 1e-12) continue;

            for (int j = 0; j < yLines.Count - 1; j++)
            {
               double cy0 = yLines[j];
               double cy1 = yLines[j + 1];
               if (cy1 - cy0 < 1e-12) continue;

               var clippedOuter = ClipByRect(hullPts, cx0, cx1, cy0, cy1);
               if (clippedOuter.Count < 3) continue;

               double areaOuter = SignedArea(clippedOuter);
               if (areaOuter < MinArea) continue;

               var clippedHoles = new List<List<(double X, double Y)>>();
               foreach (var holePts in holesPts)
               {
                  // Отверстия — CW, инвертируем для обрезки
                  var holeReversed = new List<(double X, double Y)>(holePts);
                  holeReversed.Reverse();
                  var ch = ClipByRect(holeReversed, cx0, cx1, cy0, cy1);
                  if (ch.Count < 3) continue;
                  double ah = Math.Abs(SignedArea(ch));
                  // Возвращаем исходный обход (CW) для отверстия
                  ch.Reverse();
                  if (ah < MinArea) continue;
                  clippedHoles.Add(ch);
               }

               clippedOuter = RemoveSpikes(clippedOuter);
               if (clippedOuter.Count < 3) continue;

               var outerParts = SplitWoundPolygon(clippedOuter, cx0, cx1, cy0, cy1);
               if (outerParts.Count == 0) continue;

               foreach (var partVerts in outerParts)
               {
                  if (partVerts.Count < 3) continue;

                  List<List<(double X, double Y)>> partHoles;
                  if (outerParts.Count == 1)
                  {
                     partHoles = clippedHoles;
                  }
                  else
                  {
                     var cent = CentroidXY(partVerts);
                     partHoles = clippedHoles.Where(h => PointInPoly(cent.X, cent.Y, h)).ToList();
                  }

                  double netArea = NetArea(partVerts, partHoles.Count > 0 ? partHoles : null);
                  if (netArea < MinArea) continue;

                  var (cx, cy) = NetCentroid(partVerts, partHoles.Count > 0 ? partHoles : null);

                  string wkt = PolygonWKT(partVerts, partHoles.Count > 0 ? partHoles : null);

                  FiberType ft = partVerts.Count > 4 ? FiberType.poly : FiberType.tri;

                  var fa = new Fiber(fiberNum++, region.Tag, wkt)
                  {
                     X = cx,
                     Y = cy,
                     Area = Math.Abs(netArea),
                     E = E,
                     TypeFiber = ft
                  };
                  fibers.Add(fa);
               }
            }
         }

         return fibers.ToArray();
      }

      /// <summary>
      /// Разбивает область на волокна методом нарезки прямоугольной сеткой
      /// по осям X и Y.
      /// </summary>
      public static Fiber[] SliceXY(Region region, int nx = 40, int ny = 40)
      {
         return Slice(region, nx, ny);
      }

      /// <summary>
      /// Разбивает область на волокна методом нарезки горизонтальными полосами (по Y).
      /// </summary>
      public static Fiber[] SliceY(Region region, int ny = 40)
      {
         return Slice(region, 0, ny);
      }

      /// <summary>
      /// Разбивает область на волокна методом нарезки вертикальными полосами (по X).
      /// </summary>
      public static Fiber[] SliceX(Region region, int nx = 40)
      {
         return Slice(region, nx, 0);
      }

      static string PolygonWKT(List<(double X, double Y)> outer, List<List<(double X, double Y)>> holes)
      {
         var sb = new System.Text.StringBuilder();
         sb.Append("POLYGON (");

         sb.Append('(');
         for (int i = 0; i < outer.Count; i++)
            sb.Append($"{outer[i].X} {outer[i].Y}{(i < outer.Count - 1 ? ", " : "")}");
         sb.Append(')');

         if (holes != null)
         {
            foreach (var hole in holes)
            {
               sb.Append(", (");
               for (int i = 0; i < hole.Count; i++)
                  sb.Append($"{hole[i].X} {hole[i].Y}{(i < hole.Count - 1 ? ", " : "")}");
               sb.Append(')');
            }
         }

         sb.Append(')');
         return sb.ToString();
      }
   }
}