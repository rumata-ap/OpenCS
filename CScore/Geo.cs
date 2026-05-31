using TriangleNet.Geometry;
using TriangleMesh = TriangleNet.Meshing;
using TriangleGeo = TriangleNet.Geometry;

namespace CScore
{
   /// <summary>
   /// Статический класс с методами геометрического разбиения области сечения
   /// на конечные элементы (волокна). Поддерживает триангуляцию (Triangle.NET)
   /// и нарезку по осям X и Y (алгоритм Сазерленда–Ходжмана).
   /// </summary>
   public static class Geo
   {
      /// <summary>
      /// Разбивает область на волокна методом нарезки прямоугольной сеткой
      /// по осям X и Y с помощью алгоритма Сазерленда–Ходжмана.
      /// </summary>
      /// <param name="region">Область сечения для разбиения.</param>
      /// <param name="nx">Количество участков деления по оси X (по умолчанию 40).</param>
      /// <param name="ny">Количество участков деления по оси Y (по умолчанию 40).</param>
      /// <returns>Массив волокон <see cref="Fiber"/>, покрывающих область.</returns>
      /// <exception cref="Exception">Выбрасывается, если nx или ny меньше 2, или если область не содержит внешнего контура.</exception>
      public static Fiber[] SliceXY(Region region, int nx = 40, int ny = 40)
      {
         return GridSplit.SliceXY(region, nx, ny);
      }

      /// <summary>
      /// Разбивает область на волокна методом нарезки горизонтальными полосами (по оси Y).
      /// </summary>
      /// <param name="region">Область сечения для разбиения.</param>
      /// <param name="ny">Количество участков деления по оси Y (по умолчанию 40).</param>
      /// <returns>Массив волокон <see cref="Fiber"/>, упорядоченных по Y.</returns>
      /// <exception cref="Exception">Выбрасывается, если ny меньше 2.</exception>
      public static Fiber[] SliceY(Region region, int ny = 40)
      {
         return GridSplit.SliceY(region, ny);
      }

      /// <summary>
      /// Разбивает область на волокна методом нарезки вертикальными полосами (по оси X).
      /// </summary>
      /// <param name="region">Область сечения для разбиения.</param>
      /// <param name="nx">Количество участков деления по оси X (по умолчанию 40).</param>
      /// <returns>Массив волокон <see cref="Fiber"/>, упорядоченных по X.</returns>
      /// <exception cref="Exception">Выбрасывается, если nx меньше 2.</exception>
      public static Fiber[] SliceX(Region region, int nx = 40)
      {
         return GridSplit.SliceX(region, nx);
      }

      /// <summary>
      /// Разбивает область на треугольные волокна методом триангуляции Делоне
      /// с использованием библиотеки Triangle.NET. Поддерживает отверстия в сечении.
      /// После триангуляции применяется сглаживание Ллойда (20 итераций).
      /// </summary>
      /// <param name="region">Область сечения для триангуляции.</param>
      /// <param name="maxTrgArea">Максимальная площадь треугольника как доля от площади области (по умолчанию 0.01).</param>
      /// <param name="maxAngl">Минимальный угол треугольника в градусах (по умолчанию 30).</param>
      /// <param name="scale">Масштабный коэффициент координат для улучшения качества триангуляции (по умолчанию 8).</param>
      /// <returns>Массив треугольных волокон <see cref="Fiber"/>.</returns>
      public static Fiber[] Triangulation(Region region, double maxTrgArea = 0.01, double maxAngl = 30, double scale = 8)
      {
         List<Vertex> vrtxs = [];
         Vertex vrtx;
         int i = 1;

         for (int j = 0; j < region.Hull.Points.Count - 1; j++)
         {
            vrtx = new Vertex(region.Hull.X[j] * scale, region.Hull.Y[j] * scale, i)
            {
               ID = i - 1
            };
            vrtxs.Add(vrtx);
            i++;
         }

         TriangleGeo.Contour cnt = new(vrtxs);
         TriangleGeo.Polygon polygon = new();
         polygon.Add(cnt, false);

         if (region.Contours.Count > 1)
         {
            var h = region.Holes;
            for (int k = 0; k < h.Count; k++)
            {
               Contour hole = h[k];
               vrtxs = [];
               for (int j = 0; j < hole.Points.Count - 1; j++)
               {
                  vrtx = new Vertex(hole.X[j] * scale, hole.Y[j] * scale, i)
                  {
                     ID = i - 1
                  };
                  vrtxs.Add(vrtx);
                  i++;
               }
               cnt = new TriangleGeo.Contour(vrtxs);
               polygon.Add(cnt, true);
            }
         }
         TriangleMesh.GenericMesher mesher = new TriangleMesh.GenericMesher();
         TriangleMesh.QualityOptions quality = new TriangleMesh.QualityOptions();
         TriangleMesh.ConstraintOptions constraint = new TriangleMesh.ConstraintOptions();

         double hullArea = WktHelper.PolygonArea(region.Hull.X, region.Hull.Y);
         quality.MaximumArea = hullArea * maxTrgArea;
         quality.MinimumAngle = maxAngl;
         constraint.ConformingDelaunay = true;
         TriangleMesh.IMesh mesh = mesher.Triangulate(polygon, constraint, quality);
         mesh.Refine(quality, true);
         TriangleNet.Smoothing.SimpleSmoother smoother = new TriangleNet.Smoothing.SimpleSmoother();
         smoother.Smooth(mesh, 20);

         double E = region.Material == null ? 0 : region.Material.E;
         List<TriangleNet.Topology.Triangle> triangles = new List<TriangleNet.Topology.Triangle>(mesh.Triangles);
         List<Fiber> fas = new List<Fiber>(triangles.Count);
         i = 1;
         foreach (TriangleNet.Topology.Triangle tri in triangles)
         {
            double x0 = tri.GetVertex(0).X / scale, y0 = tri.GetVertex(0).Y / scale;
            double x1 = tri.GetVertex(1).X / scale, y1 = tri.GetVertex(1).Y / scale;
            double x2 = tri.GetVertex(2).X / scale, y2 = tri.GetVertex(2).Y / scale;

            double cx = (x0 + x1 + x2) / 3.0;
            double cy = (y0 + y1 + y2) / 3.0;
            double area = Math.Abs((x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)) / 2.0;

            string wkt = $"POLYGON (({x0} {y0}, {x1} {y1}, {x2} {y2}, {x0} {y0}))";

            Fiber fa = new Fiber(i, $"{region.Tag}", wkt)
            {
               X = cx,
               Y = cy,
               Area = area,
               E = E,
               TypeFiber = FiberType.tri
            };
            fas.Add(fa); i++;
         }

         return [.. fas];
      }
   }
}