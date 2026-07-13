using System;
using System.Collections.Generic;
using System.Linq;
using TriangleNet.Geometry;
using TriangleMesh = TriangleNet.Meshing;

namespace CSTriangulation.Ruppert
{
   /// <summary>
   /// Параметры триангуляции для алгоритма Рупперта.
   /// </summary>
   public sealed class TriangulationParams
   {
      /// <summary>
      /// Минимально допустимый угол треугольника (градусы).
      /// Гарантированная сходимость при ≤ 20.7°. На практике работает до ~28°.
      /// </summary>
      public double MinAngleDeg { get; set; } = 20.0;

      /// <summary>
      /// Максимальная площадь треугольника. null — без ограничения.
      /// </summary>
      public double? MaxArea { get; set; }

      /// <summary>
      /// Максимальная длина стороны. Временная Triangle.NET-реализация её не
      /// поддерживает (на момент подмены никем не используется) — игнорируется.
      /// </summary>
      public double? MaxEdgeLen { get; set; }

      /// <summary>
      /// Максимальное число точек Штейнера. По умолчанию: 100 000.
      /// </summary>
      public int MaxSteinerPoints { get; set; } = 100_000;

      /// <summary>
      /// False — только CDT, без рефайнмента. True — CDT + Рупперт.
      /// </summary>
      public bool DoRefine { get; set; } = true;

      /// <summary>
      /// Разрешать разбиение ограниченных рёбер. ИГНОРИРУЕТСЯ временной
      /// Triangle.NET-реализацией: эмпирически проверено, что
      /// ConstraintOptions.SegmentSplitting != 0 в этой сборке полностью
      /// отключает качественный рефайнмент (получается голый CDT без единой
      /// точки Штейнера, независимо от MaximumArea/MinimumAngle). Защита от
      /// взрыва точек Штейнера на вогнутых контурах обеспечивается через
      /// MaxSteinerPoints (проверено: cap=20 → 28 вершин вместо 7858 без
      /// ограничения на том же контуре).
      /// </summary>
      public bool AllowBoundarySplit { get; set; } = true;
   }

   /// <summary>
   /// Результат триангуляции алгоритмом Рупперта.
   /// </summary>
   public sealed class RuppertResult
   {
      /// <summary>Координаты вершин: [(x, y), ...].</summary>
      public (double X, double Y)[] Vertices = [];

      /// <summary>Треугольники: [(i, j, k), ...] — CCW.</summary>
      public (int, int, int)[] Triangles = [];

      /// <summary>Статистика сетки: число треугольников, вершин и т.д.</summary>
      public Dictionary<string, double> Stats = [];

      /// <summary>Число вставленных точек Штейнера (оценка: итоговые вершины минус входные).</summary>
      public int SteinerPoints;

      /// <summary>Ограниченные рёбра: {(min_i, max_i), ...}.</summary>
      public HashSet<(int, int)> ConstrainedEdges = [];
   }

   /// <summary>
   /// Основной класс триангуляции Рупперта.
   ///
   /// ВРЕМЕННО (см. docs/superpowers/specs/2026-07-11-ruppert-trianglenet-design.md):
   /// внутри используется библиотека Triangle.NET вместо собственных CDT.cs/Refine.cs —
   /// собственная реализация не реагировала на параметр maxAngl (всегда), и на
   /// maxTrgArea при наличии отверстий в области. Публичный API класса не меняется,
   /// чтобы Geo.cs/FireMeshBuilder.cs/MeshBuilder.cs не трогать.
   /// </summary>
   public sealed class Triangulator
   {
      private Vec2[]? _outer;
      private readonly List<Vec2[]> _holes = [];
      private readonly List<(Vec2, Vec2)> _segments = [];
      private readonly List<Vec2> _points = [];

      /// <summary>Задаёт внешний контур полигона.</summary>
      public void SetOuterPolygon(Vec2[] vertices) => _outer = vertices;

      /// <summary>Добавляет отверстие.</summary>
      public void AddHole(Vec2[] vertices) => _holes.Add(vertices);

      /// <summary>Добавляет ребро-ограничение. Оно гарантированно войдёт в сетку.</summary>
      public void AddConstraintSegment(Vec2 p1, Vec2 p2) => _segments.Add((p1, p2));

      /// <summary>Добавляет вершину-ограничение. Гарантированно войдёт в сетку.</summary>
      public void AddConstraintPoint(Vec2 p) => _points.Add(p);

      /// <summary>Строит триангуляцию с заданными параметрами.</summary>
      public RuppertResult Triangulate(TriangulationParams? parms = null)
      {
         if (_outer == null)
            throw new InvalidOperationException("Внешний контур не задан. Вызовите SetOuterPolygon().");
         parms ??= new TriangulationParams();

         var polygon = new Polygon();
         var outerVerts = _outer.Select(v => new Vertex(v.X, v.Y)).ToList();
         polygon.Add(new Contour(outerVerts), hole: false);

         int inputVertexCount = outerVerts.Count;
         foreach (var hole in _holes)
         {
            var holeVerts = hole.Select(v => new Vertex(v.X, v.Y)).ToList();
            polygon.Add(new Contour(holeVerts), hole: true);
            inputVertexCount += holeVerts.Count;
         }

         foreach (var p in _points)
         {
            polygon.Points.Add(new Vertex(p.X, p.Y));
            inputVertexCount++;
         }

         foreach (var (a, b) in _segments)
            polygon.Add(new Segment(new Vertex(a.X, a.Y), new Vertex(b.X, b.Y)), insert: true);

         // SegmentSplitting намеренно не задаётся (остаётся 0 по умолчанию):
         // эмпирически проверено, что любое ненулевое значение полностью
         // отключает качественный рефайнмент в этой сборке Triangle.NET.
         // AllowBoundarySplit из parms игнорируется по этой причине.
         var constraintOptions = new TriangleMesh.ConstraintOptions
         {
            ConformingDelaunay = true
         };

         var mesher = new TriangleMesh.GenericMesher();
         TriangleMesh.IMesh mesh;

         if (parms.DoRefine)
         {
            var quality = new TriangleMesh.QualityOptions
            {
               MinimumAngle = parms.MinAngleDeg,
               SteinerPoints = parms.MaxSteinerPoints
            };
            if (parms.MaxArea.HasValue)
               quality.MaximumArea = parms.MaxArea.Value;

            mesh = mesher.Triangulate(polygon, constraintOptions, quality);
            mesh.Refine(quality, true);
         }
         else
         {
            mesh = mesher.Triangulate(polygon, constraintOptions);
         }

         mesh.Renumber();

         var vertices = new (double X, double Y)[mesh.Vertices.Count];
         foreach (var v in mesh.Vertices)
            vertices[v.ID] = (v.X, v.Y);

         var triangles = mesh.Triangles
            .Select(t => (t.GetVertex(0).ID, t.GetVertex(1).ID, t.GetVertex(2).ID))
            .ToArray();

         var constrainedEdges = new HashSet<(int, int)>(
            mesh.Segments.Select(s => (Math.Min(s.P0, s.P1), Math.Max(s.P0, s.P1))));

         int steiner = Math.Max(0, vertices.Length - inputVertexCount);

         return new RuppertResult
         {
            Vertices = vertices,
            Triangles = triangles,
            Stats = new Dictionary<string, double>
            {
               ["triangles"] = triangles.Length,
               ["vertices"] = vertices.Length
            },
            SteinerPoints = steiner,
            ConstrainedEdges = constrainedEdges
         };
      }

      /// <summary>Быстрая триангуляция полигона одной строкой.</summary>
      public static RuppertResult FromPolygon(Vec2[] outer, Vec2[][]? holes = null,
         TriangulationParams? parms = null)
      {
         var t = new Triangulator();
         t.SetOuterPolygon(outer);
         if (holes != null)
            foreach (var h in holes) t.AddHole(h);
         return t.Triangulate(parms);
      }
   }
}
