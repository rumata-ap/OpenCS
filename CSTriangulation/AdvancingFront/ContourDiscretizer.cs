using System;
using System.Collections.Generic;

namespace CSTriangulation
{
   /// <summary>Дискретизация контура и constrained-элементов (§2).</summary>
   internal static class ContourDiscretizer
   {
      const double Eps = 1e-9;

      public static DiscretizedContour Discretize(AdvancingFrontInput input)
      {
         var nodes = new List<double[]>();
         var kinds = new List<ContourNodeKind>();
         var hvals = new List<double>();

         var outerIdx = DiscretizeLoop(input.Outer, nodes, kinds, hvals, ContourNodeKind.Hull);

         var holeIdxLists = new List<List<int>>();
         foreach (var hole in input.Holes)
            holeIdxLists.Add(DiscretizeLoop(hole, nodes, kinds, hvals, ContourNodeKind.Hole));

         var constrainedPoints = new List<int>();
         var constrainedSegments = new List<(int, int)>();
         foreach (var c in input.Constraints)
            DiscretizeConstraint(c, nodes, kinds, hvals, constrainedPoints, constrainedSegments);

         return new DiscretizedContour
         {
            Nodes = nodes.ToArray(),
            NodeKinds = kinds.ToArray(),
            HValues = hvals.ToArray(),
            OuterIndices = outerIdx,
            HoleIndices = holeIdxLists,
            ConstrainedPointIndices = constrainedPoints,
            ConstrainedSegments = constrainedSegments
         };
      }

      static List<int> DiscretizeLoop(ContourLoop loop, List<double[]> nodes,
         List<ContourNodeKind> kinds, List<double> hvals, ContourNodeKind kind)
      {
         var idx = new List<int>();
         foreach (var face in loop.Faces)
         {
            foreach (var (x, y, h) in DiscretizeFace(face, loop.Kind))
            {
               idx.Add(nodes.Count);
               nodes.Add(new[] { x, y });
               kinds.Add(kind);
               hvals.Add(h);
            }
         }
         return idx;
      }

      /// <summary>Начальный узел грани + промежуточные узлы, БЕЗ конечного узла (§2.3, п.1-2).</summary>
      static List<(double X, double Y, double H)> DiscretizeFace(ContourFace face, LoopKind loopKindForArcs)
      {
         var result = new List<(double, double, double)>();

         if (face.Kind == FaceKind.Linear)
         {
            double xA = face.A.X, yA = face.A.Y, xB = face.B.X, yB = face.B.Y;
            double len = Math.Sqrt((xB - xA) * (xB - xA) + (yB - yA) * (yB - yA));
            double hAvg = Math.Max((face.A.H + face.B.H) / 2.0, Eps);
            int n = Math.Max(1, (int)Math.Round(len / hAvg));

            result.Add((xA, yA, hAvg));
            for (int s = 1; s < n; s++)
            {
               double t = (double)s / n;
               result.Add((xA + t * (xB - xA), yA + t * (yB - yA), hAvg));
            }
         }
         else
         {
            double deltaPhi = ContourFace.SignedDeltaPhi(loopKindForArcs, face.Phi1, face.Phi2);
            double len = face.Radius * Math.Abs(deltaPhi);
            double hAvg = Math.Max((face.A.H + face.B.H) / 2.0, Eps);
            int n = Math.Max(1, (int)Math.Round(len / hAvg));

            result.Add((face.A.X, face.A.Y, hAvg));
            for (int s = 1; s < n; s++)
            {
               double t = (double)s / n;
               double phi = face.Phi1 + t * deltaPhi;
               result.Add((face.CenterX + face.Radius * Math.Cos(phi), face.CenterY + face.Radius * Math.Sin(phi), hAvg));
            }
         }
         return result;
      }

      /// <summary>
      /// Дискретизация constrained-элемента (§2.4). В отличие от замкнутого контура,
      /// цепочка разомкнута — конечный узел последней грани добавляется явно.
      /// Направление дуги для constrained-элементов не привязано к обходу hull/hole —
      /// используется условное направление LoopKind.Hull (см. design doc §8).
      /// </summary>
      static void DiscretizeConstraint(ConstrainedElement c, List<double[]> nodes,
         List<ContourNodeKind> kinds, List<double> hvals,
         List<int> constrainedPoints, List<(int, int)> constrainedSegments)
      {
         if (c.Kind == ConstraintKind.Point)
         {
            int idx = nodes.Count;
            nodes.Add(new[] { c.Point!.X, c.Point.Y });
            kinds.Add(ContourNodeKind.Constrained);
            hvals.Add(c.Point.H);
            constrainedPoints.Add(idx);
            return;
         }

         var chainIdx = new List<int>();
         var faces = c.Faces!;
         for (int i = 0; i < faces.Count; i++)
         {
            foreach (var (x, y, h) in DiscretizeFace(faces[i], LoopKind.Hull))
            {
               chainIdx.Add(nodes.Count);
               nodes.Add(new[] { x, y });
               kinds.Add(ContourNodeKind.Constrained);
               hvals.Add(h);
            }
         }
         var lastFace = faces[^1];
         chainIdx.Add(nodes.Count);
         nodes.Add(new[] { lastFace.B.X, lastFace.B.Y });
         kinds.Add(ContourNodeKind.Constrained);
         hvals.Add(lastFace.B.H);

         for (int i = 0; i < chainIdx.Count - 1; i++)
            constrainedSegments.Add((chainIdx[i], chainIdx[i + 1]));

         // Узлы segment/polyline/arc-ограничения тоже должны быть кандидатами в t (Тип 3, §3.2) —
         // иначе они остаются "плавающими" узлами без механизма попасть во фронт вершиной треугольника,
         // и ребро ограничения никогда не появится в сетке (только защищено от разрезания, но не построено).
         constrainedPoints.AddRange(chainIdx);
      }
   }
}
