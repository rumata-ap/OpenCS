using System.Collections.Generic;
using System.Linq;

namespace CSTriangulation
{
   /// <summary>
   /// Триангуляция методом продвижения фронта по спецификации triangulation_spec.md.
   /// Поддерживает отверстия, дуговые грани, внутренние ограничения (constrained elements).
   /// </summary>
   public static class AdvancingFront
   {
      /// <summary>Полный конвейер: валидация (§1.1) → дискретизация (§2) → триангуляция (§3) → очистка отверстий (§3.4).</summary>
      public static TriangulationResult Triangulate(AdvancingFrontInput input, double alphaThresholdDeg = 90.0)
      {
         var normalized = ContourValidator.ValidateAndNormalize(input);
         var discretized = ContourDiscretizer.Discretize(normalized);
         return Triangulate(discretized, alphaThresholdDeg);
      }

      /// <summary>Низкоуровневый вход — контур уже дискретизирован (используется тестами и для обратной совместимости).</summary>
      public static TriangulationResult Triangulate(DiscretizedContour contour, double alphaThresholdDeg = 90.0)
      {
         ContourValidator.ValidateDiscretized(contour);
         var triangulator = new FrontTriangulator(contour, alphaThresholdDeg);
         var raw = triangulator.Run();
         return Compact(raw);
      }

      static TriangulationResult Compact(RawTriangulation raw)
      {
         if (raw.Triangles.Count == 0)
            return new TriangulationResult { Nodes = [], Triangles = [], IsBoundary = [] };

         var referenced = new HashSet<int>();
         foreach (var (a, b, c) in raw.Triangles) { referenced.Add(a); referenced.Add(b); referenced.Add(c); }

         var sorted = referenced.OrderBy(x => x).ToList();
         var old2new = new Dictionary<int, int>();
         for (int i = 0; i < sorted.Count; i++) old2new[sorted[i]] = i;

         var newNodes = new double[sorted.Count][];
         var newIsBoundary = new bool[sorted.Count];
         for (int i = 0; i < sorted.Count; i++)
         {
            newNodes[i] = raw.Nodes[sorted[i]];
            newIsBoundary[i] = raw.Kinds[sorted[i]] != ContourNodeKind.Interior;
         }

         var newTris = new int[raw.Triangles.Count][];
         for (int i = 0; i < raw.Triangles.Count; i++)
            newTris[i] = [old2new[raw.Triangles[i].Item1], old2new[raw.Triangles[i].Item2], old2new[raw.Triangles[i].Item3]];

         return new TriangulationResult { Nodes = newNodes, Triangles = newTris, IsBoundary = newIsBoundary };
      }
   }
}
