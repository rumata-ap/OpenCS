using System;
using System.Collections.Generic;
using System.Linq;

namespace CSTriangulation
{
   internal sealed record RawTriangulation(List<double[]> Nodes, List<ContourNodeKind> Kinds, List<(int, int, int)> Triangles);

   /// <summary>Основной итеративный цикл метода продвижения фронта (§3.2-3.3).</summary>
   internal sealed class FrontTriangulator
   {
      const double KLenMultiplier = 5.0;

      readonly List<double[]> _nodes;
      readonly List<ContourNodeKind> _kinds;
      readonly List<double> _h;
      readonly List<List<int>> _holeFronts;
      readonly List<List<int>> _originalHoles;
      readonly List<int> _constrainedPoints;
      readonly List<(int A, int B)> _constrainedSegments;
      readonly List<(int, int, int)> _triangles = new();
      readonly HashSet<int> _excluded = new();
      readonly Stack<List<int>> _fronts = new();
      readonly double _alphaThresholdRad;
      int _iterBudget;

      internal static HashSet<int>? DebugWatchQ;
      internal static Action<int, List<int>, List<double[]>, List<(int, int, int)>>? DebugSnapshot;

      public FrontTriangulator(DiscretizedContour c, double alphaThresholdDeg)
      {
         _nodes = new List<double[]>(c.Nodes);
         _kinds = new List<ContourNodeKind>(c.NodeKinds);
         _h = new List<double>(c.HValues);
         _holeFronts = c.HoleIndices.Select(h => new List<int>(h)).ToList();
         _originalHoles = c.HoleIndices.Select(h => new List<int>(h)).ToList();
         _constrainedPoints = new List<int>(c.ConstrainedPointIndices);
         _constrainedSegments = new List<(int, int)>(c.ConstrainedSegments);
         _alphaThresholdRad = alphaThresholdDeg * Math.PI / 180.0;

         _fronts.Push(new List<int>(c.OuterIndices));

         int k0 = c.OuterIndices.Count + c.HoleIndices.Sum(h => h.Count);
         _iterBudget = Math.Max(500, k0 * k0 * 4);
      }

      public RawTriangulation Run()
      {
         while (_fronts.Count > 0)
            ProcessFront(_fronts.Pop());

         HoleRemoval.RemoveTrianglesInHoles(_nodes, _triangles, _originalHoles);

         if (Environment.GetEnvironmentVariable("TRI_DEBUG") == "1")
         {
            Console.Error.WriteLine($"RUN COMPLETE. ALL NODES ({_nodes.Count}):");
            for (int ni = 0; ni < _nodes.Count; ni++)
               Console.Error.WriteLine($"  {ni}: ({_nodes[ni][0]:F4},{_nodes[ni][1]:F4}) kind={_kinds[ni]}");
            Console.Error.WriteLine($"RUN COMPLETE. ALL TRIANGLES ({_triangles.Count}):");
            foreach (var t in _triangles)
               Console.Error.WriteLine($"  ({t.Item1},{t.Item2},{t.Item3})");
         }

         return new RawTriangulation(_nodes, _kinds, _triangles);
      }

      enum CaseAOutcome { NotApplicable, Continue, Split }

      void ProcessFront(List<int> front)
      {
         var skipQ = new HashSet<int>();
         while (front.Count >= 3)
         {
            if (_iterBudget-- <= 0)
               throw new TriangulationException("Превышено максимальное число итераций триангуляции (защита от зацикливания, §3.3).");

            if (front.Count == 3)
            {
               EmitFinalTriangle(front);
               return;
            }

            int n = front.Count;
            int bi = CandidateSearch.FindMinAngleIndex(front, _nodes, skipQ);
            if (bi < 0)
            {
               if (Environment.GetEnvironmentVariable("TRI_DEBUG") == "1")
               {
                  Console.Error.WriteLine($"FRONT DUMP (n={front.Count}):");
                  foreach (int idx in front) Console.Error.WriteLine($"  {idx}: ({_nodes[idx][0]:F4},{_nodes[idx][1]:F4}) h={_h[idx]:F4}");
                  foreach (var hf in _holeFronts)
                  {
                     Console.Error.WriteLine("HOLE:");
                     foreach (int idx in hf) Console.Error.WriteLine($"  {idx}: ({_nodes[idx][0]:F4},{_nodes[idx][1]:F4})");
                  }
                  Console.Error.WriteLine($"ALL NODES ({_nodes.Count}):");
                  for (int ni = 0; ni < _nodes.Count; ni++)
                     Console.Error.WriteLine($"  {ni}: ({_nodes[ni][0]:F4},{_nodes[ni][1]:F4}) kind={_kinds[ni]}");
                  Console.Error.WriteLine($"ALL TRIANGLES ({_triangles.Count}):");
                  foreach (var t in _triangles)
                     Console.Error.WriteLine($"  ({t.Item1},{t.Item2},{t.Item3})");
               }
               throw new TriangulationException("Не удалось продолжить триангуляцию: все тройки текущего фронта отклонены.");
            }

            int pIdx = front[(bi - 1 + n) % n], qIdx = front[bi], rIdx = front[(bi + 1) % n];

            if (DebugWatchQ != null && (DebugWatchQ.Contains(qIdx) || DebugWatchQ.Contains(pIdx) || DebugWatchQ.Contains(rIdx)))
               DebugSnapshot?.Invoke(qIdx, new List<int>(front), new List<double[]>(_nodes), new List<(int, int, int)>(_triangles));

            double alpha = GeometryUtils.AngleAtVertex(
               _nodes[pIdx][0], _nodes[pIdx][1], _nodes[qIdx][0], _nodes[qIdx][1], _nodes[rIdx][0], _nodes[rIdx][1]);

            var tCandidates = CandidateSearch.FindCandidateTs(front, pIdx, qIdx, rIdx, _nodes, _holeFronts, _constrainedPoints, _excluded);
            var outcome = CaseAOutcome.NotApplicable;
            foreach (int tCand in tCandidates)
            {
               outcome = TryCaseA(front, bi, pIdx, qIdx, rIdx, tCand);
               if (outcome != CaseAOutcome.NotApplicable) break;
            }
            if (Environment.GetEnvironmentVariable("TRI_DEBUG") == "1")
               Console.Error.WriteLine($"n={n} p={pIdx} q={qIdx} r={rIdx} alphaDeg={alpha * 180 / Math.PI:F1} tCandidates=[{string.Join(",", tCandidates)}] outcome={outcome}");

            if (outcome == CaseAOutcome.Split) return;
            if (outcome == CaseAOutcome.Continue) { skipQ.Clear(); continue; }

            bool handled = HandleCaseB(front, bi, pIdx, qIdx, rIdx, alpha, skipQ);
            if (handled) skipQ.Clear();
         }
      }

      // ───────────────────────────── Случай A (§3.2, A.1) — выбор диагонали четырёхугольника p,q,r,t, см. design doc §9а ─────────────────────────────

      CaseAOutcome TryCaseA(List<int> front, int qPos, int pIdx, int qIdx, int rIdx, int tIdx)
      {
         // Диагональ (p,r): треугольники (p,q,r) + (p,r,t).
         var variantDiagPR = new (int, int, int)[] { (pIdx, qIdx, rIdx), (pIdx, rIdx, tIdx) };
         // Диагональ (q,t): треугольники (p,q,t) + (q,r,t).
         var variantDiagQT = new (int, int, int)[] { (pIdx, qIdx, tIdx), (qIdx, rIdx, tIdx) };

         // Если t лежит ВНУТРИ треугольника (p,q,r) — а не просто в секторе угла q за пределами
         // отрезка (p,r) — диагональ (p,r) невалидна: треугольник (p,r,t) целиком перекрывал бы
         // (p,q,r). Единственно возможное разбиение в этом случае — по диагонали (q,t).
         bool tInsideTriangle = GeometryUtils.PointInTriangle(
            _nodes[pIdx][0], _nodes[pIdx][1], _nodes[qIdx][0], _nodes[qIdx][1], _nodes[rIdx][0], _nodes[rIdx][1],
            _nodes[tIdx][0], _nodes[tIdx][1]);

         bool prOk = !tInsideTriangle && IsNonDegenerate(variantDiagPR);
         bool qtOk = IsNonDegenerate(variantDiagQT);
         if (!prOk && !qtOk) return CaseAOutcome.NotApplicable;

         double maxPR = prOk ? Math.Max(MaxAngleOfTriangle(variantDiagPR[0]), MaxAngleOfTriangle(variantDiagPR[1])) : double.MaxValue;
         double maxQT = qtOk ? Math.Max(MaxAngleOfTriangle(variantDiagQT[0]), MaxAngleOfTriangle(variantDiagQT[1])) : double.MaxValue;

         // При равенстве максимальных углов побеждает диагональ (q,t) — по тексту спека для "Варианта 2".
         bool preferQT = qtOk && maxQT <= maxPR;

         bool tIsFront = front.Contains(tIdx);
         List<int>? holeFront = tIsFront ? null : _holeFronts.FirstOrDefault(h => h.Contains(tIdx));
         bool tIsHole = holeFront != null;

         var first = preferQT ? variantDiagQT : variantDiagPR;
         var second = preferQT ? variantDiagPR : variantDiagQT;
         bool firstOk = preferQT ? qtOk : prOk;
         bool secondOk = preferQT ? prOk : qtOk;

         // Если предпочтительный вариант не проходит проверки A.2 — пробуем второй, прежде чем сдаться (переход к случаю B).
         if (firstOk && ChecksPass(front, tIdx, first, tIsHole, holeFront))
            return FinishCandidate(front, qPos, pIdx, rIdx, tIdx, first, tIsFront, holeFront);
         if (secondOk && ChecksPass(front, tIdx, second, tIsHole, holeFront))
            return FinishCandidate(front, qPos, pIdx, rIdx, tIdx, second, tIsFront, holeFront);

         return CaseAOutcome.NotApplicable;
      }

      double MaxAngleOfTriangle((int, int, int) tri)
      {
         var (a, b, c) = tri;
         return Math.Max(AngleAt(b, a, c), Math.Max(AngleAt(a, b, c), AngleAt(a, c, b)));
      }

      double AngleAt(int p, int q, int r) => GeometryUtils.AngleAtVertex(
         _nodes[p][0], _nodes[p][1], _nodes[q][0], _nodes[q][1], _nodes[r][0], _nodes[r][1]);

      /// <summary>
      /// Строит выбранную пару треугольников и обновляет фронт (A.3). Разбиение/укорачивание фронта
      /// не зависит от того, какая диагональ была выбрана — обе потребляют одни и те же рёбра
      /// (p,q),(q,r) и экспонируют одни и те же новые рёбра (r,t),(t,p) (см. design doc §9а/§9б).
      /// </summary>
      CaseAOutcome FinishCandidate(List<int> front, int qPos, int pIdx, int rIdx, int tIdx, (int, int, int)[] tris, bool tIsFront, List<int>? holeFront)
      {
         EmitTriangle(tris[0].Item1, tris[0].Item2, tris[0].Item3);
         EmitTriangle(tris[1].Item1, tris[1].Item2, tris[1].Item3);

         if (tIsFront)
            return SplitFront(front, pIdx, tIdx, rIdx);

         // Тип 2 (t на отверстии) — слияние петель (loop-merge, первоисточник): вся петля отверстия
         // вшивается во фронт, отверстие перестаёт быть отдельным контуром (см. spec §3.2, design doc).
         if (holeFront != null)
            return MergeHoleIntoFront(front, qPos, tIdx, holeFront);

         // Тип 3 (constrained point) — одиночный узел, петли нет: q заменяется на t, t исключается из кандидатов.
         front[qPos] = tIdx;
         _excluded.Add(tIdx);
         return CaseAOutcome.Continue;
      }

      /// <summary>
      /// Слияние петли отверстия с активным фронтом (§3.2, Тип 2 — реализация через loop-merge).
      /// Обе диагонали Случая A потребляют рёбра (p,q),(q,r) и экспонируют (p,t),(t,r); узел q заменяется
      /// на всю петлю отверстия в её порядке обхода (CW, §1.1), начиная и заканчивая на t — так возникает
      /// «мост»-перемычка (узел t сдвоен), превращающий многосвязный фронт в односвязный. Область отверстия
      /// оказывается СНАРУЖИ нового фронта (обход CW внутри CCW-фронта = вырез), поэтому отверстие больше
      /// не триангулируется и удаляется из списка активных отверстий.
      /// </summary>
      CaseAOutcome MergeHoleIntoFront(List<int> front, int qPos, int tIdx, List<int> holeFront)
      {
         int m = holeFront.Count;
         int tPos = holeFront.IndexOf(tIdx);
         var insert = new List<int>(m + 1);
         for (int i = 0; i < m; i++) insert.Add(holeFront[(tPos + i) % m]); // t, h1, ..., h(m-1)
         insert.Add(tIdx);                                                   // замыкающий t (мост)

         front.RemoveAt(qPos);
         front.InsertRange(qPos, insert);
         _holeFronts.Remove(holeFront);
         return CaseAOutcome.Continue;
      }

      bool ChecksPass(List<int> front, int tIdx, (int, int, int)[] newTris, bool tIsHole, List<int>? holeFront)
      {
         bool dbg = Environment.GetEnvironmentVariable("TRI_DEBUG") == "1";

         // Проверка 1 — пересечения (§3.2 A.2).
         foreach (var tri in newTris)
            if (!EdgesClear(front, tri)) { if (dbg) Console.Error.WriteLine($"    ChecksPass FAIL#1 edges tri=({tri.Item1},{tri.Item2},{tri.Item3})"); return false; }

         // Проверка 2 — отрезок внутри треугольника: рёбра отверстия у t + все constrained segments.
         if (tIsHole)
         {
            int hp = holeFront!.IndexOf(tIdx);
            int prevIdx = holeFront[(hp - 1 + holeFront.Count) % holeFront.Count];
            int nextIdx = holeFront[(hp + 1) % holeFront.Count];
            foreach (var tri in newTris)
            {
               if (SegmentInsideTriangle(prevIdx, tIdx, tri)) { if (dbg) Console.Error.WriteLine($"    ChecksPass FAIL#2a segInside prev={prevIdx} t={tIdx} tri=({tri.Item1},{tri.Item2},{tri.Item3})"); return false; }
               if (SegmentInsideTriangle(tIdx, nextIdx, tri)) { if (dbg) Console.Error.WriteLine($"    ChecksPass FAIL#2b segInside t={tIdx} next={nextIdx} tri=({tri.Item1},{tri.Item2},{tri.Item3})"); return false; }
            }
         }
         foreach (var (a, b) in _constrainedSegments)
            foreach (var tri in newTris)
               if (SegmentInsideTriangle(a, b, tri)) { if (dbg) Console.Error.WriteLine($"    ChecksPass FAIL#2c constrained"); return false; }

         // Проверка 3 — ограничение длины рёбер из t (только Тип 2).
         if (tIsHole)
         {
            double kh = KLenMultiplier * _h[tIdx];
            foreach (var tri in newTris)
               foreach (var (a, b) in EdgesOf(tri))
                  if ((a == tIdx || b == tIdx) && Dist(a, b) > kh)
                     { if (dbg) Console.Error.WriteLine($"    ChecksPass FAIL#3 длина ребра из t={tIdx}"); return false; }
         }

         // Опциональная страховка CentroidCovered.
         foreach (var tri in newTris)
            if (SegmentGeometry.CentroidCovered(tri.Item1, tri.Item2, tri.Item3, _nodes, _triangles))
               { if (dbg) Console.Error.WriteLine($"    ChecksPass FAIL#4 centroidCovered tri=({tri.Item1},{tri.Item2},{tri.Item3})"); return false; }

         return true;
      }

      CaseAOutcome SplitFront(List<int> front, int pIdx, int tIdx, int rIdx)
      {
         var sub1 = WalkForward(front, rIdx, tIdx);
         var sub2 = WalkForward(front, tIdx, pIdx);
         PushFront(sub1);
         PushFront(sub2);
         return CaseAOutcome.Split;
      }

      static List<int> WalkForward(List<int> front, int fromValue, int toValue)
      {
         int n = front.Count;
         int pos = front.IndexOf(fromValue);
         var result = new List<int>();
         while (true)
         {
            int v = front[pos];
            result.Add(v);
            if (v == toValue) break;
            pos = (pos + 1) % n;
         }
         return result;
      }

      void PushFront(List<int> sub)
      {
         if (sub.Count >= 3) _fronts.Push(sub);
      }

      // ───────────────────────────── Случай B (§3.2) ─────────────────────────────

      bool HandleCaseB(List<int> front, int qPos, int pIdx, int qIdx, int rIdx, double alpha, HashSet<int> skipQ)
      {
         if (alpha <= _alphaThresholdRad + 1e-9)
         {
            EmitTriangle(pIdx, qIdx, rIdx);
            front.RemoveAt(qPos);
            return true;
         }

         bool dbgB2 = Environment.GetEnvironmentVariable("TRI_DEBUG") == "1";
         int gIdx = AddBisectorNode(pIdx, qIdx, rIdx);
         if (dbgB2) Console.Error.WriteLine($"  B.2 bisector g={gIdx} at ({_nodes[gIdx][0]:F4},{_nodes[gIdx][1]:F4})");
         if (!TrianglesClear(front, pIdx, qIdx, gIdx, rIdx))
         {
            if (dbgB2) Console.Error.WriteLine($"    bisector g rejected");
            RemoveLastNode();
            gIdx = AddMidpointNode(pIdx, rIdx, _h[qIdx]);
            if (dbgB2) Console.Error.WriteLine($"  B.2 midpoint g={gIdx} at ({_nodes[gIdx][0]:F4},{_nodes[gIdx][1]:F4})");
            if (!TrianglesClear(front, pIdx, qIdx, gIdx, rIdx))
            {
               if (dbgB2) Console.Error.WriteLine($"    midpoint g rejected too");
               RemoveLastNode();
               skipQ.Add(qIdx);
               return false;
            }
         }

         EmitTriangle(pIdx, qIdx, gIdx);
         EmitTriangle(gIdx, qIdx, rIdx);
         front[qPos] = gIdx;
         return true;
      }

      bool TrianglesClear(List<int> front, int pIdx, int qIdx, int gIdx, int rIdx)
      {
         var tris = new (int, int, int)[] { (pIdx, qIdx, gIdx), (gIdx, qIdx, rIdx) };
         foreach (var tri in tris)
         {
            if (!EdgesClear(front, tri)) return false;
            foreach (var (a, b) in _constrainedSegments)
               if (SegmentInsideTriangle(a, b, tri)) return false;
         }
         return true;
      }

      int AddBisectorNode(int pIdx, int qIdx, int rIdx)
      {
         double px = _nodes[pIdx][0], py = _nodes[pIdx][1];
         double qx = _nodes[qIdx][0], qy = _nodes[qIdx][1];
         double rx = _nodes[rIdx][0], ry = _nodes[rIdx][1];
         double qpx = px - qx, qpy = py - qy, qrx = rx - qx, qry = ry - qy;
         double qpLen = Math.Sqrt(qpx * qpx + qpy * qpy), qrLen = Math.Sqrt(qrx * qrx + qry * qry);
         // Расстояние g от q — целевой параметр густоты h в узле q (а не min(qp,qr), который
         // укорачивался бы с каждой вставленной биссектрисой в одном месте и давал самоусиливающееся
         // стягивание узлов в точку — видимый "шов" на сетке прямоугольника).
         double dist = _h[qIdx];

         double qpNx = qpx / qpLen, qpNy = qpy / qpLen, qrNx = qrx / qrLen, qrNy = qry / qrLen;
         double bx = qpNx + qrNx, by = qpNy + qrNy;
         double bLen = Math.Sqrt(bx * bx + by * by);
         if (bLen < 1e-9) { bx = -qrNy; by = qrNx; bLen = 1.0; }
         bx /= bLen; by /= bLen;

         return AddInteriorNode(qx + dist * bx, qy + dist * by, _h[qIdx]);
      }

      int AddMidpointNode(int pIdx, int rIdx, double h) =>
         AddInteriorNode((_nodes[pIdx][0] + _nodes[rIdx][0]) / 2.0, (_nodes[pIdx][1] + _nodes[rIdx][1]) / 2.0, h);

      int AddInteriorNode(double x, double y, double h)
      {
         int idx = _nodes.Count;
         _nodes.Add(new[] { x, y });
         _kinds.Add(ContourNodeKind.Interior);
         _h.Add(h);
         return idx;
      }

      void RemoveLastNode()
      {
         _nodes.RemoveAt(_nodes.Count - 1);
         _kinds.RemoveAt(_kinds.Count - 1);
         _h.RemoveAt(_h.Count - 1);
      }

      // ───────────────────────────── Общие помощники ─────────────────────────────

      void EmitTriangle(int a, int b, int c)
      {
         _triangles.Add((a, b, c));
         if (Environment.GetEnvironmentVariable("TRI_DEBUG") == "1")
         {
            double e1 = Dist(a, b), e2 = Dist(b, c), e3 = Dist(c, a);
            double maxE = Math.Max(e1, Math.Max(e2, e3));
            if (maxE > 2 * Math.Max(_h[a], Math.Max(_h[b], _h[c])))
               Console.Error.WriteLine($"    EMIT LONG tri=({a},{b},{c}) maxEdge={maxE:F4}");
         }
      }

      void EmitFinalTriangle(List<int> front)
      {
         if (IsNonDegenerate(new[] { (front[0], front[1], front[2]) }))
            EmitTriangle(front[0], front[1], front[2]);
      }

      bool IsNonDegenerate((int, int, int)[] tris)
      {
         foreach (var (a, b, c) in tris)
         {
            double area2 = Math.Abs(
               (_nodes[b][0] - _nodes[a][0]) * (_nodes[c][1] - _nodes[a][1]) -
               (_nodes[c][0] - _nodes[a][0]) * (_nodes[b][1] - _nodes[a][1]));
            if (area2 < 1e-12) return false;
         }
         return true;
      }

      double Dist(int i, int j)
      {
         double dx = _nodes[i][0] - _nodes[j][0], dy = _nodes[i][1] - _nodes[j][1];
         return Math.Sqrt(dx * dx + dy * dy);
      }

      static IEnumerable<(int, int)> EdgesOf((int, int, int) t)
      {
         yield return (t.Item1, t.Item2);
         yield return (t.Item2, t.Item3);
         yield return (t.Item3, t.Item1);
      }

      static IEnumerable<(int, int)> LoopEdges(List<int> loop)
      {
         int n = loop.Count;
         for (int i = 0; i < n; i++) yield return (loop[i], loop[(i + 1) % n]);
      }

      bool EdgeClearAgainst(int a, int b, IEnumerable<(int, int)> edges, string src = "")
      {
         double ax = _nodes[a][0], ay = _nodes[a][1], bx = _nodes[b][0], by = _nodes[b][1];
         foreach (var (c, d) in edges)
         {
            if (c == a || c == b || d == a || d == b) continue; // общий конец — стык, не пересечение (§3.5)
            if (SegmentGeometry.SegmentsIntersect(ax, ay, bx, by, _nodes[c][0], _nodes[c][1], _nodes[d][0], _nodes[d][1]))
            {
               if (Environment.GetEnvironmentVariable("TRI_DEBUG") == "1")
                  Console.Error.WriteLine($"      edge ({a},{b}) crosses ({c},{d}) [{src}]");
               return false;
            }
         }
         return true;
      }

      bool EdgesClear(List<int> front, (int, int, int) tri)
      {
         foreach (var (a, b) in EdgesOf(tri))
         {
            if (!EdgeClearAgainst(a, b, LoopEdges(front), "front")) return false;
            foreach (var hf in _holeFronts)
               if (!EdgeClearAgainst(a, b, LoopEdges(hf), "hole")) return false;
            if (!EdgeClearAgainst(a, b, _constrainedSegments, "constrained")) return false;
            if (!EdgeClearAgainst(a, b, _triangles.SelectMany(EdgesOf), "triangles")) return false;
         }
         return true;
      }

      bool SegmentInsideTriangle(int segA, int segB, (int, int, int) tri)
      {
         return SegmentGeometry.SegmentFullyInsideTriangle(
            _nodes[segA][0], _nodes[segA][1], _nodes[segB][0], _nodes[segB][1],
            _nodes[tri.Item1][0], _nodes[tri.Item1][1],
            _nodes[tri.Item2][0], _nodes[tri.Item2][1],
            _nodes[tri.Item3][0], _nodes[tri.Item3][1]);
      }
   }
}
