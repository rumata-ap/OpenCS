using System.Collections.Generic;
using System.Linq;

namespace CSTriangulation
{
   /// <summary>Финальная очистка треугольников внутри отверстий (§3.4).</summary>
   internal static class HoleRemoval
   {
      public static void RemoveTrianglesInHoles(List<double[]> nodes, List<(int, int, int)> triangles, List<List<int>> holeLoops)
      {
         if (holeLoops.Count == 0) return;

         var holePolys = holeLoops
            .Select(loop => loop.Select(idx => (nodes[idx][0], nodes[idx][1])).ToList())
            .ToList();

         triangles.RemoveAll(tri =>
         {
            double cx = (nodes[tri.Item1][0] + nodes[tri.Item2][0] + nodes[tri.Item3][0]) / 3.0;
            double cy = (nodes[tri.Item1][1] + nodes[tri.Item2][1] + nodes[tri.Item3][1]) / 3.0;
            return holePolys.Any(poly => GeometryUtils.PointInPolygon(cx, cy, poly));
         });
      }
   }
}
