using System;
using System.Collections.Generic;
using System.Linq;

namespace CSTriangulation.Ruppert
{
   /// <summary>
   /// Constrained Delaunay Triangulation (CDT).
   ///
   /// 1. Bowyer-Watson с Lawson-флипами.
   /// 2. Восстановление ограничений через флипы и вставку точек пересечения.
   /// 3. Удаление внешних треугольников.
   /// </summary>
   public sealed class CDT
   {
      public readonly Mesh Mesh = new();

      /// <summary>
      /// Внешний контур (CCW) — доступен для рефайнмента.
      /// </summary>
      public Vec2[] OuterVertices => _outer;

      /// <summary>
      /// Отверстия (CW) — доступны для рефайнмента.
      /// </summary>
      public List<Vec2[]> HoleVertices => _holes;

      private List<int> _super = [];
      private Vec2[] _outer = [];
      private List<Vec2[]> _holes = [];
      private List<(Vec2, Vec2)> _segs = [];
      private List<Vec2> _pts = [];

      /// <summary>
      /// Задаёт внешний контур (CCW).
      /// </summary>
      public void SetOuter(Vec2[] pts) => _outer = Geo.EnsureCCW(pts);

      /// <summary>
      /// Добавляет отверстие (CW).
      /// </summary>
      public void AddHole(Vec2[] pts) => _holes.Add(Geo.EnsureCW(pts));

      /// <summary>
      /// Добавляет ограниченный отрезок.
      /// </summary>
      public void AddSegment(Vec2 a, Vec2 b) => _segs.Add((a, b));

      /// <summary>
      /// Добавляет обязательную точку.
      /// </summary>
      public void AddPoint(Vec2 p) => _pts.Add(p);

      /// <summary>
      /// Строит CDT и возвращает сетку.
      /// </summary>
      public Mesh Build()
      {
         MakeSuper();
         var allPts = new List<Vec2>(_outer);
         foreach (var h in _holes) allPts.AddRange(h);
         foreach (var (a, b) in _segs) { allPts.Add(a); allPts.Add(b); }
         allPts.AddRange(_pts);
         foreach (var p in Dedup(allPts))
            Insert(p);

         var poly = _outer;
         for (int i = 0; i < poly.Length; i++)
            Enforce(poly[i], poly[(i + 1) % poly.Length]);
         foreach (var hole in _holes)
            for (int i = 0; i < hole.Length; i++)
               Enforce(hole[i], hole[(i + 1) % hole.Length]);
         foreach (var (a, b) in _segs)
            Enforce(a, b);

         RemoveOuter();
         return Mesh;
      }

      /// <summary>
      /// Находит индекс вершины, равной p. Возвращает Nil если не найдена.
      /// </summary>
      public int FindVert(Vec2 p)
      {
         for (int i = 0; i < Mesh.Verts.Count; i++)
         {
            if (Math.Abs(Mesh.Verts[i].X - p.X) < Vec2.Eps * 100 &&
                Math.Abs(Mesh.Verts[i].Y - p.Y) < Vec2.Eps * 100)
               return i;
         }
         return Mesh.Nil;
      }

      // ── суперструктура ──────────────────────────────────────────────
      private void MakeSuper()
      {
         var allXs = _outer.Concat(_holes.SelectMany(h => h))
            .Concat(_segs.SelectMany(s => new[] { s.Item1, s.Item2 }))
            .Concat(_pts).ToList();
         if (allXs.Count == 0) allXs.Add(new Vec2(0, 0));
         double minX = allXs.Min(v => v.X), maxX = allXs.Max(v => v.X);
         double minY = allXs.Min(v => v.Y), maxY = allXs.Max(v => v.Y);
         double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
         double r = Math.Max(maxX - minX, maxY - minY) * 20 + 1000;
         int s0 = Mesh.AddVert(new Vec2(cx, cy + 3 * r));
         int s1 = Mesh.AddVert(new Vec2(cx - 3 * r, cy - 3 * r));
         int s2 = Mesh.AddVert(new Vec2(cx + 3 * r, cy - 3 * r));
         _super = [s0, s1, s2];
         Mesh.AddTri(s0, s1, s2);
      }

      // ── вспомогательные ─────────────────────────────────────────────
      private static List<Vec2> Dedup(List<Vec2> pts)
      {
         var seen = new HashSet<(double, double)>();
         var r = new List<Vec2>();
         foreach (var p in pts)
         {
            var k = (Math.Round(p.X, 8), Math.Round(p.Y, 8));
            if (seen.Add(k)) r.Add(p);
         }
         return r;
      }

      // ── Bowyer-Watson вставка ────────────────────────────────────────
      /// <summary>
      /// Вставляет точку p в триангуляцию. Возвращает индекс вершины.
      /// </summary>
      public int Insert(Vec2 p)
      {
         int vi = FindVert(p);
         if (vi != Mesh.Nil) return vi;
         int ti = Mesh.Locate(p);
         if (ti == Mesh.Nil) return Mesh.Nil;
         for (int k = 0; k < 3; k++)
         {
            int vk = Mesh.Tris[ti][k];
            if (Mesh.Verts[vk].Dist2(p) < 1e-14) return vk;
         }
         int ek = OnEdge(p, ti);
         if (ek != Mesh.Nil) return SplitEdge(p, ti, ek);
         return BowyerWatson(p, ti);
      }

      private int OnEdge(Vec2 p, int ti)
      {
         var t = Mesh.Tris[ti];
         for (int k = 0; k < 3; k++)
         {
            var a = Mesh.Verts[t[k]]; var b = Mesh.Verts[t[(k + 1) % 3]];
            if (Geo.OnSegStrict(p, a, b)) return k;
         }
         return Mesh.Nil;
      }

      private int BowyerWatson(Vec2 p, int start)
      {
         var mesh = Mesh;
         var cav = new HashSet<int>();
         var stk = new Stack<int>();
         stk.Push(start);
         while (stk.Count > 0)
         {
            int ti = stk.Pop();
            if (ti == Mesh.Nil || !mesh.Alive[ti] || cav.Contains(ti)) continue;
            var t = mesh.Tris[ti];
            var a = mesh.Verts[t[0]]; var b = mesh.Verts[t[1]]; var c = mesh.Verts[t[2]];
            if (Geo.Orient2D(a, b, c) < 0) { (a, c) = (c, a); }
            if (Geo.InCircle(a, b, c, p) > -Vec2.Eps)
            {
               cav.Add(ti);
               for (int k = 0; k < 3; k++)
               {
                  int nb = mesh.Adj[ti][k];
                  if (nb != Mesh.Nil && !cav.Contains(nb)) stk.Push(nb);
               }
            }
         }
         if (cav.Count == 0) return Mesh.Nil;

         var bnd = new List<(int va, int vb, int extNb, int extS)>();
         foreach (int ti in cav)
         {
            var t = mesh.Tris[ti];
            for (int k = 0; k < 3; k++)
            {
               int nb = mesh.Adj[ti][k];
               if (nb == Mesh.Nil || !cav.Contains(nb))
                  bnd.Add((t[k], t[(k + 1) % 3], nb, mesh.AdjS[ti][k]));
            }
         }
         if (bnd.Count == 0) return Mesh.Nil;

         foreach (int ti in cav) mesh.Kill(ti);
         int vp = mesh.AddVert(p);

         var ntmap = new Dictionary<(int, int), int>();
         foreach (var (va, vb, _, _) in bnd)
            ntmap[(va, vb)] = mesh.AddTri(vp, va, vb);

         for (int bi = 0; bi < bnd.Count; bi++)
         {
            var (va, vb, extNb, extS) = bnd[bi];
            int nti = ntmap[(va, vb)];
            if (extNb != Mesh.Nil)
            {
               mesh.Adj[nti][1] = extNb; mesh.AdjS[nti][1] = extS;
               mesh.Adj[extNb][extS] = nti; mesh.AdjS[extNb][extS] = 1;
            }
            for (int bj = 0; bj < bnd.Count; bj++)
            {
               if (bnd[bj].va == vb)
               {
                  int nbTi = ntmap[(bnd[bj].va, bnd[bj].vb)];
                  mesh.Adj[nti][2] = nbTi; mesh.AdjS[nti][2] = 0;
                  mesh.Adj[nbTi][0] = nti; mesh.AdjS[nbTi][0] = 2;
                  break;
               }
            }
         }

         Legalize(vp, ntmap.Values.ToList());
         return vp;
      }

      // ── Lawson легализация ───────────────────────────────────────────
      private void Legalize(int vi, List<int> newTris)
      {
         var mesh = Mesh;
         var stk = new Stack<int>(newTris);
         var ins = new HashSet<int>(newTris);
         int lim = Math.Max(mesh.Tris.Count * 4, 500);
         int itr = 0;
         while (stk.Count > 0 && itr < lim)
         {
            itr++;
            int ti = stk.Pop(); ins.Remove(ti);
            if (!mesh.Alive[ti]) continue;
            var t = mesh.Tris[ti];
            int kOp = Mesh.Nil;
            for (int k = 0; k < 3; k++)
               if (t[k] != vi && t[(k + 1) % 3] != vi) { kOp = k; break; }
            if (kOp == Mesh.Nil) continue;
            int j = mesh.Adj[ti][kOp];
            if (j == Mesh.Nil || !mesh.Alive[j]) continue;
            int va = t[kOp], vb = t[(kOp + 1) % 3];
            if (mesh.IsCon(va, vb)) continue;
            int kj = mesh.AdjS[ti][kOp];
            int vcI = mesh.Opp(ti, kOp), vdJ = mesh.Opp(j, kj);
            var pa = mesh.Verts[va]; var pb = mesh.Verts[vb];
            var pc = mesh.Verts[vcI]; var pd = mesh.Verts[vdJ];
            double oa = Geo.Orient2D(pa, pb, pc);
            if (Math.Abs(oa) < Vec2.Eps) continue;
            if (oa < 0) { (pa, pb) = (pb, pa); }
            if (Geo.InCircle(pa, pb, pc, pd) > Vec2.Eps)
            {
               if (mesh.Flip(ti, kOp))
               {
                  foreach (int c in new[] { ti, j })
                     if (mesh.Alive[c] && !ins.Contains(c))
                     { ins.Add(c); stk.Push(c); }
               }
            }
         }
      }

      // ── разбиение ребра ─────────────────────────────────────────────
      private int SplitEdge(Vec2 p, int ti, int k)
      {
         var mesh = Mesh;
         var t = mesh.Tris[ti];
         int va = t[k], vb = t[(k + 1) % 3], vc = t[(k + 2) % 3];
         bool wasCon = mesh.IsCon(va, vb);
         int j = mesh.Adj[ti][k]; int kj = mesh.AdjS[ti][k];
         int vd = Mesh.Nil;
         if (j != Mesh.Nil && mesh.Alive[j]) vd = mesh.Opp(j, kj);

         int k1 = (k + 1) % 3, k2 = (k + 2) % 3;
         int nb1 = mesh.Adj[ti][k1], s1 = mesh.AdjS[ti][k1];
         int nb2 = mesh.Adj[ti][k2], s2 = mesh.AdjS[ti][k2];

         int nbj1 = Mesh.Nil, nbj2 = Mesh.Nil, sj1 = Mesh.Nil, sj2 = Mesh.Nil;
         if (j != Mesh.Nil && mesh.Alive[j])
         {
            int kj1 = (kj + 1) % 3, kj2 = (kj + 2) % 3;
            nbj1 = mesh.Adj[j][kj1]; sj1 = mesh.AdjS[j][kj1];
            nbj2 = mesh.Adj[j][kj2]; sj2 = mesh.AdjS[j][kj2];
         }

         mesh.Kill(ti);
         if (j != Mesh.Nil && mesh.Alive[j]) mesh.Kill(j);
         if (wasCon) mesh.Constrained.Remove((Math.Min(va, vb), Math.Max(va, vb)));

         int vp = mesh.AddVert(p);
         int ta = mesh.AddTri(va, vp, vc);
         int tb = mesh.AddTri(vp, vb, vc);
         mesh.Link(ta, 1, tb, 2);
         if (nb1 != Mesh.Nil) mesh.Link(tb, 1, nb1, s1);
         if (nb2 != Mesh.Nil) mesh.Link(ta, 2, nb2, s2);

         var newTris = new List<int> { ta, tb };
         if (j != Mesh.Nil && vd != Mesh.Nil)
         {
            int tc = mesh.AddTri(vb, vp, vd);
            int td = mesh.AddTri(vp, va, vd);
            mesh.Link(tc, 1, td, 2);
            mesh.Link(tb, 0, tc, 0);
            mesh.Link(ta, 0, td, 0);
            if (nbj1 != Mesh.Nil) mesh.Link(td, 1, nbj1, sj1);
            if (nbj2 != Mesh.Nil) mesh.Link(tc, 2, nbj2, sj2);
            newTris.Add(tc); newTris.Add(td);
         }

         if (wasCon) { mesh.MarkCon(va, vp); mesh.MarkCon(vp, vb); }
         Legalize(vp, newTris);
         return vp;
      }

      // ── enforce edge ────────────────────────────────────────────────
      private void Enforce(Vec2 pa, Vec2 pb)
      {
         int vaI = FindVert(pa);
         if (vaI == Mesh.Nil) vaI = Insert(pa);
         int vbI = FindVert(pb);
         if (vbI == Mesh.Nil) vbI = Insert(pb);
         if (vaI == Mesh.Nil || vbI == Mesh.Nil || vaI == vbI) return;
         EnforceVV(vaI, vbI);
      }

      private void EnforceVV(int vaI, int vbI)
      {
         var mesh = Mesh;
         if (HasEdge(vaI, vbI)) { mesh.MarkCon(vaI, vbI); return; }
         int mid = MidVert(vaI, vbI);
         if (mid != Mesh.Nil)
         {
            EnforceVV(vaI, mid);
            EnforceVV(mid, vbI);
            return;
         }
         Recover(vaI, vbI);
         if (HasEdge(vaI, vbI)) mesh.MarkCon(vaI, vbI);
      }

      private bool HasEdge(int u, int v)
      {
         foreach (int ti in Mesh.Live())
         {
            var t = Mesh.Tris[ti];
            for (int k = 0; k < 3; k++)
               if ((t[k] == u && t[(k + 1) % 3] == v) || (t[k] == v && t[(k + 1) % 3] == u))
                  return true;
         }
         return false;
      }

      private int MidVert(int vaI, int vbI)
      {
         var pa = Mesh.Verts[vaI]; var pb = Mesh.Verts[vbI];
         for (int i = 0; i < Mesh.Verts.Count; i++)
         {
            if (i == vaI || i == vbI || _super.Contains(i)) continue;
            if (Geo.OnSegStrict(Mesh.Verts[i], pa, pb)) return i;
         }
         return Mesh.Nil;
      }

      private void Recover(int vaI, int vbI)
      {
         var mesh = Mesh;
         var pa = mesh.Verts[vaI]; var pb = mesh.Verts[vbI];
         for (int iter = 0; iter < mesh.Tris.Count * 4 + 100; iter++)
         {
            if (HasEdge(vaI, vbI)) return;
            var crossing = Crossing(vaI, vbI);
            if (crossing.Count == 0) return;
            bool done = false;
            foreach (var (ti, k) in crossing)
            {
               if (!mesh.Alive[ti]) continue;
               var t = mesh.Tris[ti];
               int u = t[k], v = t[(k + 1) % 3];
               if (mesh.IsCon(u, v))
               {
                  var ip = Geo.SegIntersect(pa, pb, mesh.Verts[u], mesh.Verts[v]);
                  if (ip != null)
                  {
                     int nv = Insert(ip);
                     if (nv != Mesh.Nil && nv != vaI && nv != vbI)
                     { EnforceVV(vaI, nv); EnforceVV(nv, vbI); return; }
                  }
                  continue;
               }
               int j = mesh.Adj[ti][k]; int kj = mesh.AdjS[ti][k];
               if (j == Mesh.Nil || !mesh.Alive[j]) continue;
               int vc = mesh.Opp(ti, k); int vd = mesh.Opp(j, kj);
               if (Geo.SegCross(mesh.Verts[u], mesh.Verts[v], mesh.Verts[vc], mesh.Verts[vd]))
               {
                  if (mesh.Flip(ti, k)) { done = true; break; }
               }
            }
            if (!done)
            {
               foreach (var (ti, k) in crossing)
               {
                  if (!mesh.Alive[ti]) continue;
                  var t = mesh.Tris[ti];
                  int u = t[k], v = t[(k + 1) % 3];
                  if (mesh.IsCon(u, v)) continue;
                  var ip = Geo.SegIntersect(pa, pb, mesh.Verts[u], mesh.Verts[v]);
                  if (ip != null)
                  {
                     int nv = Insert(ip);
                     if (nv != Mesh.Nil && nv != vaI && nv != vbI)
                     { EnforceVV(vaI, nv); EnforceVV(nv, vbI); return; }
                  }
               }
               return;
            }
         }
      }

      private List<(int, int)> Crossing(int vaI, int vbI)
      {
         var mesh = Mesh;
         var pa = mesh.Verts[vaI]; var pb = mesh.Verts[vbI];
         var r = new List<(int, int)>();
         var seen = new HashSet<(int, int)>();
         foreach (int ti in mesh.Live())
         {
            var t = mesh.Tris[ti];
            for (int k = 0; k < 3; k++)
            {
               int u = t[k], v = t[(k + 1) % 3];
               var key = (Math.Min(u, v), Math.Max(u, v));
               if (seen.Contains(key)) continue;
               seen.Add(key);
               if (u == vaI || v == vaI || u == vbI || v == vbI) continue;
               if (Geo.SegCross(pa, pb, mesh.Verts[u], mesh.Verts[v]))
                  r.Add((ti, k));
            }
         }
         return r;
      }

      // ── удаление внешних ─────────────────────────────────────────────
      private void RemoveOuter()
      {
         var mesh = Mesh;
         var sup = new HashSet<int>(_super);
         foreach (int ti in mesh.Live())
         {
            var t = mesh.Tris[ti];
            if (t.Any(v => sup.Contains(v))) { mesh.Kill(ti); continue; }
            double cx = t.Sum(v => mesh.Verts[v].X) / 3;
            double cy = t.Sum(v => mesh.Verts[v].Y) / 3;
            var c = new Vec2(cx, cy);
            if (_outer.Length > 0 && !Geo.PointInPoly(c, _outer)) { mesh.Kill(ti); continue; }
            if (_holes.Any(h => Geo.PointInPoly(c, h))) { mesh.Kill(ti); }
         }
      }
   }
}