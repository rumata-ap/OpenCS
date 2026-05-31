using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Polygonize;
using TriangleNet.Geometry;
using TriangleMesh = TriangleNet.Meshing;
using TriangleGeo = TriangleNet.Geometry;
using Topo = NetTopologySuite.Geometries;
using NetTopologySuite.Index.HPRtree;
using System.Reflection;

namespace CScore
{
   /// <summary>
   /// Статический класс с методами геометрического разбиения области сечения
   /// на конечные элементы (волокна). Поддерживает триангуляцию и нарезку
   /// по осям X и Y с помощью библиотек NetTopologySuite и Triangle.NET.
   /// </summary>
   public static class Geo
   {
      /// <summary>
      /// Разрезает полигон линией и возвращает результирующие полигоны,
      /// которые лежат внутри исходного полигона.
      /// </summary>
      /// <param name="polygon">Исходный полигон.</param>
      /// <param name="line">Разрезающая линия.</param>
      /// <returns>Коллекция полигонов, образовавшихся после разреза.</returns>
      public static Geometry SplitPolygon(Geometry polygon, Geometry line)
      {
         var nodedLinework = polygon.Boundary.Union(line);
         var polygons = Polygonize(nodedLinework);

         // сохранять только полигоны, находящиеся внутри ввода
         var output = new List<Geometry>();
         for (int i = 0; i < polygons.NumGeometries; i++)
         {
            var candpoly = (Topo.Polygon)polygons.GetGeometryN(i);
            if (polygon.Contains(candpoly.InteriorPoint))
               output.Add(candpoly);
         }

         return polygon.Factory.BuildGeometry(output);
      }

      /// <summary>
      /// Разбивает мультиполигон на отдельные полигоны с помощью Polygonizer.
      /// </summary>
      static Geometry Polygonize(Geometry geometry)
      {
         var lines = LineStringExtracter.GetLines(geometry);
         var polygonizer = new Polygonizer(false);
         polygonizer.Add(lines);
         var polys = new List<Geometry>(polygonizer.GetPolygons());

         var polyArray = GeometryFactory.ToGeometryArray(polys);
         return geometry.Factory.BuildGeometry(polyArray);
      }


      /// <summary>
      /// Разбивает область на волокна методом нарезки прямоугольной сеткой
      /// по осям X и Y. Каждый результирующий волоконный элемент — это полигон,
      /// полученный пересечением исходной области с ячейкой сетки.
      /// </summary>
      /// <param name="region">Область сечения для разбиения.</param>
      /// <param name="nx">Количество участков деления по оси X (по умолчанию 40).</param>
      /// <param name="ny">Количество участков деления по оси Y (по умолчанию 40).</param>
      /// <returns>Массив волокон <see cref="Fiber"/>, покрывающих область.</returns>
      /// <exception cref="Exception">Выбрасывается, если nx или ny меньше 2, или если область не содержит внешнего контура.</exception>
      public static Fiber[] SliceXY(Region region, int nx = 40, int ny = 40)
      {
         if (nx < 2 || ny < 2) throw new Exception("Количество участков деления болжно быть больше 1.");
         if (region.Hull == null) throw new Exception("Область не содержит внешнего контура.");
         List<double> xv = new(region.Hull.X);
         List<double> yv = new(region.Hull.Y);
         double ymin = yv.Min();
         double ymax = yv.Max();
         double xmin = xv.Min();
         double xmax = xv.Max();
         double H = yv.Max() - yv.Min();
         double B = xv.Max() - xv.Min();
         double dh = H / ny;
         double db = B / nx;

         LineString[] ptsSLx = new LineString[nx - 1];
         LineString[] ptsSLy = new LineString[ny - 1];
         for (int i = 0; i < ny - 1; i++)
         {
            Coordinate[] p = [new(2 * xmin, ymin + dh * (i + 1)), new(2 * xmax, ymin + dh * (i + 1))];
            ptsSLy[i] = new LineString(p);
         }
         for (int i = 0; i < nx - 1; i++)
         {
            Coordinate[] p = [new(xmin + db * (i + 1), 2 * ymin), new(xmin + db * (i + 1), 2 * ymax)];
            ptsSLx[i] = new LineString(p);
         }

         MultiPolygon? polygons = null;
         MultiPolygon master = new([region.Polygon]);
         List<Topo.Polygon> geometries = [];

         for (int i = 0; i < ptsSLy.Length; i++)
         {
            polygons = SplitPolygon(master, ptsSLy[i]) as MultiPolygon;
            List<double> Y = (from g in polygons.Geometries select g.Centroid.Y).ToList();
            double y = Y.Max();
            List<Topo.Polygon> work = (from g in polygons where Math.Round(y, 6) == Math.Round(g.Centroid.Y, 6) select (Topo.Polygon)g).ToList();
            List<Topo.Polygon> res = (from g in polygons where Math.Round(y, 6) > Math.Round(g.Centroid.Y, 6) select (Topo.Polygon)g).ToList();
            geometries.AddRange(res);
            master = new MultiPolygon([.. work]);
            if (i == ptsSLy.Length - 1) geometries.AddRange(work);
         }

         var orderedPolys = from i in geometries
                            orderby i.Centroid.Y ascending
                            select i;
         List<Topo.Polygon> polysY = new(orderedPolys);
         List<Topo.Polygon> polys = [];

         for (int j = 0; j < polysY.Count; j++)
         {
            geometries = [];
            master = new MultiPolygon([polysY[j]]);
            int k = 0;
            for (int i = 0; i < ptsSLx.Length; i++)
            {
               try
               {
                  polygons = SplitPolygon(master, ptsSLx[i]) as MultiPolygon;
               }
               catch { continue; }
               if (polygons == null && k == 0) continue;
               if (polygons == null && k != 0) { geometries.Add((Topo.Polygon)master.Geometries[0]); break; }
               Topo.Polygon one = (Topo.Polygon)polygons.Geometries[0];
               Topo.Polygon two = (Topo.Polygon)polygons.Geometries[1];
               if (one.Centroid.X > two.Centroid.X)
               {
                  master = new MultiPolygon([one]);
                  geometries.Add(two);
               }
               else
               {
                  master = new MultiPolygon([two]);
                  geometries.Add(one);
               }
               k++;
               if (i == ptsSLx.Length - 1) geometries.Add((Topo.Polygon)master.Geometries[0]);
            }
            orderedPolys = from i in geometries orderby i.Centroid.X ascending select i;
            polys.AddRange(orderedPolys);
         }

         double E = region.Material == null ? 0 : region.Material.E;
         Fiber[] fas = new Fiber[polys.Count];
         for (int i = 0; i < fas.Length; i++)
         {
            fas[i] = new Fiber(i + 1, $"{region.Tag}", polys[i]);
            fas[i].E = E;
            if (polys[i].Coordinates.Length > 4) fas[i].TypeFiber = FiberType.poly;
            else fas[i].TypeFiber = FiberType.tri;
         }

         return fas;
      }

      /// <summary>
      /// Разбивает область на волокна методом нарезки горизонтальными полосами (по оси Y).
      /// Каждый результирующий волоконный элемент — это полигон,
      /// полученный пересечением исходной области с горизонтальной полосой.
      /// </summary>
      /// <param name="region">Область сечения для разбиения.</param>
      /// <param name="ny">Количество участков деления по оси Y (по умолчанию 40).</param>
      /// <returns>Массив волокон <see cref="Fiber"/>, упорядоченных по Y.</returns>
      /// <exception cref="Exception">Выбрасывается, если ny меньше 2.</exception>
      public static Fiber[] SliceY(Region region, int ny = 40)
      {
         if (ny < 2) throw new Exception("Количество участков деления болжно быть больше 1.");
         List<double> xv = new(region.Hull.X);
         List<double> yv = new List<double>(region.Hull.Y);
         double ymin = yv.Min();
         double ymax = yv.Max();
         double xmin = xv.Min();
         double xmax = xv.Max();
         double H = yv.Max() - yv.Min();
         double dh = H / ny;

         LineString[] ptsSLy = new LineString[ny - 1];
         for (int i = 0; i < ny - 1; i++)
         {
            Coordinate[] p = { new(2 * xmin, ymin + dh * (i + 1)), new Coordinate(2 * xmax, ymin + dh * (i + 1)) };
            ptsSLy[i] = new LineString(p);
         }

         MultiPolygon polygons = null;
         MultiPolygon master = new MultiPolygon(new Topo.Polygon[] { region.Polygon });
         List<Topo.Polygon> geometries = new List<Topo.Polygon>();

         for (int i = 0; i < ptsSLy.Length; i++)
         {
            polygons = SplitPolygon(master, ptsSLy[i]) as MultiPolygon;
            List<double> Y = (from g in polygons.Geometries select g.Centroid.Y).ToList();
            double y = Y.Max();
            List<Topo.Polygon> work = (from g in polygons where Math.Round(y, 6) == Math.Round(g.Centroid.Y, 6) select (Topo.Polygon)g).ToList();
            List<Topo.Polygon> res = (from g in polygons where Math.Round(y, 6) > Math.Round(g.Centroid.Y, 6) select (Topo.Polygon)g).ToList();
            geometries.AddRange(res);
            master = new MultiPolygon(work.ToArray());
            if (i == ptsSLy.Length - 1) geometries.AddRange(work);
         }

         var orderedPolys = from i in geometries
                            orderby i.Centroid.Y ascending
                            select i;
         List<Topo.Polygon> polysY = new(orderedPolys);

         double E = region.Material == null ? 0 : region.Material.E;
         Fiber[] fas = new Fiber[polysY.Count];
         for (int i = 0; i < fas.Length; i++)
         {
            fas[i] = new Fiber(i + 1, $"{region.Tag}", polysY[i]);
            fas[i].E = E;
            if (polysY[i].Coordinates.Length > 4) fas[i].TypeFiber = FiberType.poly;
            else fas[i].TypeFiber = FiberType.tri;
         }

         return fas;
      }

      /// <summary>
      /// Разбивает область на волокна методом нарезки вертикальными полосами (по оси X).
      /// Каждый результирующий волоконный элемент — это полигон,
      /// полученный пересечением исходной области с вертикальной полосой.
      /// </summary>
      /// <param name="region">Область сечения для разбиения.</param>
      /// <param name="nx">Количество участков деления по оси X (по умолчанию 40).</param>
      /// <returns>Массив волокон <see cref="Fiber"/>, упорядоченных по X.</returns>
      /// <exception cref="Exception">Выбрасывается, если nx меньше 2.</exception>
      public static Fiber[] SliceX(Region region, int nx = 40)
      {
         if (nx < 2) throw new Exception("Количество участков деления болжно быть больше 1.");
         List<double> xv = new List<double>(region.Hull.X);
         List<double> yv = new List<double>(region.Hull.Y);
         double ymin = yv.Min();
         double ymax = yv.Max();
         double xmin = xv.Min();
         double xmax = xv.Max();
         double B = xv.Max() - xv.Min();
         double db = B / nx;

         LineString[] ptsSLx = new LineString[nx - 1];
         for (int i = 0; i < nx - 1; i++)
         {
            Coordinate[] p = { new Coordinate(xmin + db * (i + 1), 2 * ymin), new Coordinate(xmin + db * (i + 1), 2 * ymax) };
            ptsSLx[i] = new LineString(p);
         }

         MultiPolygon polygons = null;
         MultiPolygon master = new MultiPolygon(new Topo.Polygon[] { region.Polygon });
         List<Topo.Polygon> geometries = new List<Topo.Polygon>();

         for (int i = 0; i < ptsSLx.Length; i++)
         {
            polygons = SplitPolygon(master, ptsSLx[i]) as MultiPolygon;
            List<double> X = (from g in polygons.Geometries select g.Centroid.X).ToList();
            double x = X.Max();
            List<Topo.Polygon> work = (from g in polygons where Math.Round(x, 6) == Math.Round(g.Centroid.X, 6) select (Topo.Polygon)g).ToList();
            List<Topo.Polygon> res = (from g in polygons where Math.Round(x, 6) > Math.Round(g.Centroid.X, 6) select (Topo.Polygon)g).ToList();
            geometries.AddRange(res);
            master = new MultiPolygon(work.ToArray());
            if (i == ptsSLx.Length - 1) geometries.AddRange(work);
         }

         var orderedPolys = from i in geometries
                            orderby i.Centroid.X ascending
                            select i;
         List<Topo.Polygon> polysX = new List<Topo.Polygon>(orderedPolys);

         double E = region.Material == null ? 0 : region.Material.E;
         Fiber[] fas = new Fiber[polysX.Count];
         for (int i = 0; i < fas.Length; i++)
         {
            fas[i] = new Fiber(i + 1, $"{region.Tag}", polysX[i]);
            fas[i].E = E;
            if (polysX[i].Coordinates.Length > 4) fas[i].TypeFiber = FiberType.poly;
            else fas[i].TypeFiber = FiberType.tri;
         }

         return fas;
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

         quality.MaximumArea = region.Polygon.Area * maxTrgArea;
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
            XY pt1 = new(tri.GetVertex(0).X / scale, tri.GetVertex(0).Y / scale);
            XY pt2 = new(tri.GetVertex(1).X / scale, tri.GetVertex(1).Y / scale);
            XY pt3 = new(tri.GetVertex(2).X / scale, tri.GetVertex(2).Y / scale);

            Topo.Polygon trg = new(new LinearRing(
               [pt1.Coordinate, pt2.Coordinate, pt3.Coordinate, pt1.Coordinate]));

            Fiber fa = new(i, $"{region.Tag}", trg) { TypeFiber = FiberType.tri};
            fa.E = E;
            fas.Add(fa); i++;
         }

         return [.. fas];
      }
   }
}