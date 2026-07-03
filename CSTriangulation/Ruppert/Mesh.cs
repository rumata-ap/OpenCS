using System;
using System.Collections.Generic;
using System.Linq;

namespace CSTriangulation.Ruppert
{
   /// <summary>
   /// Хранилище триангуляции на параллельных массивах.
   ///
   /// Треугольник i задаётся вершинами Tris[i] = [v0, v1, v2] (CCW).
   /// Смежность: Adj[i][k] — сосед по ребру k = (vk → v(k+1)%3), Nil если граница.
   ///            AdjS[i][k] — слот ребра у соседа.
   /// </summary>
   public sealed class Mesh
   {
      public const int Nil = -1;

      public List<Vec2> Verts { get; } = [];
      public List<int[]> Tris { get; } = [];
      public List<int[]> Adj { get; } = [];
      public List<int[]> AdjS { get; } = [];
      public List<bool> Alive { get; } = [];
      public HashSet<(int, int)> Constrained { get; } = [];

      /// <summary>
      /// Добавляет вершину и возвращает её индекс.
      /// </summary>
      public int AddVert(Vec2 p) { Verts.Add(p); return Verts.Count - 1; }

      /// <summary>
      /// Добавляет треугольник (v0,v1,v2) с необязательными соседями. Возвращает индекс.
      /// </summary>
      public int AddTri(int v0, int v1, int v2, int a0 = Nil, int a1 = Nil, int a2 = Nil,
         int s0 = Nil, int s1 = Nil, int s2 = Nil)
      {
         int i = Tris.Count;
         Tris.Add([v0, v1, v2]);
         Adj.Add([a0, a1, a2]);
         AdjS.Add([s0, s1, s2]);
         Alive.Add(true);
         return i;
      }

      /// <summary>
      /// Помечает треугольник как удалённый.
      /// </summary>
      public void Kill(int i) => Alive[i] = false;

      /// <summary>
      /// Список живых треугольников.
      /// </summary>
      public List<int> Live()
      {
         var r = new List<int>();
         for (int i = 0; i < Alive.Count; i++)
            if (Alive[i]) r.Add(i);
         return r;
      }

      /// <summary>
      /// Связывает ребро ki треугольника ti с ребром kj треугольника tj.
      /// </summary>
      public void Link(int ti, int ki, int tj, int kj)
      {
         Adj[ti][ki] = tj; AdjS[ti][ki] = kj;
         Adj[tj][kj] = ti; AdjS[tj][kj] = ki;
      }

      /// <summary>
      /// Помечает ребро (u,v) как ограниченное.
      /// </summary>
      public void MarkCon(int u, int v) => Constrained.Add((Math.Min(u, v), Math.Max(u, v)));

      /// <summary>
      /// Проверяет, является ли ребро (u,v) ограниченным.
      /// </summary>
      public bool IsCon(int u, int v) => Constrained.Contains((Math.Min(u, v), Math.Max(u, v)));

      /// <summary>
      /// Возвращает вершину напротив ребра k в треугольнике ti.
      /// </summary>
      public int Opp(int ti, int k) => Tris[ti][(k + 2) % 3];

      /// <summary>
      /// Находит треугольник, содержащий точку p (линейный поиск).
      /// </summary>
      public int Locate(Vec2 p)
      {
         foreach (int i in Live())
         {
            var t = Tris[i];
            var a = Verts[t[0]]; var b = Verts[t[1]]; var c = Verts[t[2]];
            double d0 = Geo.Orient2D(a, b, p);
            double d1 = Geo.Orient2D(b, c, p);
            double d2 = Geo.Orient2D(c, a, p);
            if (!((d0 < 0 || d1 < 0 || d2 < 0) && (d0 > 0 || d1 > 0 || d2 > 0)))
               return i;
         }
         return Nil;
      }

      /// <summary>
      /// Флип ребра ki в треугольнике ti. Возвращает true если флип выполнен.
      /// </summary>
      public bool Flip(int ti, int ki)
      {
         if (!Alive[ti]) return false;
         int j = Adj[ti][ki];
         if (j == Nil || !Alive[j]) return false;
         int kj = AdjS[ti][ki];

         var t = Tris[ti]; var n = Tris[j];
         int a = t[ki], b = t[(ki + 1) % 3], c = t[(ki + 2) % 3];
         if (!(n[kj] == b && n[(kj + 1) % 3] == a)) return false;
         int d = n[(kj + 2) % 3];
         if (c == d || c == a || c == b || d == a || d == b) return false;
         if (IsCon(a, b)) return false;

         int ki1 = (ki + 1) % 3, ki2 = (ki + 2) % 3;
         int kj1 = (kj + 1) % 3, kj2 = (kj + 2) % 3;
         int ni1 = Adj[ti][ki1], si1 = AdjS[ti][ki1];
         int ni2 = Adj[ti][ki2], si2 = AdjS[ti][ki2];
         int nj1 = Adj[j][kj1], sj1 = AdjS[j][kj1];
         int nj2 = Adj[j][kj2], sj2 = AdjS[j][kj2];

         Tris[ti] = [a, d, c];
         Tris[j] = [b, c, d];

         Adj[ti][1] = j; AdjS[ti][1] = 1;
         Adj[j][1] = ti; AdjS[j][1] = 1;

         Adj[ti][0] = nj1; AdjS[ti][0] = sj1;
         if (nj1 != Nil) { Adj[nj1][sj1] = ti; AdjS[nj1][sj1] = 0; }

         Adj[ti][2] = ni2; AdjS[ti][2] = si2;
         if (ni2 != Nil) { Adj[ni2][si2] = ti; AdjS[ni2][si2] = 2; }

         Adj[j][0] = ni1; AdjS[j][0] = si1;
         if (ni1 != Nil) { Adj[ni1][si1] = j; AdjS[ni1][si1] = 0; }

         Adj[j][2] = nj2; AdjS[j][2] = sj2;
         if (nj2 != Nil) { Adj[nj2][sj2] = j; AdjS[nj2][sj2] = 2; }

         return true;
      }

      /// <summary>
      /// Экспортирует живые треугольники: (вершины, треугольники) с переиндексацией.
      /// </summary>
      public (Vec2[] verts, (int, int, int)[] tris) Export()
      {
         var used = new HashSet<int>();
         foreach (int i in Live())
            foreach (int v in Tris[i]) used.Add(v);
         var sorted = used.OrderBy(v => v).ToList();
         var o2n = new Dictionary<int, int>();
         for (int n = 0; n < sorted.Count; n++) o2n[sorted[n]] = n;
         var vout = sorted.Select(v => Verts[v]).ToArray();
         var tout = Live().Select(i =>
            (o2n[Tris[i][0]], o2n[Tris[i][1]], o2n[Tris[i][2]])).ToArray();
         return (vout, tout);
      }

      /// <summary>
      /// Статистика сетки: число треугольников, вершин, минимальный угол и т.д.
      /// </summary>
      public Dictionary<string, double> Stats()
      {
         var live = Live();
         if (live.Count == 0) return [];
         var angs = new List<double>();
         var areas = new List<double>();
         foreach (int i in live)
         {
            var t = Tris[i];
            var a = Verts[t[0]]; var b = Verts[t[1]]; var c = Verts[t[2]];
            angs.Add(Geo.TriMinAngleDeg(a, b, c));
            areas.Add(Geo.TriArea(a, b, c));
         }
         int verts = live.SelectMany(i => Tris[i]).Distinct().Count();
         return new Dictionary<string, double>
         {
            ["triangles"] = live.Count,
            ["vertices"] = verts,
            ["min_angle_deg"] = angs.Min(),
            ["avg_min_angle_deg"] = angs.Average(),
            ["max_area"] = areas.Max(),
            ["min_area"] = areas.Min()
         };
      }
   }
}