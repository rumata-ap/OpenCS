using System;
using System.Collections.Generic;

namespace CSTriangulation
{
   /// <summary>
   /// Оптимизация треугольной сетки: Laplacian smoothing + edge flipping + дробление длинных сторон.
   /// </summary>
   public static class Optimize
   {
      /// <summary>
      /// Оптимизирует треугольную сетку: сглаживание, флип диагоналей, дробление длинных рёбер.
      /// </summary>
      /// <param name="result">Результат триангуляции.</param>
      /// <param name="nIter">Число итераций сглаживания Лапласа.</param>
      /// <param name="chi">Макс. отношение длин сторон для дробления (2.0 = по умолчанию).</param>
      /// <returns>Оптимизированный результат триангуляции.</returns>
      public static TriangulationResult OptimizeTriangular(TriangulationResult result, int nIter = 5, double chi = 2.0)
      {
         if (result.Triangles.Length == 0) return result;

         var nodes = new List<double[]>(result.Nodes.Length);
         foreach (var n in result.Nodes) nodes.Add((double[])n.Clone());
         var isBoundary = new List<bool>(result.IsBoundary.Length);
         foreach (var b in result.IsBoundary) isBoundary.Add(b);
         var triangles = new List<int[]>(result.Triangles.Length);
         foreach (var t in result.Triangles) triangles.Add((int[])t.Clone());

         for (int i = 0; i < nIter; i++)
            LaplacianSmooth(nodes, triangles, isBoundary);

         int maxFlips = Math.Max(30, triangles.Count * 3);
         FlipEdges(nodes, triangles, isBoundary, maxFlips);

         if (chi < 100)
            SplitLongEdges(nodes, triangles, isBoundary, chi);

         int maxFlips2 = Math.Max(30, triangles.Count * 3);
         FlipEdges(nodes, triangles, isBoundary, maxFlips2);

         var compact = CompactAndBuild(nodes, isBoundary, triangles);
         return compact;
      }

      /// <summary>
      /// Одна итерация сглаживания Лапласа: внутренние узлы перемещаются
      /// в центр тяжести соседних элементов.
      /// </summary>
      static void LaplacianSmooth(List<double[]> nodes, List<int[]> triangles, List<bool> isBoundary)
      {
         int n = nodes.Count;
         var cx = new double[n];
         var cy = new double[n];
         var cnt = new int[n];

         foreach (var tri in triangles)
         {
            double centX = (nodes[tri[0]][0] + nodes[tri[1]][0] + nodes[tri[2]][0]) / 3.0;
            double centY = (nodes[tri[0]][1] + nodes[tri[1]][1] + nodes[tri[2]][1]) / 3.0;
            for (int k = 0; k < 3; k++)
            {
               int idx = tri[k];
               cx[idx] += centX;
               cy[idx] += centY;
               cnt[idx]++;
            }
         }

         for (int idx = 0; idx < n; idx++)
         {
            if (isBoundary[idx] || cnt[idx] == 0) continue;
            nodes[idx][0] = cx[idx] / cnt[idx];
            nodes[idx][1] = cy[idx] / cnt[idx];
         }
      }

      /// <summary>
      /// Lawson edge flipping: исправляет диагонали, нарушающие условие Делоне.
      /// </summary>
      static void FlipEdges(List<double[]> nodes, List<int[]> triangles, List<bool> isBoundary, int maxPasses)
      {
         for (int pass = 0; pass < maxPasses; pass++)
         {
            bool flipped = false;

            var edgeDict = new Dictionary<(int, int), List<(int ti, int a, int b, int opp)>>();
            for (int ti = 0; ti < triangles.Count; ti++)
            {
               var tri = triangles[ti];
               int a = tri[0], b = tri[1], c = tri[2];
               AddEdge(edgeDict, a, b, ti, a, b, c);
               AddEdge(edgeDict, b, c, ti, b, c, a);
               AddEdge(edgeDict, a, c, ti, a, c, b);
            }

            foreach (var kvp in edgeDict)
            {
               if (kvp.Value.Count != 2) continue;
               int i = kvp.Key.Item1, j = kvp.Key.Item2;
               int ti0 = kvp.Value[0].ti, a0 = kvp.Value[0].a, b0 = kvp.Value[0].b, k = kvp.Value[0].opp;
               int ti1 = kvp.Value[1].ti, a1 = kvp.Value[1].a, b1 = kvp.Value[1].b, l = kvp.Value[1].opp;

               if (isBoundary[i] && isBoundary[j]) continue;

               double angleK = AngleAt(k, i, j, nodes);
               double angleL = AngleAt(l, i, j, nodes);
               if (angleK + angleL <= Math.PI) continue;

               double sk = Orient2D(nodes[i], nodes[j], nodes[k]);
               double sl = Orient2D(nodes[i], nodes[j], nodes[l]);
               if (sk * sl >= 0) continue;

               var new0 = MakeCCW(i, k, l, nodes);
               var new1 = MakeCCW(j, l, k, nodes);
               triangles[ti0] = new0;
               triangles[ti1] = new1;
               flipped = true;
               break;
            }

            if (!flipped) break;
         }
      }

      static void AddEdge(Dictionary<(int, int), List<(int, int, int, int)>> dict, int a, int b, int ti, int ea, int eb, int opp)
      {
         var key = (Math.Min(a, b), Math.Max(a, b));
         if (!dict.ContainsKey(key)) dict[key] = new List<(int, int, int, int)>();
         dict[key].Add((ti, ea, eb, opp));
      }

      static double AngleAt(int apex, int v0, int v1, List<double[]> nodes)
      {
         double ax = nodes[v0][0] - nodes[apex][0], ay = nodes[v0][1] - nodes[apex][1];
         double bx = nodes[v1][0] - nodes[apex][0], by = nodes[v1][1] - nodes[apex][1];
         double dot = ax * bx + ay * by;
         double denom = Math.Sqrt((ax * ax + ay * ay) * (bx * bx + by * by)) + 1e-30;
         return Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot / denom)));
      }

      static double Orient2D(double[] a, double[] b, double[] c) =>
         (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);

      static int[] MakeCCW(int i, int j, int k, List<double[]> nodes)
      {
         double area2 = (nodes[j][0] - nodes[i][0]) * (nodes[k][1] - nodes[i][1]) -
                        (nodes[k][0] - nodes[i][0]) * (nodes[j][1] - nodes[i][1]);
         return area2 >= 0 ? [i, j, k] : [i, k, j];
      }

      /// <summary>
      /// Дробление сторон с отношением max/min &gt; chi.
      /// </summary>
      static void SplitLongEdges(List<double[]> nodes, List<int[]> triangles, List<bool> isBoundary, double chi)
      {
         var edgeCount = new Dictionary<(int, int), int>();
         foreach (var tri in triangles)
         {
            int a = tri[0], b = tri[1], c = tri[2];
            Incr(edgeCount, Math.Min(a, b), Math.Max(a, b));
            Incr(edgeCount, Math.Min(b, c), Math.Max(b, c));
            Incr(edgeCount, Math.Min(a, c), Math.Max(a, c));
         }

         var splitEdges = new HashSet<(int, int)>();
         foreach (var tri in triangles)
         {
            int a = tri[0], b = tri[1], c = tri[2];
            double da = Dist(nodes, a, b), db = Dist(nodes, b, c), dc = Dist(nodes, c, a);
            var sides = new[] { (da, a, b, c), (db, b, c, a), (dc, c, a, b) };
            double minSide = Math.Min(Math.Min(da, db), dc);
            if (minSide < 1e-10) continue;
            Array.Sort(sides, (x, y) => y.Item1.CompareTo(x.Item1));
            if (sides[0].Item1 / minSide > chi)
               splitEdges.Add((Math.Min(sides[0].Item2, sides[0].Item3), Math.Max(sides[0].Item2, sides[0].Item3)));
         }

         if (splitEdges.Count == 0) return;

         var edgeMidpoints = new Dictionary<(int, int), int>();
         var newTriangles = new List<int[]>();

         int MidpointFor((int, int) edgeKey)
         {
            if (!edgeMidpoints.TryGetValue(edgeKey, out int midIdx))
            {
               int ei = edgeKey.Item1, ej = edgeKey.Item2;
               double mx = (nodes[ei][0] + nodes[ej][0]) / 2.0;
               double my = (nodes[ei][1] + nodes[ej][1]) / 2.0;
               midIdx = nodes.Count;
               nodes.Add([mx, my]);
               bool bndEdge = edgeCount.GetValueOrDefault(edgeKey, 0) == 1;
               isBoundary.Add(bndEdge && isBoundary[ei] && isBoundary[ej]);
               edgeMidpoints[edgeKey] = midIdx;
            }
            return midIdx;
         }

         foreach (var tri in triangles)
         {
            int a = tri[0], b = tri[1], c = tri[2];
            var sideData = new[]
            {
               (a, b, c, (Math.Min(a, b), Math.Max(a, b))),
               (b, c, a, (Math.Min(b, c), Math.Max(b, c))),
               (c, a, b, (Math.Min(a, c), Math.Max(a, c)))
            };
            var selected = new List<(int i, int j, int k, (int, int) edge)>();
            foreach (var sd in sideData)
               if (splitEdges.Contains(sd.Item4)) selected.Add(sd);

            if (selected.Count == 0) { newTriangles.Add([a, b, c]); continue; }

            var mids = new Dictionary<(int, int), int>();
            foreach (var (_, _, _, edge) in selected)
               mids[edge] = MidpointFor(edge);

            if (selected.Count == 1)
            {
               int i2 = selected[0].i, j2 = selected[0].j, k2 = selected[0].k;
               int m = mids[selected[0].edge];
               AppendOriented(newTriangles, nodes, i2, m, k2);
               AppendOriented(newTriangles, nodes, m, j2, k2);
            }
            else if (selected.Count == 2)
            {
               var edges = new List<(int, int)>(mids.Keys);
                var common = new HashSet<int>();
               if (edges[0].Item1 == edges[1].Item1 || edges[0].Item1 == edges[1].Item2) common.Add(edges[0].Item1);
               if (edges[0].Item2 == edges[1].Item1 || edges[0].Item2 == edges[1].Item2) common.Add(edges[0].Item2);
               if (common.Count != 1) { newTriangles.Add([a, b, c]); continue; }
               int v = -1;
               foreach (var cv in common) v = cv;
               int m1 = mids[edges[0]], m2 = mids[edges[1]];
               int o1 = edges[0].Item1 == v ? edges[0].Item2 : edges[0].Item1;
               int o2 = edges[1].Item1 == v ? edges[1].Item2 : edges[1].Item1;
               AppendOriented(newTriangles, nodes, v, m1, m2);
               AppendOriented(newTriangles, nodes, m1, o1, o2);
               AppendOriented(newTriangles, nodes, m1, o2, m2);
            }
            else
            {
               int mab = mids[(Math.Min(a, b), Math.Max(a, b))];
               int mbc = mids[(Math.Min(b, c), Math.Max(b, c))];
               int mca = mids[(Math.Min(a, c), Math.Max(a, c))];
               AppendOriented(newTriangles, nodes, a, mab, mca);
               AppendOriented(newTriangles, nodes, mab, b, mbc);
               AppendOriented(newTriangles, nodes, mca, mbc, c);
               AppendOriented(newTriangles, nodes, mab, mbc, mca);
            }
         }

         triangles.Clear();
         triangles.AddRange(newTriangles);
      }

      static void Incr(Dictionary<(int, int), int> dict, int a, int b)
      {
         var key = (a, b);
         if (dict.ContainsKey(key)) dict[key]++; else dict[key] = 1;
      }

      static double Dist(List<double[]> nodes, int i, int j)
      {
         double dx = nodes[i][0] - nodes[j][0], dy = nodes[i][1] - nodes[j][1];
         return Math.Sqrt(dx * dx + dy * dy);
      }

      static void AppendOriented(List<int[]> triangles, List<double[]> nodes, int i, int j, int k)
      {
         double area2 = (nodes[j][0] - nodes[i][0]) * (nodes[k][1] - nodes[i][1]) -
                        (nodes[k][0] - nodes[i][0]) * (nodes[j][1] - nodes[i][1]);
         if (area2 < 0) triangles.Add([i, k, j]);
         else triangles.Add([i, j, k]);
      }

      static TriangulationResult CompactAndBuild(List<double[]> nodes, List<bool> isBoundary, List<int[]> triangles)
      {
         var referenced = new HashSet<int>();
         foreach (var tri in triangles)
            for (int k = 0; k < 3; k++) referenced.Add(tri[k]);

         var sorted = new List<int>(referenced);
         sorted.Sort();
         var old2new = new Dictionary<int, int>();
         for (int n = 0; n < sorted.Count; n++) old2new[sorted[n]] = n;

         var newNodes = new double[sorted.Count][];
         var newIsBnd = new bool[sorted.Count];
         for (int i = 0; i < sorted.Count; i++)
         {
            newNodes[i] = nodes[sorted[i]];
            newIsBnd[i] = isBoundary[sorted[i]];
         }

         var newTris = new int[triangles.Count][];
         for (int t = 0; t < triangles.Count; t++)
            newTris[t] = [old2new[triangles[t][0]], old2new[triangles[t][1]], old2new[triangles[t][2]]];

         return new TriangulationResult
         {
            Nodes = newNodes,
            Triangles = newTris,
            IsBoundary = newIsBnd
         };
      }
   }
}