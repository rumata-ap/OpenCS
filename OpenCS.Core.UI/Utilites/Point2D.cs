namespace OpenCS.Utilites
{
   /// <summary>Точка в 2D-координатах модели (мм и т.п.). GUI-агностический аналог
   /// System.Windows.Point; канвасы GUI-проектов конвертируют её в свои экранные типы.</summary>
   public readonly record struct Point2D(double X, double Y)
   {
      public static Point2D operator +(Point2D a, Point2D b) => new(a.X + b.X, a.Y + b.Y);
      public static Point2D operator -(Point2D a, Point2D b) => new(a.X - b.X, a.Y - b.Y);
   }
}
