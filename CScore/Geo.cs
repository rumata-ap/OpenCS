using System.Linq;
using CSTriangulation;
using Rupp = CSTriangulation.Ruppert;

namespace CScore
{
   /// <summary>
   /// Статический класс с методами геометрического разбиения области сечения
   /// на конечные элементы (волокна). Поддерживает триангуляцию (Рупперт или
   /// метод продвижения фронта) и нарезку по осям X и Y (Сазерленд–Ходжман).
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
      /// <exception cref="Exception">Выбрасывается, если nx или ny меньше 2.</exception>
      public static Fiber[] SliceXY(MaterialArea region, int nx = 40, int ny = 40)
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
      public static Fiber[] SliceY(MaterialArea region, int ny = 40)
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
      public static Fiber[] SliceX(MaterialArea region, int nx = 40)
      {
         return GridSplit.SliceX(region, nx);
      }

      /// <summary>
      /// Разбивает область на треугольные волокна методом триангуляции.
      /// По умолчанию используется алгоритм Рупперта (CDT + рефайнмент).
      /// Поддерживает отверстия в сечении. После триангуляции применяется
      /// сглаживание и оптимизация сетки.
      /// </summary>
      /// <param name="region">Область сечения для триангуляции.</param>
      /// <param name="maxTrgArea">Максимальная площадь треугольника как доля от площади области (по умолчанию 0.01).</param>
      /// <param name="maxAngl">Минимальный угол треугольника в градусах (по умолчанию 30).</param>
      /// <param name="scale">Масштабный коэффициент координат для улучшения качества триангуляции (по умолчанию 8).</param>
      /// <param name="method">Метод триангуляции (по умолчанию Ruppert).</param>
      /// <returns>Массив треугольных волокон <see cref="Fiber"/>.</returns>
      public static Fiber[] Triangulation(MaterialArea region, double maxTrgArea = 0.01, double maxAngl = 30, double scale = 8,
         double maxEdgeLen = 0, int smoothIter = 5,
         TriangulationMethod method = TriangulationMethod.Ruppert)
      {
         if (method == TriangulationMethod.AdvancingFront)
            return TriangulationAdvancingFront(region, maxTrgArea, scale, maxEdgeLen, smoothIter);
         else
            return TriangulationRuppert(region, maxTrgArea, maxAngl, scale, smoothIter);
      }

      /// <summary>
      /// Триангуляция алгоритмом Рупперта (CDT + рефайнмент).
      /// </summary>
      static Fiber[] TriangulationRuppert(MaterialArea region, double maxTrgArea, double maxAngl, double scale, int smoothIter = 5)
      {
         double hullArea = WktHelper.PolygonArea(region.Hull!.X, region.Hull.Y);
         double maxArea = hullArea * maxTrgArea * scale * scale;

         var outerPts = new Rupp.Vec2[region.Hull.Points.Count - 1];
         for (int j = 0; j < outerPts.Length; j++)
            outerPts[j] = new Rupp.Vec2(region.Hull.X[j] * scale, region.Hull.Y[j] * scale);

         var tri = new Rupp.Triangulator();
         tri.SetOuterPolygon(outerPts);

         if (region.Contours.Count > 1)
         {
            var h = region.Holes;
            for (int k = 0; k < h.Count; k++)
            {
               Contour hole = h[k];
               var holePts = new Rupp.Vec2[hole.Points.Count - 1];
               for (int j = 0; j < holePts.Length; j++)
                  holePts[j] = new Rupp.Vec2(hole.X[j] * scale, hole.Y[j] * scale);
               tri.AddHole(holePts);
            }
         }

         var parms = new Rupp.TriangulationParams
         {
            MinAngleDeg = maxAngl,
            MaxArea = maxArea,
            DoRefine = true
         };
         var result = tri.Triangulate(parms);

         var boundarySet = new System.Collections.Generic.HashSet<int>();
         foreach (var (a, b) in result.ConstrainedEdges) { boundarySet.Add(a); boundarySet.Add(b); }

         var optimized = Optimize.OptimizeTriangular(
            new TriangulationResult
            {
               Nodes = result.Vertices.Select(v => new double[] { v.X / scale, v.Y / scale }).ToArray(),
               Triangles = result.Triangles.Select(t => new int[] { t.Item1, t.Item2, t.Item3 }).ToArray(),
               IsBoundary = result.Vertices.Select((_, i) => boundarySet.Contains(i)).ToArray()
            },
            nIter: smoothIter,
            chi: 2.0
         );

         double E = region.Material == null ? 0 : region.Material.E;
         var fas = new List<Fiber>(optimized.Triangles.Length);
         int idx = 1;
         foreach (var triIdx in optimized.Triangles)
         {
            double x0 = optimized.Nodes[triIdx[0]][0], y0 = optimized.Nodes[triIdx[0]][1];
            double x1 = optimized.Nodes[triIdx[1]][0], y1 = optimized.Nodes[triIdx[1]][1];
            double x2 = optimized.Nodes[triIdx[2]][0], y2 = optimized.Nodes[triIdx[2]][1];

            double cx = (x0 + x1 + x2) / 3.0;
            double cy = (y0 + y1 + y2) / 3.0;
            double area = Math.Abs((x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)) / 2.0;

            string wkt = FormattableString.Invariant($"POLYGON (({x0} {y0}, {x1} {y1}, {x2} {y2}, {x0} {y0}))");

            Fiber fa = new Fiber(idx, $"{region.Tag}", wkt)
            {
               X = cx,
               Y = cy,
               Area = area,
               E = E,
               TypeFiber = FiberType.tri
            };
            fas.Add(fa);
            idx++;
         }

         return [.. fas];
      }

      /// <summary>
      /// Триангуляция методом продвижения фронта (SETKA-4N-2D).
      /// </summary>
      static Fiber[] TriangulationAdvancingFront(MaterialArea region, double maxTrgArea, double scale, double maxEdgeLen = 0, int smoothIter = 5)
      {
         double hullArea = WktHelper.PolygonArea(region.Hull!.X, region.Hull!.Y);
         double avgH = maxEdgeLen > 0
            ? maxEdgeLen
            : Math.Sqrt(hullArea * maxTrgArea * 4 / System.Math.Sqrt(3));
         double avgHScaled = System.Math.Max(avgH * scale, 1e-6);

         var hullPts = region.Hull;
         int nOuter = hullPts.Points.Count - 1;
         var nodes = new List<double[]>();
         var isBoundary = new List<bool>();
         var hValues = new List<double>();
         var outerIdxs = new List<int>();

         // Добавляем промежуточные узлы вдоль рёбер контура с шагом avgHScaled
         for (int j = 0; j < nOuter; j++)
         {
            double x0 = hullPts.X[j] * scale, y0 = hullPts.Y[j] * scale;
            double x1 = hullPts.X[(j + 1) % nOuter] * scale, y1 = hullPts.Y[(j + 1) % nOuter] * scale;
            double edgeLen = System.Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            int nSeg = System.Math.Max(1, (int)System.Math.Round(edgeLen / avgHScaled));
            for (int s = 0; s < nSeg; s++)
            {
               double t = (double)s / nSeg;
               outerIdxs.Add(nodes.Count);
               nodes.Add([x0 + t * (x1 - x0), y0 + t * (y1 - y0)]);
               isBoundary.Add(true);
               hValues.Add(avgHScaled);
            }
         }

         var holeIdxs = new List<List<int>>();
         if (region.Contours.Count > 1)
         {
            var h = region.Holes;
            for (int k = 0; k < h.Count; k++)
            {
               Contour hole = h[k];
               int nHole = hole.Points.Count - 1;
               var holeIdxList = new List<int>();
               for (int j = 0; j < nHole; j++)
               {
                  double x0 = hole.X[j] * scale, y0 = hole.Y[j] * scale;
                  double x1 = hole.X[(j + 1) % nHole] * scale, y1 = hole.Y[(j + 1) % nHole] * scale;
                  double edgeLen = System.Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                  int nSeg = System.Math.Max(1, (int)System.Math.Round(edgeLen / avgHScaled));
                  for (int s = 0; s < nSeg; s++)
                  {
                     double t = (double)s / nSeg;
                     holeIdxList.Add(nodes.Count);
                     nodes.Add([x0 + t * (x1 - x0), y0 + t * (y1 - y0)]);
                     isBoundary.Add(true);
                     hValues.Add(avgHScaled);
                  }
               }
               holeIdxs.Add(holeIdxList);
            }
         }

         var contour = new DiscretizedContour
         {
            Nodes = nodes.ToArray(),
            IsBoundary = isBoundary.ToArray(),
            HValues = hValues.ToArray(),
            OuterIndices = outerIdxs,
            HoleIndices = holeIdxs
         };

         var afResult = AdvancingFront.Triangulate(contour, 90.0);

         var optimized = Optimize.OptimizeTriangular(afResult, nIter: smoothIter, chi: 2.0);

         double E = region.Material == null ? 0 : region.Material.E;
         var fas = new List<Fiber>(optimized.Triangles.Length);
         int idx = 1;
         foreach (var triIdx in optimized.Triangles)
         {
            double x0 = optimized.Nodes[triIdx[0]][0] / scale, y0 = optimized.Nodes[triIdx[0]][1] / scale;
            double x1 = optimized.Nodes[triIdx[1]][0] / scale, y1 = optimized.Nodes[triIdx[1]][1] / scale;
            double x2 = optimized.Nodes[triIdx[2]][0] / scale, y2 = optimized.Nodes[triIdx[2]][1] / scale;

            double cx = (x0 + x1 + x2) / 3.0;
            double cy = (y0 + y1 + y2) / 3.0;
            double area = Math.Abs((x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)) / 2.0;

            string wkt = FormattableString.Invariant($"POLYGON (({x0} {y0}, {x1} {y1}, {x2} {y2}, {x0} {y0}))");

            Fiber fa = new Fiber(idx, $"{region.Tag}", wkt)
            {
               X = cx,
               Y = cy,
               Area = area,
               E = E,
               TypeFiber = FiberType.tri
            };
            fas.Add(fa);
            idx++;
         }

         return [.. fas];
      }
   }
}