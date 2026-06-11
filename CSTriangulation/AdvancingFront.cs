using System;
using System.Collections.Generic;

namespace CSTriangulation
{
   /// <summary>
   /// Триангуляция методом продвижения фронта (SETKA-4N-2D).
   /// Поддерживает отверстия, контроль размера ячеек через h-значения,
   /// и разбиение «игольчатых» подобластей (Branch A/A'/B/C).
   /// </summary>
   public static class AdvancingFront
   {
      const double Deg2Rad = Math.PI / 180.0;

      /// <summary>
      /// Выполняет триангуляцию контура методом продвижения фронта.
      /// </summary>
      /// <param name="contour">Дискретизированный контур (внешний + отверстия).</param>
      /// <param name="alphaThreshold">Пороговый угол (градусы) для ветви C (по умолчанию 90°).</param>
      /// <returns>Результат триангуляции (узлы, треугольники, признаки границы).</returns>
      public static TriangulationResult Triangulate(DiscretizedContour contour, double alphaThreshold = 90.0)
      {
         double alphaRad = alphaThreshold * Deg2Rad;
         var nodesList = new List<double[]>(contour.Nodes.Length);
         for (int i = 0; i < contour.Nodes.Length; i++)
            nodesList.Add(new double[] { contour.Nodes[i][0], contour.Nodes[i][1] });
         var boundaryFlags = new List<bool>(contour.IsBoundary.Length);
         for (int i = 0; i < contour.IsBoundary.Length; i++)
            boundaryFlags.Add(contour.IsBoundary[i]);
         var hValues = new List<double>(contour.HValues.Length);
         for (int i = 0; i < contour.HValues.Length; i++)
            hValues.Add(contour.HValues[i]);

         var merged = PremergeHoles(contour.OuterIndices, contour.HoleIndices, nodesList, boundaryFlags, hValues);

         List<List<(double X, double Y)>> holePolys = null;
         if (contour.HoleIndices != null && contour.HoleIndices.Count > 0)
         {
            holePolys = new List<List<(double X, double Y)>>(contour.HoleIndices.Count);
            foreach (var hi in contour.HoleIndices)
            {
               var hp = new List<(double, double)>(hi.Count);
               foreach (int idx in hi)
                  hp.Add((nodesList[idx][0], nodesList[idx][1]));
               holePolys.Add(hp);
            }
         }

         var triangles = new List<(int, int, int)>();
         var stack = new Stack<List<int>>();
         stack.Push(merged);

         while (stack.Count > 0)
         {
            var current = stack.Pop();
            ProcessContour(current, stack, nodesList, boundaryFlags, triangles, alphaRad, hValues, holePolys);
         }

         var validTriangles = new List<(int, int, int)>();
         for (int t = 0; t < triangles.Count; t++)
         {
            int i = triangles[t].Item1, j = triangles[t].Item2, k = triangles[t].Item3;
            double area = Math.Abs(
               (nodesList[j][0] - nodesList[i][0]) * (nodesList[k][1] - nodesList[i][1]) -
               (nodesList[k][0] - nodesList[i][0]) * (nodesList[j][1] - nodesList[i][1]));
            if (area > 1e-10)
               validTriangles.Add(triangles[t]);
         }

         // WeldCoincidentNodes только если есть реально совпадающие узлы
         // (после fix SplitAtNeedle они не должны возникать)
         var result = CompactNodes(nodesList, boundaryFlags, validTriangles);

         return result;
      }

      static List<int> PremergeHoles(List<int> outerIndices, List<List<int>> holeIndices,
         List<double[]> nodesList, List<bool> boundaryFlags, List<double> hValues)
      {
         if (holeIndices == null || holeIndices.Count == 0)
            return new List<int>(outerIndices);

         var holesSorted = new List<List<int>>(holeIndices.Count);
         foreach (var h in holeIndices)
         {
            double maxX = double.MinValue;
            foreach (int idx in h)
               if (nodesList[idx][0] > maxX) maxX = nodesList[idx][0];
            holesSorted.Add(new List<int>(h));
         }
         holesSorted.Sort((a, b) =>
         {
            double maxA = double.MinValue, maxB = double.MinValue;
            foreach (int idx in a) if (nodesList[idx][0] > maxA) maxA = nodesList[idx][0];
            foreach (int idx in b) if (nodesList[idx][0] > maxB) maxB = nodesList[idx][0];
            return maxB.CompareTo(maxA);
         });

         var result = new List<int>(outerIndices);

         foreach (var hole in holesSorted)
         {
            int mPos = 0;
            double mx = double.MinValue;
            for (int i = 0; i < hole.Count; i++)
            {
               if (nodesList[hole[i]][0] > mx)
               {
                  mx = nodesList[hole[i]][0];
                  mPos = i;
               }
            }
            int mIdx = hole[mPos];
            double my = nodesList[mIdx][1];

            double bestX = double.MaxValue;
            int bestSeg = -1;
            int nRes = result.Count;
            for (int i = 0; i < nRes; i++)
            {
               int aIdx = result[i];
               int bIdx = result[(i + 1) % nRes];
               double ay = nodesList[aIdx][1], by = nodesList[bIdx][1];
               if (Math.Abs(ay - by) < 1e-12) continue;
               if ((ay <= my && by > my) || (by <= my && ay > my))
               {
                  double t = (my - ay) / (by - ay);
                  double xi = nodesList[aIdx][0] + t * (nodesList[bIdx][0] - nodesList[aIdx][0]);
                  if (xi > mx + 1e-9 && xi < bestX)
                  {
                     bestX = xi;
                     bestSeg = i;
                  }
               }
            }

            int rPos;
            if (bestSeg < 0)
            {
               var outerSet = new HashSet<int>(outerIndices);
               double bestDist = double.MaxValue;
               rPos = -1;
               foreach (int hIdx in hole)
               {
                  double hx = nodesList[hIdx][0], hy2 = nodesList[hIdx][1];
                  for (int rp = 0; rp < result.Count; rp++)
                  {
                     int rIdx = result[rp];
                     if (!outerSet.Contains(rIdx)) continue;
                     double d = Math.Sqrt(Math.Pow(nodesList[rIdx][0] - hx, 2) + Math.Pow(nodesList[rIdx][1] - hy2, 2));
                     if (d < bestDist)
                     {
                        bestDist = d;
                        rPos = rp;
                        mIdx = hIdx;
                        mx = hx;
                        my = hy2;
                     }
                  }
               }
               if (rPos < 0) continue;
            }
            else
            {
               int aIdx = result[bestSeg];
               int bIdx = result[(bestSeg + 1) % nRes];
               if (nodesList[aIdx][0] >= nodesList[bIdx][0])
                  rPos = bestSeg;
               else
                  rPos = (bestSeg + 1) % nRes;
            }

            int rNode = result[rPos];
            double rx = nodesList[rNode][0], ry2 = nodesList[rNode][1];
            double bridgeLen = Math.Sqrt(Math.Pow(rx - mx, 2) + Math.Pow(ry2 - my, 2));
            double avgH = 1.0;
            if (hValues != null && rNode < hValues.Count && mIdx < hValues.Count)
            {
               avgH = (hValues[rNode] + hValues[mIdx]) / 2.0;
               if (avgH < 1e-10) avgH = 1.0;
            }

            int nSeg = Math.Max(1, (int)Math.Ceiling(bridgeLen / avgH));
            var bridgeNodes = new List<int>();
            for (int s = 1; s < nSeg; s++)
            {
               double t2 = (double)s / nSeg;
               double gx = rx + t2 * (mx - rx);
               double gy = ry2 + t2 * (my - ry2);
               int gIdx = nodesList.Count;
               nodesList.Add(new double[] { gx, gy });
               boundaryFlags.Add(false);
               hValues.Add(avgH);
               bridgeNodes.Add(gIdx);
            }

            var holeRot = new List<int>(hole.Count);
            for (int i = mPos; i < hole.Count; i++) holeRot.Add(hole[i]);
            for (int i = 0; i < mPos; i++) holeRot.Add(hole[i]);

            var insert = new List<int>(bridgeNodes.Count + holeRot.Count + bridgeNodes.Count + 2);
            insert.AddRange(bridgeNodes);
            insert.AddRange(holeRot);
            insert.Add(mIdx);
            insert.AddRange(bridgeNodes);
            insert.Add(rNode);

            result.InsertRange(rPos + 1, insert);
         }

         return result;
      }

      static void ProcessContour(List<int> current, Stack<List<int>> stack,
         List<double[]> nodesList, List<bool> boundaryFlags,
         List<(int, int, int)> triangles, double alphaRad, List<double> hValues,
         List<List<(double X, double Y)>> holePolys)
      {
         if (current.Count < 3) return;
         if (current.Count == 3)
         {
            int i3 = current[0], j3 = current[1], k3 = current[2];
            if (!TriInHole(i3, j3, k3, nodesList, holePolys) &&
                !CentroidCovered(i3, j3, k3, nodesList, triangles))
               triangles.Add((i3, j3, k3));
            return;
         }

         int maxIter = Math.Max(500, current.Count * current.Count * 4);

         while (current.Count > 3 && maxIter > 0)
         {
            maxIter--;
            int n = current.Count;

            int bestI = FindMinAngleIdx(current, nodesList);
            int pIdx = current[(bestI - 1 + n) % n];
            int qIdx = current[bestI];
            int rIdx = current[(bestI + 1) % n];

            double alpha = GeometryUtils.AngleAtVertex(
               nodesList[pIdx][0], nodesList[pIdx][1],
               nodesList[qIdx][0], nodesList[qIdx][1],
               nodesList[rIdx][0], nodesList[rIdx][1]);

            int tIdx = FindTInCurrent(current, nodesList, pIdx, qIdx, rIdx, hValues);
            if (tIdx >= 0)
            {
               if (TryBranchA(current, stack, nodesList, pIdx, qIdx, rIdx, bestI, tIdx, triangles, holePolys))
                  return;
            }

            if (tIdx < 0)
            {
               int tInEar = FindNodeInEar(current, nodesList, pIdx, qIdx, rIdx);
               if (tInEar >= 0)
               {
                  if (TryBranchA(current, stack, nodesList, pIdx, qIdx, rIdx, bestI, tInEar, triangles, holePolys))
                     return;
               }
            }

            bool useBranchC = (alpha >= alphaRad - 1e-5);

            if (useBranchC)
            {
               int gIdx = BisectorNode(nodesList, boundaryFlags, hValues, pIdx, qIdx, rIdx);
               if (EdgeClear(current, nodesList, pIdx, gIdx) &&
                   EdgeClear(current, nodesList, gIdx, rIdx) &&
                   !CentroidCovered(pIdx, qIdx, gIdx, nodesList, triangles) &&
                   !CentroidCovered(gIdx, qIdx, rIdx, nodesList, triangles) &&
                   !TriInHole(pIdx, qIdx, gIdx, nodesList, holePolys) &&
                   !TriInHole(gIdx, qIdx, rIdx, nodesList, holePolys))
               {
                  current[bestI] = gIdx;
                  double minEdge = MinContourEdge(current, nodesList);
                  double threshold = Math.Max(minEdge / 2, 1e-6);
                  int snapPos = FindProximitySnap(current, nodesList, bestI, threshold);
                  if (snapPos >= 0)
                  {
                     int snapIdx = current[snapPos];
                     if (EdgeClear(current, nodesList, pIdx, snapIdx) &&
                         EdgeClear(current, nodesList, snapIdx, rIdx) &&
                         CapClear(current, nodesList, pIdx, qIdx, snapIdx) &&
                         CapClear(current, nodesList, snapIdx, qIdx, rIdx) &&
                         !CentroidCovered(pIdx, qIdx, snapIdx, nodesList, triangles) &&
                         !CentroidCovered(snapIdx, qIdx, rIdx, nodesList, triangles))
                     {
                        nodesList.RemoveAt(nodesList.Count - 1);
                        boundaryFlags.RemoveAt(boundaryFlags.Count - 1);
                        hValues.RemoveAt(hValues.Count - 1);
                        triangles.Add((pIdx, qIdx, snapIdx));
                        triangles.Add((snapIdx, qIdx, rIdx));
                        current[bestI] = snapIdx;
                        SplitAtNeedle(current, stack, nodesList, bestI, snapPos, holePolys);
                        return;
                     }
                     else
                     {
                        current[bestI] = qIdx;
                        nodesList.RemoveAt(nodesList.Count - 1);
                        boundaryFlags.RemoveAt(boundaryFlags.Count - 1);
                        hValues.RemoveAt(hValues.Count - 1);
                        int gMid = MidpointNode(nodesList, boundaryFlags, hValues, pIdx, rIdx, hValues[qIdx]);
                        if (!TriInHole(pIdx, qIdx, gMid, nodesList, holePolys) &&
                            !TriInHole(gMid, qIdx, rIdx, nodesList, holePolys))
                        {
                           triangles.Add((pIdx, qIdx, gMid));
                           triangles.Add((gMid, qIdx, rIdx));
                           current[bestI] = gMid;
                        }
                        else
                        {
                           nodesList.RemoveAt(nodesList.Count - 1);
                           boundaryFlags.RemoveAt(boundaryFlags.Count - 1);
                           hValues.RemoveAt(hValues.Count - 1);
                           current.RemoveAt(bestI);
                        }
                        continue;
                     }
                  }
                  triangles.Add((pIdx, qIdx, gIdx));
                  triangles.Add((gIdx, qIdx, rIdx));
               }
               else
               {
                  nodesList.RemoveAt(nodesList.Count - 1);
                  boundaryFlags.RemoveAt(boundaryFlags.Count - 1);
                  hValues.RemoveAt(hValues.Count - 1);
                  int gMid = MidpointNode(nodesList, boundaryFlags, hValues, pIdx, rIdx, hValues[qIdx]);
                  if (!TriInHole(pIdx, qIdx, gMid, nodesList, holePolys) &&
                      !TriInHole(gMid, qIdx, rIdx, nodesList, holePolys))
                  {
                     triangles.Add((pIdx, qIdx, gMid));
                     triangles.Add((gMid, qIdx, rIdx));
                     current[bestI] = gMid;
                  }
                  else
                  {
                     nodesList.RemoveAt(nodesList.Count - 1);
                     boundaryFlags.RemoveAt(boundaryFlags.Count - 1);
                     hValues.RemoveAt(hValues.Count - 1);
                     current.RemoveAt(bestI);
                  }
               }
            }
            else
            {
               if (IsEar(nodesList, current, pIdx, qIdx, rIdx) &&
                   !TriInHole(pIdx, qIdx, rIdx, nodesList, holePolys))
               {
                  triangles.Add((pIdx, qIdx, rIdx));
                  current.RemoveAt(bestI);
               }
               else
               {
                  bool foundEar = false;
                  for (int i2 = 0; i2 < current.Count; i2++)
                  {
                     int pi2 = current[(i2 - 1 + current.Count) % current.Count];
                     int qi2 = current[i2];
                     int ri2 = current[(i2 + 1) % current.Count];
                     double angleQi = GeometryUtils.AngleAtVertex(
                        nodesList[pi2][0], nodesList[pi2][1],
                        nodesList[qi2][0], nodesList[qi2][1],
                        nodesList[ri2][0], nodesList[ri2][1]);
                     if (angleQi >= alphaRad - 1e-5) continue;
                     if (IsEar(nodesList, current, pi2, qi2, ri2) &&
                         !TriInHole(pi2, qi2, ri2, nodesList, holePolys))
                     {
                        triangles.Add((pi2, qi2, ri2));
                        current.RemoveAt(i2);
                        foundEar = true;
                        break;
                     }
                  }
                  if (!foundEar) break;
               }
            }
         }

         if (current.Count == 3)
         {
            int i2 = current[0], j2 = current[1], k2 = current[2];
            if (!TriInHole(i2, j2, k2, nodesList, holePolys) &&
                !CentroidCovered(i2, j2, k2, nodesList, triangles))
               triangles.Add((i2, j2, k2));
         }
      }

      static int FindMinAngleIdx(List<int> current, List<double[]> nodesList)
      {
         int n = current.Count;
         int bestI = 0;
         double bestAngle = double.MaxValue;
         for (int i = 0; i < n; i++)
         {
            int p = current[(i - 1 + n) % n], q = current[i], r = current[(i + 1) % n];
            double cross = (nodesList[q][0] - nodesList[p][0]) * (nodesList[r][1] - nodesList[q][1]) -
                          (nodesList[q][1] - nodesList[p][1]) * (nodesList[r][0] - nodesList[q][0]);
            if (cross <= 0) continue;
            double angle = GeometryUtils.AngleAtVertex(
               nodesList[p][0], nodesList[p][1],
               nodesList[q][0], nodesList[q][1],
               nodesList[r][0], nodesList[r][1]);
            if (angle < bestAngle)
            {
               bestAngle = angle;
               bestI = i;
            }
         }
         if (bestAngle == double.MaxValue)
         {
            for (int i = 0; i < current.Count; i++)
            {
               int p2 = current[(i - 1 + current.Count) % current.Count], q2 = current[i], r2 = current[(i + 1) % current.Count];
               double angle = GeometryUtils.AngleAtVertex(
                  nodesList[p2][0], nodesList[p2][1],
                  nodesList[q2][0], nodesList[q2][1],
                  nodesList[r2][0], nodesList[r2][1]);
               if (angle < bestAngle)
               {
                  bestAngle = angle;
                  bestI = i;
               }
            }
         }
         return bestI;
      }

      static int FindTInCurrent(List<int> current, List<double[]> nodesList,
         int pIdx, int qIdx, int rIdx, List<double> hValues)
      {
         double px = nodesList[pIdx][0], py = nodesList[pIdx][1];
         double qx = nodesList[qIdx][0], qy = nodesList[qIdx][1];
         double rx = nodesList[rIdx][0], ry = nodesList[rIdx][1];
         double qpLen = Math.Sqrt(Math.Pow(px - qx, 2) + Math.Pow(py - qy, 2));
         double qrLen = Math.Sqrt(Math.Pow(rx - qx, 2) + Math.Pow(ry - qy, 2));
         double hQ = qIdx < hValues.Count ? hValues[qIdx] : 1.0;

         int bestT = -1;
         double bestDist = double.MaxValue;

         for (int ii = 0; ii < current.Count; ii++)
         {
            int idx = current[ii];
            if (idx == pIdx || idx == qIdx || idx == rIdx) continue;
            double tx = nodesList[idx][0], ty = nodesList[idx][1];
            if (!GeometryUtils.PointInSector(px, py, qx, qy, rx, ry, tx, ty)) continue;
            double qtLen = Math.Sqrt(Math.Pow(tx - qx, 2) + Math.Pow(ty - qy, 2));
            if (qtLen > hQ * 5.0) continue;
            double ptLen = Math.Sqrt(Math.Pow(tx - px, 2) + Math.Pow(ty - py, 2));
            double rtLen = Math.Sqrt(Math.Pow(tx - rx, 2) + Math.Pow(ty - ry, 2));
            if (ptLen < qpLen || rtLen < qrLen)
            {
               double d = ptLen + rtLen;
               if (d < bestDist)
               {
                  bestDist = d;
                  bestT = idx;
               }
            }
         }
         return bestT;
      }

      static int FindNodeInEar(List<int> current, List<double[]> nodesList,
         int pIdx, int qIdx, int rIdx)
      {
         double px = nodesList[pIdx][0], py = nodesList[pIdx][1];
         double qx = nodesList[qIdx][0], qy = nodesList[qIdx][1];
         double rx = nodesList[rIdx][0], ry = nodesList[rIdx][1];

         int bestClear = -1;
         double bestClearDist = double.MaxValue;
         int bestAny = -1;
         double bestAnyDist = double.MaxValue;

         for (int ii = 0; ii < current.Count; ii++)
         {
            int idx = current[ii];
            if (idx == pIdx || idx == qIdx || idx == rIdx) continue;
            double tx = nodesList[idx][0], ty = nodesList[idx][1];
            if (!GeometryUtils.PointInTriangle(px, py, qx, qy, rx, ry, tx, ty)) continue;
            double d = Math.Sqrt(Math.Pow(tx - qx, 2) + Math.Pow(ty - qy, 2));
            if (d < bestAnyDist)
            {
               bestAnyDist = d;
               bestAny = idx;
            }
            if (EdgeClear(current, nodesList, pIdx, idx) && EdgeClear(current, nodesList, idx, rIdx))
            {
               if (d < bestClearDist)
               {
                  bestClearDist = d;
                  bestClear = idx;
               }
            }
         }
         return bestClear >= 0 ? bestClear : bestAny;
      }

      static bool TryBranchA(List<int> current, Stack<List<int>> stack, List<double[]> nodesList,
         int pIdx, int qIdx, int rIdx, int qPos, int tIdx,
         List<(int, int, int)> triangles, List<List<(double X, double Y)>> holePolys)
      {
         if (!EdgeClear(current, nodesList, pIdx, tIdx) || !EdgeClear(current, nodesList, tIdx, rIdx))
            return false;

         // cap-треугольники не должны быть вырожденными и не должны содержать
         // вершины контура на своих рёбрах (защита от T-соединений)
         if (!CapClear(current, nodesList, pIdx, qIdx, tIdx) ||
             !CapClear(current, nodesList, tIdx, qIdx, rIdx))
            return false;

         if (!TriInHole(pIdx, qIdx, tIdx, nodesList, holePolys) &&
             !CentroidCovered(pIdx, qIdx, tIdx, nodesList, triangles))
            triangles.Add((pIdx, qIdx, tIdx));
         if (!TriInHole(tIdx, qIdx, rIdx, nodesList, holePolys) &&
             !CentroidCovered(tIdx, qIdx, rIdx, nodesList, triangles))
            triangles.Add((tIdx, qIdx, rIdx));

         int nBefore = current.Count;
         current.RemoveAt(qPos);

         int pPosBefore = (qPos - 1 + nBefore) % nBefore;
         int pPos = pPosBefore < qPos ? pPosBefore : pPosBefore - 1;
         int tPos = current.IndexOf(tIdx);

         List<int> sub1, sub2;
         if (pPos < tPos)
         {
            sub1 = current.GetRange(pPos, tPos - pPos + 1);
            sub2 = new List<int>(current.Count - sub1.Count + 1);
            sub2.AddRange(current.GetRange(tPos, current.Count - tPos));
            sub2.AddRange(current.GetRange(0, pPos + 1));
         }
         else
         {
            sub1 = current.GetRange(tPos, pPos - tPos + 1);
            sub2 = new List<int>(current.Count - sub1.Count + 1);
            sub2.AddRange(current.GetRange(pPos, current.Count - pPos));
            sub2.AddRange(current.GetRange(0, tPos + 1));
         }

         PushSubcontour(sub1, nodesList, stack, holePolys);
         PushSubcontour(sub2, nodesList, stack, holePolys);
         return true;
      }

      static int BisectorNode(List<double[]> nodesList, List<bool> boundaryFlags, List<double> hValues,
         int pIdx, int qIdx, int rIdx)
      {
         double px = nodesList[pIdx][0], py = nodesList[pIdx][1];
         double qx = nodesList[qIdx][0], qy = nodesList[qIdx][1];
         double rx = nodesList[rIdx][0], ry = nodesList[rIdx][1];
         double hQ = qIdx < hValues.Count ? hValues[qIdx] : 1.0;

         double qpx = px - qx, qpy = py - qy;
         double qrx = rx - qx, qry = ry - qy;
         double qpLen = Math.Sqrt(qpx * qpx + qpy * qpy);
         double qrLen = Math.Sqrt(qrx * qrx + qry * qry);

         double dist = hQ > 1e-10 ? hQ : 1.0;
         double bx = 0, by = 0;

         double thrR = Math.Max(qpLen * 2.0, hQ * 3.0);
         if (qrLen > thrR)
         {
            bx = qrx / qrLen;
            by = qry / qrLen;
         }
         else if (qpLen > Math.Max(qrLen * 2.0, hQ * 3.0))
         {
            bx = qpx / qpLen;
            by = qpy / qpLen;
         }
         else if (qpLen > 1e-10 && qrLen > 1e-10)
         {
            double qpNx = qpx / qpLen, qpNy = qpy / qpLen;
            double qrNx = qrx / qrLen, qrNy = qry / qrLen;
            bx = qpNx + qrNx;
            by = qpNy + qrNy;
         }

         double bLen = Math.Sqrt(bx * bx + by * by);
         if (bLen < 1e-6)
         {
            if (qrLen > 1e-10) { bx = -qry / qrLen; by = qrx / qrLen; }
            else { bx = 0; by = 1; }
         }
         else { bx /= bLen; by /= bLen; }

         double gx = qx + dist * bx;
         double gy = qy + dist * by;
         int gIdx = nodesList.Count;
         nodesList.Add(new double[] { gx, gy });
         boundaryFlags.Add(false);
         hValues.Add(hQ);
         return gIdx;
      }

      static int MidpointNode(List<double[]> nodesList, List<bool> boundaryFlags, List<double> hValues,
         int pIdx, int rIdx, double hQ)
      {
         double gx = (nodesList[pIdx][0] + nodesList[rIdx][0]) / 2.0;
         double gy = (nodesList[pIdx][1] + nodesList[rIdx][1]) / 2.0;
         int gIdx = nodesList.Count;
         nodesList.Add(new double[] { gx, gy });
         boundaryFlags.Add(false);
         hValues.Add(hQ);
         return gIdx;
      }

      static bool IsEar(List<double[]> nodes, List<int> current, int pIdx, int qIdx, int rIdx)
      {
         double px = nodes[pIdx][0], py = nodes[pIdx][1];
         double qx = nodes[qIdx][0], qy = nodes[qIdx][1];
         double rx = nodes[rIdx][0], ry = nodes[rIdx][1];

         double cross = (px - qx) * (ry - qy) - (py - qy) * (rx - qx);
         if (cross > -1e-7) return false;

         for (int ii = 0; ii < current.Count; ii++)
         {
            int idx = current[ii];
            if (idx == pIdx || idx == qIdx || idx == rIdx) continue;
            double tx = nodes[idx][0], ty = nodes[idx][1];
            if (GeometryUtils.PointInTriangle(px, py, qx, qy, rx, ry, tx, ty))
               return false;
         }

         int n = current.Count;
         for (int i2 = 0; i2 < n; i2++)
         {
            int aIdx = current[i2];
            int bIdx = current[(i2 + 1) % n];
            if (aIdx == pIdx || aIdx == rIdx || bIdx == pIdx || bIdx == rIdx) continue;
            if (GeometryUtils.SegmentsIntersect(px, py, rx, ry, nodes[aIdx][0], nodes[aIdx][1],
               nodes[bIdx][0], nodes[bIdx][1]))
               return false;
         }
         return true;
      }

      static bool CentroidCovered(int pIdx, int qIdx, int rIdx, List<double[]> nodesList,
         List<(int, int, int)> triangles)
      {
         double cx = (nodesList[pIdx][0] + nodesList[qIdx][0] + nodesList[rIdx][0]) / 3.0;
         double cy = (nodesList[pIdx][1] + nodesList[qIdx][1] + nodesList[rIdx][1]) / 3.0;
         double px = nodesList[pIdx][0], py = nodesList[pIdx][1];
         double qx = nodesList[qIdx][0], qy = nodesList[qIdx][1];
         double rx = nodesList[rIdx][0], ry = nodesList[rIdx][1];
         for (int t = 0; t < triangles.Count; t++)
         {
            int i = triangles[t].Item1, j = triangles[t].Item2, k = triangles[t].Item3;
            double ecx = (nodesList[i][0] + nodesList[j][0] + nodesList[k][0]) / 3.0;
            double ecy = (nodesList[i][1] + nodesList[j][1] + nodesList[k][1]) / 3.0;
            // новый треугольник покрывает центроид существующего
            if (GeometryUtils.PointInTriangle(px, py, qx, qy, rx, ry, ecx, ecy))
               return true;
            if (GeometryUtils.PointInTriangle(
               nodesList[i][0], nodesList[i][1],
               nodesList[j][0], nodesList[j][1],
               nodesList[k][0], nodesList[k][1], cx, cy))
               return true;
         }
         return false;
      }

      static bool TriInHole(int i, int j, int k, List<double[]> nodesList,
         List<List<(double X, double Y)>> holePolys)
      {
         if (holePolys == null || holePolys.Count == 0) return false;
         double cx = (nodesList[i][0] + nodesList[j][0] + nodesList[k][0]) / 3.0;
         double cy = (nodesList[i][1] + nodesList[j][1] + nodesList[k][1]) / 3.0;
         foreach (var hp in holePolys)
         {
            if (GeometryUtils.PointInPolygon(cx, cy, hp)) return true;
         }
         return false;
      }

      /// <summary>
      /// Проверяет, что cap-треугольник (a,b,c) не вырожден и не содержит
      /// вершин текущего контура на своих рёбрах (T-соединения).
      /// </summary>
      static bool CapClear(List<int> current, List<double[]> nodes, int aIdx, int bIdx, int cIdx)
      {
         double ax = nodes[aIdx][0], ay = nodes[aIdx][1];
         double bx = nodes[bIdx][0], by = nodes[bIdx][1];
         double cx = nodes[cIdx][0], cy = nodes[cIdx][1];

         // вырожденный треугольник
         double area = Math.Abs((bx - ax) * (cy - ay) - (cx - ax) * (by - ay));
         if (area < 1e-10) return false;

         // ни одна вершина контура не должна лежать строго на рёбрах cap
         foreach (int idx in current)
         {
            if (idx == aIdx || idx == bIdx || idx == cIdx) continue;
            double px = nodes[idx][0], py = nodes[idx][1];
            if (PointOnSegmentStrict(ax, ay, bx, by, px, py)) return false;
            if (PointOnSegmentStrict(bx, by, cx, cy, px, py)) return false;
            if (PointOnSegmentStrict(cx, cy, ax, ay, px, py)) return false;
         }
         return true;
      }

      static bool PointOnSegmentStrict(double ax, double ay, double bx, double by, double px, double py)
      {
         double cross = (bx - ax) * (py - ay) - (by - ay) * (px - ax);
         if (Math.Abs(cross) > 1e-8) return false;
         double dot = (px - ax) * (bx - ax) + (py - ay) * (by - ay);
         double len2 = (bx - ax) * (bx - ax) + (by - ay) * (by - ay);
         return dot > 1e-9 && dot < len2 - 1e-9;
      }

      static bool EdgeClear(List<int> current, List<double[]> nodes, int aIdx, int bIdx)
      {
         double ax = nodes[aIdx][0], ay = nodes[aIdx][1];
         double bx = nodes[bIdx][0], by = nodes[bIdx][1];
         int n = current.Count;
         for (int i2 = 0; i2 < n; i2++)
         {
            int cIdx = current[i2];
            int dIdx = current[(i2 + 1) % n];
            if (cIdx == aIdx || cIdx == bIdx || dIdx == aIdx || dIdx == bIdx) continue;
            if (GeometryUtils.SegmentsIntersect(ax, ay, bx, by,
               nodes[cIdx][0], nodes[cIdx][1], nodes[dIdx][0], nodes[dIdx][1]))
               return false;
         }
         return true;
      }

      static double MinContourEdge(List<int> current, List<double[]> nodesList)
      {
         double minLen = double.MaxValue;
         int n = current.Count;
         for (int i2 = 0; i2 < n; i2++)
         {
            double dx = nodesList[current[i2]][0] - nodesList[current[(i2 + 1) % n]][0];
            double dy = nodesList[current[i2]][1] - nodesList[current[(i2 + 1) % n]][1];
            minLen = Math.Min(minLen, Math.Sqrt(dx * dx + dy * dy));
         }
         return minLen;
      }

      static int FindProximitySnap(List<int> current, List<double[]> nodesList, int gPos, double threshold)
      {
         int gIdx = current[gPos];
         double gx = nodesList[gIdx][0], gy = nodesList[gIdx][1];
         int n = current.Count;
         var exclude = new HashSet<int> { gPos, (gPos - 1 + n) % n, (gPos + 1) % n };

         int bestPos = -1;
         double bestDist = threshold;

         // расстояние до узлов
         for (int i2 = 0; i2 < n; i2++)
         {
            if (exclude.Contains(i2)) continue;
            int idx = current[i2];
            double dx = nodesList[idx][0] - gx;
            double dy = nodesList[idx][1] - gy;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < bestDist)
            {
               bestDist = d;
               bestPos = i2;
            }
         }

         // расстояние до рёбер (как в Python)
         for (int i2 = 0; i2 < n; i2++)
         {
            int ip1 = (i2 + 1) % n;
            if (exclude.Contains(i2) || exclude.Contains(ip1)) continue;
            int aIdx = current[i2], bIdx = current[ip1];
            double ax = nodesList[aIdx][0], ay = nodesList[aIdx][1];
            double bx = nodesList[bIdx][0], by = nodesList[bIdx][1];
            double d = PointToSegmentDist(gx, gy, ax, ay, bx, by);
            if (d < bestDist)
            {
               double da = Math.Sqrt((gx - ax) * (gx - ax) + (gy - ay) * (gy - ay));
               double db = Math.Sqrt((gx - bx) * (gx - bx) + (gy - by) * (gy - by));
               int snapPos = da <= db ? i2 : ip1;
               if (!exclude.Contains(snapPos))
               {
                  bestDist = d;
                  bestPos = snapPos;
               }
            }
         }

         return bestPos;
      }

      static double PointToSegmentDist(double px, double py, double ax, double ay, double bx, double by)
      {
         double dx = bx - ax, dy = by - ay;
         double lenSq = dx * dx + dy * dy;
         if (lenSq < 1e-20) return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
         double t = Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / lenSq));
         double projX = ax + t * dx, projY = ay + t * dy;
         return Math.Sqrt((px - projX) * (px - projX) + (py - projY) * (py - projY));
      }

      static void SplitAtNeedle(List<int> current, Stack<List<int>> stack, List<double[]> nodesList,
         int pos1, int pos2, List<List<(double X, double Y)>> holePolys)
      {
         if (pos1 > pos2) { int tmp = pos1; pos1 = pos2; pos2 = tmp; }
         int gIdx = current[pos1];

         var sub1 = current.GetRange(pos1, pos2 - pos1);
         var sub2 = new List<int>((current.Count - pos2) + pos1);
         sub2.AddRange(current.GetRange(pos2, current.Count - pos2));
         sub2.AddRange(current.GetRange(0, pos1));
         if (sub2.Count > 0) sub2[0] = gIdx;

         current.Clear();
         PushSubcontour(sub1, nodesList, stack, holePolys);
         PushSubcontour(sub2, nodesList, stack, holePolys);
      }

      static void PushSubcontour(List<int> sub, List<double[]> nodesList, Stack<List<int>> stack,
         List<List<(double X, double Y)>> holePolys)
      {
         if (sub.Count < 3) return;
         double area2 = 0;
         for (int i2 = 0; i2 < sub.Count; i2++)
         {
            double xi = nodesList[sub[i2]][0], yi = nodesList[sub[i2]][1];
            double xj = nodesList[sub[(i2 + 1) % sub.Count]][0], yj = nodesList[sub[(i2 + 1) % sub.Count]][1];
            area2 += xi * yj - xj * yi;
         }
         if (area2 < 0) sub.Reverse();
         stack.Push(sub);
      }

      static void WeldCoincidentNodes(List<double[]> nodesList, List<bool> boundaryFlags,
         List<(int, int, int)> triangles, double tol = 1e-6)
      {
         int n = nodesList.Count;
         var canonical = new int[n];
         for (int i = 0; i < n; i++) canonical[i] = i;

         for (int i = 0; i < n; i++)
         {
            if (canonical[i] != i) continue;
            for (int j = i + 1; j < n; j++)
            {
               if (canonical[j] != j) continue;
               if (Math.Abs(nodesList[i][0] - nodesList[j][0]) < tol &&
                   Math.Abs(nodesList[i][1] - nodesList[j][1]) < tol)
               {
                  if (boundaryFlags[j] && !boundaryFlags[i])
                     canonical[i] = j;
                  else
                     canonical[j] = i;
               }
            }
         }

         // Разрешаем цепочки до корня
         for (int i = 0; i < n; i++)
         {
            int root = canonical[i];
            while (canonical[root] != root) root = canonical[root];
            canonical[i] = root;
         }

         // Применяем отображение к треугольникам
         for (int t = 0; t < triangles.Count; t++)
         {
            var tri = triangles[t];
            triangles[t] = (canonical[tri.Item1], canonical[tri.Item2], canonical[tri.Item3]);
         }
      }

      static TriangulationResult CompactNodes(List<double[]> nodesList, List<bool> boundaryFlags,
         List<(int, int, int)> triangles)
      {
         if (triangles.Count == 0)
            return new TriangulationResult
            {
               Nodes = [],
               Triangles = [],
               IsBoundary = []
            };

         var referenced = new HashSet<int>();
         foreach (var tri in triangles)
         {
            referenced.Add(tri.Item1);
            referenced.Add(tri.Item2);
            referenced.Add(tri.Item3);
         }

         var sortedRef = new List<int>(referenced);
         sortedRef.Sort();
         var old2new = new Dictionary<int, int>();
         for (int i2 = 0; i2 < sortedRef.Count; i2++)
            old2new[sortedRef[i2]] = i2;

         var nodesCompact = new double[sortedRef.Count][];
         var isBoundaryCompact = new bool[sortedRef.Count];
         for (int i2 = 0; i2 < sortedRef.Count; i2++)
         {
            nodesCompact[i2] = nodesList[sortedRef[i2]];
            isBoundaryCompact[i2] = boundaryFlags[sortedRef[i2]];
         }

         var trianglesCompact = new int[triangles.Count][];
         for (int i2 = 0; i2 < triangles.Count; i2++)
         {
            trianglesCompact[i2] = new int[] {
               old2new[triangles[i2].Item1],
               old2new[triangles[i2].Item2],
               old2new[triangles[i2].Item3]
            };
         }

         return new TriangulationResult
         {
            Nodes = nodesCompact,
            Triangles = trianglesCompact,
            IsBoundary = isBoundaryCompact
         };
      }
   }
}