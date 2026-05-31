using CSmath.Geometry;

namespace CScore
{
   /// <summary>
   /// Точка контура с напряжённо-деформированным состоянием — точка на границе
   /// сечения, для которой вычисляются деформации и напряжения по гипотезе
   /// Бернулли (ε = ε₀ + k_y·y + k_z·x). Наследует <see cref="XY"/>,
   /// добавляя свойства E, E2, Sig, Eps, Eps_p, Nu1, Nu2.
   /// </summary>
   [Serializable]
   public class StressPoint : XY
   {
      double nu1 = 1;
      double nu2 = 1;

      /// <summary>
      /// Секущий модуль упругости в точке контура.
      /// </summary>
      public double E { get; set; }

      /// <summary>
      /// Касательный модуль упругости в точке контура.
      /// </summary>
      public double E2 { get; set; }

      /// <summary>
      /// Напряжение в точке контура [МПа].
      /// </summary>
      public double Sig { get; set; }

      /// <summary>
      /// Полная деформация в точке контура (от внешней нагрузки).
      /// </summary>
      public double Eps { get; set; }

      /// <summary>
      /// Предварительная деформация в точке контура.
      /// </summary>
      public double Eps_p { get; set; }

      /// <summary>
      /// Коэффициент упругости ν₁. По умолчанию 1.
      /// </summary>
      public double Nu1 { get => nu1; set => nu1 = value; }

      /// <summary>
      /// Коэффициент упругости ν₂. По умолчанию 1.
      /// </summary>
      public double Nu2 { get => nu2; set => nu2 = value; }

      /// <summary>
      /// Идентификатор контура, которому принадлежит точка.
      /// </summary>
      public int ContourId { get; set; }

      /// <summary>
      /// Ссылка на контур, которому принадлежит точка.
      /// </summary>
      public Contour? Contour { get; set; }

      /// <summary>
      /// Метка (тег) точки контура (напр. "bot", "top").
      /// </summary>
      public string Tag { get; set; } = "";

      /// <summary>
      /// Трёхмерная точка (X, Y, Eps) — координаты точки и её деформация.
      /// </summary>
      internal Vector3D Point { get => new(X, Y, Eps); }

      /// <summary>
      /// Конструктор по умолчанию. Устанавливает тип точки <see cref="PointType.StressPoint"/>.
      /// </summary>
      public StressPoint() { Type = PointType.StressPoint; }

      /// <summary>
      /// Создаёт точку контура из координатной точки <see cref="XY"/>.
      /// </summary>
      /// <param name="xy">Координатная точка.</param>
      public StressPoint(XY xy)
      {
         X = xy.X; Y = xy.Y;
         Type = PointType.StressPoint;
      }

      /// <summary>
      /// Создаёт точку контура с заданными координатами.
      /// </summary>
      /// <param name="x">Координата X.</param>
      /// <param name="y">Координата Y.</param>
      public StressPoint(double x, double y)
      {
         X = x; Y = y;
         Type = PointType.StressPoint;
      }

      /// <summary>
      /// Создаёт копию точки контура с сохранением координат, номера и тега.
      /// </summary>
      /// <returns>Новый объект StressPoint.</returns>
      public override StressPoint Clone()
      {
         return new() { X = X, Y = Y, Num = Num, Tag = Tag};
      }

      /// <inheritdoc/>
      public override string ToString()
      {
         if (Contour == null)
            return $"StressPoint #{Num:D3} : {Tag} | <No Contour>";
         else return $"StressPoint #{Num:D3} : {Tag} | <{Contour.Tag}>";
      }
   }
}