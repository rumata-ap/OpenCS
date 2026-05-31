using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CScore
{
   /// <summary>
   /// Утилиты для чтения и записи WKT (Well-Known Text) без внешних зависимостей.
   /// </summary>
   public static class WktHelper
   {
      public static string PolygonToWKT(IList<double> xs, IList<double> ys,
         List<List<(double X, double Y)>> holes)
      {
         var sb = new StringBuilder();
         sb.Append("POLYGON (");

         sb.Append('(');
         for (int i = 0; i < xs.Count; i++)
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0} {1}{2}", xs[i], ys[i], i < xs.Count - 1 ? ", " : "");
         sb.Append(')');

         if (holes != null)
         {
            foreach (var hole in holes)
            {
               sb.Append(", (");
               for (int i = 0; i < hole.Count; i++)
                  sb.AppendFormat(CultureInfo.InvariantCulture, "{0} {1}{2}", hole[i].X, hole[i].Y, i < hole.Count - 1 ? ", " : "");
               sb.Append(')');
            }
         }

         sb.Append(')');
         return sb.ToString();
      }

      public static string LinearRingToWKT(IList<double> xs, IList<double> ys)
      {
         var sb = new StringBuilder();
         sb.Append("LINEARRING (");
         for (int i = 0; i < xs.Count; i++)
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0} {1}{2}", xs[i], ys[i], i < xs.Count - 1 ? ", " : "");
         sb.Append(')');
         return sb.ToString();
      }

      public static void ParseWKTPolygon(string wkt,
         out List<double> outerX, out List<double> outerY,
         out List<List<double>> holeXs, out List<List<double>> holeYs)
      {
         outerX = [];
         outerY = [];
         holeXs = [];
         holeYs = [];

         if (string.IsNullOrWhiteSpace(wkt)) return;

         string s = wkt.Trim();
         if (!s.StartsWith("POLYGON", StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"Expected POLYGON, got: {s}");

         int parenStart = s.IndexOf('(');
         if (parenStart < 0) throw new FormatException("Invalid WKT: no parentheses");

         s = s.Substring(parenStart + 1);
         s = s.TrimEnd(')');

         var rings = SplitRings(s);
         if (rings.Count == 0) return;

         ParseRing(rings[0], outerX, outerY);
         for (int i = 1; i < rings.Count; i++)
         {
            var hx = new List<double>();
            var hy = new List<double>();
            ParseRing(rings[i], hx, hy);
            holeXs.Add(hx);
            holeYs.Add(hy);
         }
      }

      public static void ParseWKTLinearRing(string wkt, out List<double> xs, out List<double> ys)
      {
         xs = [];
         ys = [];

         if (string.IsNullOrWhiteSpace(wkt)) return;

         string s = wkt.Trim();
         if (!s.StartsWith("LINEARRING", StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"Expected LINEARRING, got: {s}");

         int parenStart = s.IndexOf('(');
         if (parenStart < 0) throw new FormatException("Invalid WKT: no parentheses");

         s = s.Substring(parenStart + 1).TrimEnd(')');
         ParseCoordList(s, xs, ys);
      }

      static List<string> SplitRings(string s)
      {
         var rings = new List<string>();
         int depth = 0;
         int start = -1;
         for (int i = 0; i < s.Length; i++)
         {
            if (s[i] == '(')
            {
               depth++;
               if (depth == 1) start = i + 1;
            }
            else if (s[i] == ')')
            {
               depth--;
               if (depth == 0 && start >= 0)
               {
                  rings.Add(s.Substring(start, i - start));
                  start = -1;
               }
            }
         }
         return rings;
      }

      static void ParseRing(string ring, List<double> xs, List<double> ys)
      {
         ParseCoordList(ring, xs, ys);
      }

      static void ParseCoordList(string s, List<double> xs, List<double> ys)
      {
         var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries);
         foreach (var part in parts)
         {
            var coords = part.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (coords.Length >= 2)
            {
               xs.Add(double.Parse(coords[0], CultureInfo.InvariantCulture));
               ys.Add(double.Parse(coords[1], CultureInfo.InvariantCulture));
            }
         }
      }

      public static void Envelope(IList<double> xs, IList<double> ys,
         out double xMin, out double xMax, out double yMin, out double yMax)
      {
         xMin = double.MaxValue; xMax = double.MinValue;
         yMin = double.MaxValue; yMax = double.MinValue;
         for (int i = 0; i < xs.Count; i++)
         {
            if (xs[i] < xMin) xMin = xs[i];
            if (xs[i] > xMax) xMax = xs[i];
            if (ys[i] < yMin) yMin = ys[i];
            if (ys[i] > yMax) yMax = ys[i];
         }
      }

      public static double PolygonArea(IList<double> xs, IList<double> ys)
      {
         double area = 0;
         int n = xs.Count;
         for (int i = 0; i < n - 1; i++)
            area += xs[i] * ys[i + 1] - xs[i + 1] * ys[i];
         return Math.Abs(0.5 * area);
      }

      public static (double cx, double cy) PolygonCentroid(IList<double> xs, IList<double> ys)
      {
         double A6 = 0, cxN = 0, cyN = 0;
         int n = xs.Count;
         for (int i = 0; i < n - 1; i++)
         {
            double c = xs[i] * ys[i + 1] - xs[i + 1] * ys[i];
            A6 += c;
            cxN += (xs[i] + xs[i + 1]) * c;
            cyN += (ys[i] + ys[i + 1]) * c;
         }
         if (Math.Abs(A6) < 1e-14) return (0, 0);
         return (cxN / (3.0 * A6), cyN / (3.0 * A6));
      }
   }
}