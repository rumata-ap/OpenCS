using NetTopologySuite.Geometries;

using Newtonsoft.Json;

using System.Text.RegularExpressions;

namespace CScore
{
   /// <summary>
   /// Тип точки: координата, точка контура с напряжениями, волокно, арматурный стержень или слой арматуры.
   /// </summary>
   public enum PointType { Coordinate = 1, StressPoint = 2, Fiber = 3, Rebar = 4, RebarLayer = 5 }

   /// <summary>
   /// Двумерная точка — базовый класс для всех объектов с координатами X и Y.
   /// Является предком <see cref="StressPoint"/>, <see cref="CircleP"/> и др.
   /// Поддерживает арифметические операции сложения, вычитания и умножения на скаляр.
   /// </summary>
   [Serializable]
   public class XY
   {
      string str;

      /// <summary>
      /// Координата X [м].
      /// </summary>
      public double X { get; set; }

      /// <summary>
      /// Координата Y [м].
      /// </summary>
      public double Y { get; set; }

      /// <summary>
      /// Тип точки (по умолчанию <see cref="PointType.Coordinate"/>).
      /// </summary>
      public PointType Type { get; set; } = PointType.Coordinate;

      /// <summary>
      /// Координата (X, Y) как объект NetTopologySuite.
      /// </summary>
      internal Coordinate Coordinate { get => new(X, Y); }

      /// <summary>
      /// Первичный ключ для EF Core.
      /// </summary>
      [JsonIgnore] public int Id { get; set; }

      /// <summary>
      /// Порядковый номер точки.
      /// </summary>
      public int Num { get; set; }

      /// <summary>
      /// Текстовое описание точки (возвращает ToString).
      /// </summary>
      public string Description { get => ToString(); set => str = value; }

      /// <inheritdoc/>
      public override string ToString()
      {
         return $"{Num:D3}#point | X:{X:F4}м , Y:{Y:F4}м";
      }

      /// <summary>
      /// Создаёт точку с заданными координатами.
      /// </summary>
      /// <param name="x">Координата X (по умолчанию 0).</param>
      /// <param name="y">Координата Y (по умолчанию 0).</param>
      public XY(double x = 0, double y = 0)
      {
         X = x;
         Y = y;
      }

      /// <summary>
      /// Создаёт копию точки с теми же координатами.
      /// </summary>
      /// <returns>Новый объект XY с координатами (X, Y).</returns>
      public virtual XY Clone()
      {
         return new XY(X, Y);
      }

      /// <summary>
      /// Сравнивает данную точку с другой на равенство с заданной погрешностью.
      /// </summary>
      /// <param name="other">Другая точка для сравнения.</param>
      /// <param name="error">Допустимая погрешность (по умолчанию 1e-8).</param>
      /// <returns>true, если координаты совпадают в пределах погрешности.</returns>
      public bool Equals(XY other, double error = 1e-8)
      {
         return Math.Abs(X-other.X)<=error && Math.Abs(Y - other.Y) <= error;
      }

      /// <summary>
      /// Сдвигает точку на вектор (xy.X, xy.Y).
      /// </summary>
      public static XY operator +(XY xy1, XY xy2)
      {
         xy1.X += xy2.X;
         xy1.Y += xy2.Y;
         return xy1;
      }

      /// <summary>
      /// Сдвигает точку на вектор, обратный (xy.X, xy.Y).
      /// </summary>
      public static XY operator -(XY xy1, XY xy2)
      {
         xy1.X -= xy2.X;
         xy1.Y -= xy2.Y;
         return xy1;
      }

      /// <summary>
      /// Сдвигает точку на скаляр (прибавляет значение к обеим координатам).
      /// </summary>
      public static XY operator +(XY xy1, double xy2)
      {
         xy1.X += xy2;
         xy1.Y += xy2;
         return xy1;
      }

      /// <summary>
      /// Сдвигает точку на скаляр (вычитает значение из обеих координат).
      /// </summary>
      public static XY operator -(XY xy1, double xy2)
      {
         xy1.X -= xy2;
         xy1.Y -= xy2;
         return xy1;
      }

      /// <summary>
      /// Масштабирует точку на заданный коэффициент.
      /// </summary>
      public static XY operator *(XY xy1, double xy2)
      {
         xy1.X *= xy2;
         xy1.Y *= xy2;
         return xy1;
      }
   }
}