using System;
using System.Collections.Generic;
using System.Linq;

namespace CSTriangulation
{
   /// <summary>Валидация и нормализация направления обхода контуров (§1.1).</summary>
   internal static class ContourValidator
   {
      const double Eps = 1e-9;

      public static AdvancingFrontInput ValidateAndNormalize(AdvancingFrontInput input)
      {
         if (input.Outer == null)
            throw new TriangulationException("Не задан внешний контур (Outer).");

         var outer = ValidateAndNormalizeLoop(input.Outer, LoopKind.Hull);
         var holes = input.Holes.Select(h => ValidateAndNormalizeLoop(h, LoopKind.Hole)).ToList();

         foreach (var c in input.Constraints)
            ValidateConstraint(c);

         return new AdvancingFrontInput { Outer = outer, Holes = holes, Constraints = input.Constraints };
      }

      static ContourLoop ValidateAndNormalizeLoop(ContourLoop loop, LoopKind expectedKind)
      {
         if (loop.Kind != expectedKind)
            throw new TriangulationException($"Контур должен иметь тип {expectedKind}, но задан как {loop.Kind}.");

         bool isSingleFullCircle = loop.Faces.Count == 1 && loop.Faces[0].Kind == FaceKind.Arc && IsFullTurn(loop.Faces[0]);
         if (loop.Faces.Count < 3 && !isSingleFullCircle)
            throw new TriangulationException("Контур должен содержать не менее 3 граней (или одну дуговую грань на полную окружность).");

         ValidateChainClosed(loop.Faces);

         foreach (var face in loop.Faces)
            ValidateArcFace(loop.Kind, face);

         // Контур из одной дуговой грани на полную окружность вырождается в одну "угловую" точку
         // (A=B) — площадь по угловым точкам граней здесь неприменима. Направление обхода уже
         // задано корректно через знак Phi2-Phi1 в ContourFace.FullCircle, разворот не нужен.
         if (isSingleFullCircle) return loop;

         double area = SignedAreaOfCorners(loop);
         if (Math.Abs(area) < Eps)
            throw new TriangulationException("Контур вырожден: узлы коллинеарны (площадь равна нулю).");

         bool needsReverse = (loop.Kind == LoopKind.Hull && area < 0) || (loop.Kind == LoopKind.Hole && area > 0);
         if (!needsReverse) return loop;

         var reversedFaces = new List<ContourFace>(loop.Faces.Count);
         for (int i = loop.Faces.Count - 1; i >= 0; i--)
            reversedFaces.Add(loop.Faces[i].Reversed());
         return new ContourLoop(loop.Kind, reversedFaces);
      }

      static bool IsFullTurn(ContourFace face) => Math.Abs(Math.Abs(face.Phi2 - face.Phi1) - 2.0 * Math.PI) < 1e-7;

      static void ValidateArcFace(LoopKind loopKind, ContourFace face)
      {
         if (face.Kind != FaceKind.Arc) return;

         double dphi = ContourFace.SignedDeltaPhi(loopKind, face.Phi1, face.Phi2);
         if (Math.Abs(dphi) < Eps)
            throw new TriangulationException("Дуговая грань вырождена: угол поворота близок к нулю.");

         if (face.Mid != null)
         {
            double dMid = Math.Sqrt(Math.Pow(face.Mid.X - face.CenterX, 2) + Math.Pow(face.Mid.Y - face.CenterY, 2));
            if (Math.Abs(dMid - face.Radius) > face.Radius * 0.01 + 1e-6)
               throw new TriangulationException("Промежуточная точка дуговой грани не лежит на окружности, определяемой тремя точками.");
         }
      }

      static void ValidateChainClosed(List<ContourFace> faces)
      {
         int n = faces.Count;
         for (int i = 0; i < n; i++)
         {
            var cur = faces[i];
            var next = faces[(i + 1) % n];
            double dx = cur.B.X - next.A.X, dy = cur.B.Y - next.A.Y;
            if (Math.Sqrt(dx * dx + dy * dy) > 1e-6)
               throw new TriangulationException("Грани контура не образуют замкнутую цепочку (конец одной грани не совпадает с началом следующей).");
         }
      }

      static double SignedAreaOfCorners(ContourLoop loop)
      {
         double s = 0;
         int n = loop.Faces.Count;
         for (int i = 0; i < n; i++)
         {
            var a = loop.Faces[i].A;
            var b = loop.Faces[(i + 1) % n].A;
            s += a.X * b.Y - b.X * a.Y;
         }
         return 0.5 * s;
      }

      static void ValidateConstraint(ConstrainedElement c)
      {
         if (c.Kind == ConstraintKind.Point)
         {
            if (c.Point == null)
               throw new TriangulationException("Constrained point должен задавать координаты узла.");
            return;
         }

         if (c.Faces == null || c.Faces.Count == 0)
            throw new TriangulationException($"Constrained {c.Kind} должен содержать хотя бы одну грань.");

         for (int i = 0; i < c.Faces.Count - 1; i++)
         {
            var cur = c.Faces[i];
            var next = c.Faces[i + 1];
            double dx = cur.B.X - next.A.X, dy = cur.B.Y - next.A.Y;
            if (Math.Sqrt(dx * dx + dy * dy) > 1e-6)
               throw new TriangulationException("Грани constrained-полилинии не образуют непрерывную цепочку.");
         }
      }

      public static void ValidateDiscretized(DiscretizedContour c)
      {
         if (c.OuterIndices.Count < 3)
            throw new TriangulationException("Внешний контур после дискретизации содержит менее 3 узлов.");
         foreach (var hole in c.HoleIndices)
            if (hole.Count < 3)
               throw new TriangulationException("Контур отверстия после дискретизации содержит менее 3 узлов.");

         ValidateNoAdjacentDuplicates(c.Nodes, c.OuterIndices);
         foreach (var hole in c.HoleIndices)
            ValidateNoAdjacentDuplicates(c.Nodes, hole);

         ValidateNoSelfIntersections(c.Nodes, c.OuterIndices);
         foreach (var hole in c.HoleIndices)
            ValidateNoSelfIntersections(c.Nodes, hole);
      }

      static void ValidateNoAdjacentDuplicates(double[][] nodes, List<int> loop)
      {
         int n = loop.Count;
         for (int i = 0; i < n; i++)
         {
            int a = loop[i], b = loop[(i + 1) % n];
            double dx = nodes[a][0] - nodes[b][0], dy = nodes[a][1] - nodes[b][1];
            if (Math.Sqrt(dx * dx + dy * dy) < 1e-9)
               throw new TriangulationException("Соседние узлы контура совпадают (нулевая длина ребра после дискретизации).");
         }
      }

      static void ValidateNoSelfIntersections(double[][] nodes, List<int> loop)
      {
         int n = loop.Count;
         for (int i = 0; i < n; i++)
         {
            int a = loop[i], b = loop[(i + 1) % n];
            for (int j = i + 1; j < n; j++)
            {
               int c = loop[j], d = loop[(j + 1) % n];
               if (a == c || a == d || b == c || b == d) continue;
               if (SegmentGeometry.SegmentsIntersect(
                     nodes[a][0], nodes[a][1], nodes[b][0], nodes[b][1],
                     nodes[c][0], nodes[c][1], nodes[d][0], nodes[d][1]))
                  throw new TriangulationException("Контур самопересекается после дискретизации.");
            }
         }
      }
   }
}
