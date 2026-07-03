using System;
using System.Collections.Generic;
using System.Linq;

namespace CSTriangulation.Ruppert
{
   /// <summary>
   /// Параметры качества триангуляции для алгоритма Руппера.
   /// </summary>
   public sealed class QualityParams
   {
      /// <summary>
      /// Минимальный угол треугольника (радианы).
      /// Гарантированная сходимость при ≤ 20.7°.
      /// </summary>
      public double MinAngleRad { get; }

      /// <summary>
      /// Максимальная площадь треугольника. null — без ограничения.
      /// </summary>
      public double? MaxArea { get; }

      /// <summary>
      /// Максимальная длина ребра. null — без ограничения.
      /// </summary>
      public double? MaxEdgeLen { get; }

      /// <summary>
      /// Лимит точек Штейнера (предохранитель от зависания).
      /// </summary>
      public int MaxSteiner { get; }

      /// <summary>
      /// Минимальная площадь — ниже рефайнмент не производится.
      /// </summary>
      public double MinTriArea { get; }

      /// <summary>
      /// Разрешать разбиение ограниченных рёбер.
      /// </summary>
      public bool AllowBoundarySplit { get; }

      public QualityParams(double minAngleDeg = 20.0, double? maxArea = null,
         double? maxEdgeLen = null, int maxSteiner = 100_000,
         double minTriArea = 1e-14, bool allowBoundarySplit = true)
      {
         MinAngleRad = minAngleDeg * Math.PI / 180.0;
         MaxArea = maxArea;
         MaxEdgeLen = maxEdgeLen;
         MaxSteiner = maxSteiner;
         MinTriArea = minTriArea;
         AllowBoundarySplit = allowBoundarySplit;
      }

      /// <summary>
      /// Является ли треугольник «плохим» по заданным критериям?
      /// </summary>
      public bool IsBad(Vec2 a, Vec2 b, Vec2 c)
      {
         double area = Geo.TriArea(a, b, c);
         if (area < MinTriArea) return false;
         if (Geo.TriMinAngleDeg(a, b, c) < MinAngleRad * 180.0 / Math.PI - 1e-6) return true;
         if (MaxArea.HasValue && area > MaxArea.Value + Vec2.Eps) return true;
         if (MaxEdgeLen.HasValue)
         {
            double maxEdge = Math.Max(Math.Max(a.Dist(b), b.Dist(c)), c.Dist(a));
            if (maxEdge > MaxEdgeLen.Value + Vec2.Eps) return true;
         }
         return false;
      }

      /// <summary>
      /// Оценка приоритета для приоритетной очереди (больше = хуже).
      /// </summary>
      public double Score(Vec2 a, Vec2 b, Vec2 c)
      {
         double s0 = a.Dist(b), s1 = b.Dist(c), s2 = c.Dist(a);
         double[] s = [s0, s1, s2];
         Array.Sort(s);
         if (s[0] < Vec2.Eps) return 0.0;
         double cosA = (s[1] * s[1] + s[2] * s[2] - s[0] * s[0]) / (2 * s[1] * s[2]);
         double sinA = Math.Sqrt(Math.Max(0.0, 1 - cosA * cosA));
         if (sinA < Vec2.Eps) return 1e9;
         return s[2] / (2 * sinA * s[0]);
      }
   }

   /// <summary>
   /// Алгоритм Рупперта: CDT + рефайнмент для улучшения качества сетки.
   /// </summary>
   public static class Refine
   {
      /// <summary>
      /// Запускает рефайнмент. Возвращает число вставленных точек Штейнера.
      /// </summary>
      public static int Run(CDT cdt, QualityParams parms)
      {
         var mesh = cdt.Mesh;
         var outer = cdt.OuterVertices;
         var holes = cdt.HoleVertices;
         int steiner = 0;

         if (parms.MaxEdgeLen.HasValue)
            steiner += SplitLongConstrained(cdt, parms);

         var heap = new PriorityQueue<int, double>();

         void Enq(int ti)
         {
            if (!mesh.Alive[ti]) return;
            var t = mesh.Tris[ti];
            var a = mesh.Verts[t[0]]; var b = mesh.Verts[t[1]]; var c = mesh.Verts[t[2]];
            if (parms.IsBad(a, b, c))
               heap.Enqueue(ti, -parms.Score(a, b, c));
         }

         foreach (int ti in mesh.Live()) Enq(ti);

         while (heap.Count > 0 && steiner < parms.MaxSteiner)
         {
            int ti = heap.Dequeue();
            if (!mesh.Alive[ti]) continue;
            var t = mesh.Tris[ti];
            var a = mesh.Verts[t[0]]; var b = mesh.Verts[t[1]]; var c = mesh.Verts[t[2]];
            if (!parms.IsBad(a, b, c)) continue;

            var cc = Geo.Circumcenter(a, b, c);
            if (cc == null) continue;

            if (outer.Length > 0 && !InDomain(cc, outer, holes)) continue;

            var enc = FindEncroached(cc, mesh);
            if (enc != null)
            {
               if (parms.AllowBoundarySplit)
               {
                  int u = enc.Value.Item1, v = enc.Value.Item2;
                  var mid = mesh.Verts[u].Mid(mesh.Verts[v]);
                  if (cdt.FindVert(mid) == Mesh.Nil)
                  {
                     var newTris = DoInsert(cdt, mid);
                     if (newTris.Count > 0)
                     {
                        steiner++;
                        foreach (int nt in newTris) Enq(nt);
                     }
                  }
               }
               continue;
            }

            if (cdt.FindVert(cc) == Mesh.Nil && InDomain(cc, outer, holes))
            {
               var newTris = DoInsert(cdt, cc);
               if (newTris.Count > 0)
               {
                  steiner++;
                  foreach (int nt in newTris) Enq(nt);
               }
            }
         }

         return steiner;
      }

      /// <summary>
      /// Проверяет принадлежность точки к области триангуляции.
      /// Учитывает точки на границе.
      /// </summary>
      private static bool InDomain(Vec2 p, Vec2[] outer, List<Vec2[]> holes)
      {
         if (outer.Length == 0) return true;
         if (Geo.PointInPoly(p, outer)) { /* внутри */ }
         else
         {
            bool onBoundary = false;
            int n = outer.Length;
            for (int i = 0; i < n; i++)
            {
               if (Geo.OnSegStrict(p, outer[i], outer[(i + 1) % n])) { onBoundary = true; break; }
               if (outer[i].Dist2(p) < (Vec2.Eps * 1000) * (Vec2.Eps * 1000)) { onBoundary = true; break; }
            }
            if (!onBoundary) return false;
         }
         foreach (var hole in holes)
         {
            if (Geo.PointInPoly(p, hole))
            {
               bool onHoleBnd = false;
               int m = hole.Length;
               for (int i = 0; i < m; i++)
               {
                  if (Geo.OnSegStrict(p, hole[i], hole[(i + 1) % m])) { onHoleBnd = true; break; }
                  if (hole[i].Dist2(p) < (Vec2.Eps * 1000) * (Vec2.Eps * 1000)) { onHoleBnd = true; break; }
               }
               if (!onHoleBnd) return false;
            }
         }
         return true;
      }

      /// <summary>
      /// Ищет ограниченное ребро, чья диаметральная окружность содержит p.
      /// </summary>
      private static (int, int)? FindEncroached(Vec2 p, Mesh mesh)
      {
         (int, int)? best = null;
         double bestExcess = Vec2.Eps;
         foreach (var (u, v) in mesh.Constrained)
         {
            if (u >= mesh.Verts.Count || v >= mesh.Verts.Count) continue;
            var pu = mesh.Verts[u]; var pv = mesh.Verts[v];
            var center = pu.Mid(pv);
            double r2 = pu.Dist2(center);
            double excess = r2 - p.Dist2(center);
            if (excess > bestExcess) { bestExcess = excess; best = (u, v); }
         }
         return best;
      }

      /// <summary>
      /// Итерационно разбивает ограниченные рёбра длиннее maxEdgeLen.
      /// </summary>
      private static int SplitLongConstrained(CDT cdt, QualityParams parms)
      {
         var mesh = cdt.Mesh;
         double lim = parms.MaxEdgeLen ?? double.MaxValue;
         int count = 0;
         bool changed = true;
         while (changed)
         {
            changed = false;
            foreach (var (u, v) in mesh.Constrained.ToList())
            {
               if (u >= mesh.Verts.Count || v >= mesh.Verts.Count) continue;
               if (!mesh.IsCon(u, v)) continue;
               if (mesh.Verts[u].Dist(mesh.Verts[v]) > lim + Vec2.Eps)
               {
                  var mid = mesh.Verts[u].Mid(mesh.Verts[v]);
                  if (cdt.FindVert(mid) == Mesh.Nil)
                  {
                     cdt.Insert(mid);
                     count++;
                     changed = true;
                     break;
                  }
               }
            }
         }
         return count;
      }

      /// <summary>
      /// Вставляет точку и возвращает список новых треугольников.
      /// </summary>
      private static List<int> DoInsert(CDT cdt, Vec2 p)
      {
         var mesh = cdt.Mesh;
         int oldCount = mesh.Tris.Count;
         int vi = cdt.Insert(p);
         if (vi == Mesh.Nil) return [];
         var result = new List<int>();
         for (int ti = oldCount; ti < mesh.Tris.Count; ti++)
            if (mesh.Alive[ti]) result.Add(ti);
         return result;
      }
   }
}