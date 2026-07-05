using System.Collections.Generic;

namespace CSTriangulation
{
   /// <summary>Тип узла после дискретизации контура (§1.5).</summary>
   public enum ContourNodeKind { Hull, Hole, Constrained, Interior }

   /// <summary>
   /// Дискретизированный контур: набор узлов с типами, параметрами h,
   /// внешним контуром, отверстиями и constrained-элементами.
   /// Выход дискретизатора (§2), вход основного цикла триангуляции (§3).
   /// </summary>
   public class DiscretizedContour
   {
      /// <summary>Координаты узлов: Nodes[i] = [x, y].</summary>
      public double[][] Nodes { get; set; } = System.Array.Empty<double[]>();

      /// <summary>Тип каждого узла (§1.5).</summary>
      public ContourNodeKind[] NodeKinds { get; set; } = System.Array.Empty<ContourNodeKind>();

      /// <summary>Параметр h для каждого узла.</summary>
      public double[] HValues { get; set; } = System.Array.Empty<double>();

      /// <summary>Индексы узлов внешнего контура (CCW).</summary>
      public List<int> OuterIndices { get; set; } = new();

      /// <summary>Списки индексов для каждого отверстия (CW).</summary>
      public List<List<int>> HoleIndices { get; set; } = new();

      /// <summary>Индексы constrained points (§1.6).</summary>
      public List<int> ConstrainedPointIndices { get; set; } = new();

      /// <summary>Пары индексов constrained-отрезков (§1.6).</summary>
      public List<(int A, int B)> ConstrainedSegments { get; set; } = new();
   }
}
