namespace CSTriangulation
{
   /// <summary>
   /// Узел контура: координаты и локальный шаг сетки h (§1.4 спецификации).
   /// </summary>
   public sealed class ContourNode
   {
      public double X { get; }
      public double Y { get; }
      public double H { get; }

      public ContourNode(double x, double y, double h)
      {
         X = x;
         Y = y;
         H = h;
      }
   }
}
