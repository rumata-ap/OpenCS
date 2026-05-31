using System;
using System.Collections.Generic;
using System.Linq;

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
      /// Максимальная длина стороны. null — без ограничения.
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
      /// Разрешать разбиение ограниченных рёбер.
      /// </summary>
      public bool AllowBoundarySplit { get; set; } = true;
   }

   /// <summary>
   /// Результат триангуляции алгоритмом Рупперта.
   /// </summary>
   public sealed class RuppertResult
   {
      /// <summary>
      /// Координаты вершин: [(x, y), ...].
      /// </summary>
      public (double X, double Y)[] Vertices = [];

      /// <summary>
      /// Треугольники: [(i, j, k), ...] — CCW.
      /// </summary>
      public (int, int, int)[] Triangles = [];

      /// <summary>
      /// Статистика сетки: число треугольников, минимальный угол и т.д.
      /// </summary>
      public Dictionary<string, double> Stats = [];

      /// <summary>
      /// Число вставленных точек Штейнера.
      /// </summary>
      public int SteinerPoints;

      /// <summary>
      /// Ограниченные рёбра: {(min_i, max_i), ...}.
      /// </summary>
      public HashSet<(int, int)> ConstrainedEdges = [];
   }

   /// <summary>
   /// Основной класс триангуляции Рупперта. Строит CDT и улучшает качество.
   ///
   /// Поддерживаемые входные данные:
   /// - Внешний контур полигона (обязателен)
   /// - Отверстия
   /// - Ограниченные внутренние отрезки
   /// - Дополнительные внутренние точки
   /// </summary>
   public sealed class Triangulator
   {
      private Vec2[]? _outer;
      private readonly List<Vec2[]> _holes = [];
      private readonly List<(Vec2, Vec2)> _segments = [];
      private readonly List<Vec2> _points = [];

      /// <summary>
      /// Задаёт внешний контур полигона.
      /// </summary>
      public void SetOuterPolygon(Vec2[] vertices) => _outer = vertices;

      /// <summary>
      /// Добавляет отверстие.
      /// </summary>
      public void AddHole(Vec2[] vertices) => _holes.Add(vertices);

      /// <summary>
      /// Добавляет ребро-ограничение. Оно гарантированно войдёт в сетку.
      /// </summary>
      public void AddConstraintSegment(Vec2 p1, Vec2 p2) => _segments.Add((p1, p2));

      /// <summary>
      /// Добавляет вершину-ограничение. Гарантированно войдёт в сетку.
      /// </summary>
      public void AddConstraintPoint(Vec2 p) => _points.Add(p);

      /// <summary>
      /// Строит триангуляцию с заданными параметрами.
      /// </summary>
      public RuppertResult Triangulate(TriangulationParams? parms = null)
      {
         if (_outer == null)
            throw new InvalidOperationException("Внешний контур не задан. Вызовите SetOuterPolygon().");
         parms ??= new TriangulationParams();

         var cdt = new CDT();
         cdt.SetOuter(_outer);
         foreach (var h in _holes) cdt.AddHole(h);
         foreach (var (a, b) in _segments) cdt.AddSegment(a, b);
         foreach (var p in _points) cdt.AddPoint(p);
         cdt.Build();

         int steiner = 0;
         if (parms.DoRefine)
         {
            var qp = new QualityParams(
               parms.MinAngleDeg, parms.MaxArea, parms.MaxEdgeLen,
               parms.MaxSteinerPoints, 1e-14, parms.AllowBoundarySplit);
            steiner = Refine.Run(cdt, qp);
         }

         var (verts, tris) = cdt.Mesh.Export();
         var raw = cdt.Mesh.Stats();
         raw["steiner_points"] = steiner;

         var used = new HashSet<int>();
         foreach (int i in cdt.Mesh.Live())
            foreach (int v in cdt.Mesh.Tris[i]) used.Add(v);
         var sorted = used.OrderBy(v => v).ToList();
         var o2n = new Dictionary<int, int>();
         for (int n = 0; n < sorted.Count; n++) o2n[sorted[n]] = n;
         var conEdges = new HashSet<(int, int)>(
            cdt.Mesh.Constrained
               .Where(kv => o2n.ContainsKey(kv.Item1) && o2n.ContainsKey(kv.Item2))
               .Select(kv => (Math.Min(o2n[kv.Item1], o2n[kv.Item2]),
                              Math.Max(o2n[kv.Item1], o2n[kv.Item2]))));

         return new RuppertResult
         {
            Vertices = verts.Select(v => (v.X, v.Y)).ToArray(),
            Triangles = tris,
            Stats = raw,
            SteinerPoints = steiner,
            ConstrainedEdges = conEdges
         };
      }

      /// <summary>
      /// Быстрая триангуляция полигона одной строкой.
      /// </summary>
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