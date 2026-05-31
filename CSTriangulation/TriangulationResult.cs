namespace CSTriangulation
{
   /// <summary>
   /// Результат триангуляции: узлы, треугольники и признаки граничных узлов.
   /// </summary>
   public class TriangulationResult
   {
      /// <summary>
      /// Координаты узлов: Nodes[i] = [x, y].
      /// </summary>
      public double[][] Nodes { get; set; } = [];

      /// <summary>
      /// Треугольники: Triangles[t] = [i, j, k] — индексы узлов (CCW).
      /// </summary>
      public int[][] Triangles { get; set; } = [];

      /// <summary>
      /// Признаки граничных узлов: IsBoundary[i] = true, если узел на границе.
      /// </summary>
      public bool[] IsBoundary { get; set; } = [];
   }
}