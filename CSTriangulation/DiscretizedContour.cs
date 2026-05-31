namespace CSTriangulation
{
   /// <summary>
   /// Дискретизированный контур: набор узлов с границей, параметрами h,
   /// внешним контуром и отверстиями. Входные данные для метода продвижения фронта.
   /// </summary>
   public class DiscretizedContour
   {
      /// <summary>
      /// Координаты узлов: Nodes[i] = [x, y].
      /// </summary>
      public double[][] Nodes { get; set; } = [];

      /// <summary>
      /// Признаки граничных узлов: IsBoundary[i] = true, если узел на границе.
      /// </summary>
      public bool[] IsBoundary { get; set; } = [];

      /// <summary>
      /// Параметр h (размер) для каждого узла. Используется для управления плотностью сетки.
      /// </summary>
      public double[] HValues { get; set; } = [];

      /// <summary>
      /// Индексы узлов внешнего контура (CCW).
      /// </summary>
      public List<int> OuterIndices { get; set; } = [];

      /// <summary>
      /// Списки индексов для каждого отверстия (CW).
      /// </summary>
      public List<List<int>> HoleIndices { get; set; } = [];
   }
}