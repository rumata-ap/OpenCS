using System;
using System.Collections.Generic;

namespace CSTriangulation
{
   /// <summary>Поиск минимального угла (§3.1) и кандидата t (§3.2).</summary>
   internal static class CandidateSearch
   {
      /// <summary>
      /// §3.1: минимальный угол → при равенстве минимальная min(qp,qr) → при равенстве минимальный индекс q.
      /// Узлы из <paramref name="skipQ"/> (уже испробованные и отклонённые в текущем раунде обработки фронта) исключаются.
      /// Сначала рассматриваются только выпуклые вершины (иначе "минимальный угол" может указать на
      /// рефлексную/вогнутую вершину — построенное по ней "ухо" ляжет за пределами контура). Если на
      /// фронте не осталось ни одной выпуклой вершины (вырожденный случай) — откат на полный перебор.
      /// </summary>
      public static int FindMinAngleIndex(List<int> front, List<double[]> nodes, HashSet<int>? skipQ = null)
      {
         int best = FindMinAngleIndexCore(front, nodes, skipQ, convexOnly: true);
         if (best < 0)
            best = FindMinAngleIndexCore(front, nodes, skipQ, convexOnly: false);
         return best;
      }

      static int FindMinAngleIndexCore(List<int> front, List<double[]> nodes, HashSet<int>? skipQ, bool convexOnly)
      {
         int n = front.Count;
         int best = -1;
         double bestAngle = double.MaxValue, bestLen = double.MaxValue;
         int bestQ = int.MaxValue;

         for (int i = 0; i < n; i++)
         {
            int p = front[(i - 1 + n) % n], q = front[i], r = front[(i + 1) % n];
            if (skipQ != null && skipQ.Contains(q)) continue;

            if (convexOnly)
            {
               double cross = (nodes[q][0] - nodes[p][0]) * (nodes[r][1] - nodes[q][1]) -
                              (nodes[q][1] - nodes[p][1]) * (nodes[r][0] - nodes[q][0]);
               if (cross <= 0) continue; // рефлексная вершина (контур CCW) — пропускаем на первом проходе
            }

            double angle = GeometryUtils.AngleAtVertex(
               nodes[p][0], nodes[p][1], nodes[q][0], nodes[q][1], nodes[r][0], nodes[r][1]);
            double qp = Dist(nodes, p, q), qr = Dist(nodes, q, r);
            double minLen = Math.Min(qp, qr);

            bool better = angle < bestAngle - 1e-12
               || (Math.Abs(angle - bestAngle) <= 1e-12 && minLen < bestLen - 1e-12)
               || (Math.Abs(angle - bestAngle) <= 1e-12 && Math.Abs(minLen - bestLen) <= 1e-12 && q < bestQ);

            if (better)
            {
               best = i;
               bestAngle = angle;
               bestLen = minLen;
               bestQ = q;
            }
         }
         return best;
      }

      /// <summary>
      /// §3.2, случай A: кандидаты t среди узлов текущего фронта (Тип 1), отверстий (Тип 2)
      /// и constrained points (Тип 3), ещё не исключённых. Возвращает ВСЕХ подходящих кандидатов,
      /// отсортированных по расстоянию до q (ближайший первый) — ближайший не всегда даёт
      /// геометрически валидное построение (обе диагонали могут пересекать другую геометрию),
      /// поэтому вызывающий код обязан перебрать список, а не останавливаться на первом элементе.
      /// "Сектор угла α" — это угловой сектор при вершине q, ограниченный лучами q→p и q→r (может
      /// простираться за пределы отрезка p-r), а НЕ замкнутый треугольник (p,q,r): t обычно лежит
      /// снаружи треугольника p,q,r, ближе к продолжению фронта за p/r (см. иллюстрацию спецификации).
      /// </summary>
      public static List<int> FindCandidateTs(List<int> front, int pIdx, int qIdx, int rIdx,
         List<double[]> nodes, List<List<int>> holeFronts, List<int> constrainedPoints, HashSet<int> excluded)
      {
         double px = nodes[pIdx][0], py = nodes[pIdx][1];
         double qx = nodes[qIdx][0], qy = nodes[qIdx][1];
         double rx = nodes[rIdx][0], ry = nodes[rIdx][1];
         double qp = Dist(nodes, pIdx, qIdx), qr = Dist(nodes, qIdx, rIdx);

         // Верхняя граница на qt: длина вектора qp+qr (векторная сумма, не сумма длин) — не даёт
         // кандидату оказаться дальше, чем "естественный охват" двух текущих граней фронта.
         double sumX = (px - qx) + (rx - qx), sumY = (py - qy) + (ry - qy);
         double maxQt = Math.Sqrt(sumX * sumX + sumY * sumY);

         var candidates = new List<(int Idx, double Dist)>();

         void Consider(int idx)
         {
            if (idx == pIdx || idx == qIdx || idx == rIdx || excluded.Contains(idx)) return;
            double tx = nodes[idx][0], ty = nodes[idx][1];
            if (!GeometryUtils.PointInSector(px, py, qx, qy, rx, ry, tx, ty)) return;

            double pt = Dist(nodes, pIdx, idx), rt = Dist(nodes, rIdx, idx);
            if (!(pt < qp) && !(rt < qr)) return;

            double qt = Dist(nodes, qIdx, idx);
            if (qt > maxQt) return;

            candidates.Add((idx, qt));
         }

         foreach (int idx in front) Consider(idx);
         foreach (var hole in holeFronts)
            foreach (int idx in hole) Consider(idx);
         foreach (int idx in constrainedPoints) Consider(idx);

         candidates.Sort((a, b) => a.Dist.CompareTo(b.Dist));
         return candidates.ConvertAll(c => c.Idx);
      }

      static double Dist(List<double[]> nodes, int i, int j)
      {
         double dx = nodes[i][0] - nodes[j][0], dy = nodes[i][1] - nodes[j][1];
         return Math.Sqrt(dx * dx + dy * dy);
      }
   }
}
