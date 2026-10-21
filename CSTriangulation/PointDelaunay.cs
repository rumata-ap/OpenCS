using TriangleNet.Geometry;
using TriangleMesh = TriangleNet.Meshing;

namespace CSTriangulation
{
   /// <summary>Триангуляция Делоне облака точек (выпуклая оболочка, без сегментов) на Triangle.NET.</summary>
   public static class PointDelaunay
   {
      /// <summary>
      /// Тройки индексов входных точек (длина кратна 3) или null — меньше трёх точек, все на одной прямой или
      /// Triangle.NET не справился. Совпадающие точки дают индекс первой из них.
      /// </summary>
      public static int[]? Triangulate(IReadOnlyList<(double X, double Y)> points)
      {
         if (points.Count < 3) return null;
         var index = new Dictionary<(double, double), int>();
         for (int i = 0; i < points.Count; i++) index.TryAdd(points[i], i);
         // Свой экземпляр предикатов: общий RobustPredicates.Default ломается при параллельных триангуляциях.
         var config = new TriangleNet.Configuration(
            () => new TriangleNet.RobustPredicates(),
            () => new TriangleNet.TrianglePool());
         TriangleMesh.IMesh mesh;
         try
         {
            mesh = new TriangleMesh.GenericMesher(config).Triangulate(points.Select(p => new Vertex(p.X, p.Y)).ToList());
         }
         catch (Exception) { return null; }
         var tri = new List<int>();
         var ids = new int[3];
         foreach (var t in mesh.Triangles)
         {
            bool ok = true;
            for (int k = 0; k < 3 && ok; k++)
            {
               var v = t.GetVertex(k);
               ok = index.TryGetValue((v.X, v.Y), out ids[k]);
            }
            if (ok) tri.AddRange(ids);
         }
         return tri.Count == 0 ? null : tri.ToArray();
      }
   }
}
