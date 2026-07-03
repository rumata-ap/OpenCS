namespace CScore
{
   /// <summary>
   /// Круг — геометрическая фигура с координатами центра, радиусом и площадью.
   /// Наследует <see cref="XY"/>, добавляя диаметр, радиус и площадь.
   /// Используется для моделирования арматурных стержней круглого сечения.
   /// </summary>
   [Serializable]
   public class CircleP : XY
   {
      string str = null!;

      /// <summary>
      /// Наименование (тег) круга.
      /// </summary>
      public string Tag { get; set; } = "";

      /// <summary>
      /// Имя набора геометрии, которому принадлежит круг.
      /// </summary>
      public string? GeometrySet { get; set; }

      /// <summary>
      /// Диаметр круга [м].
      /// </summary>
      public double Diameter { get; set; }

      /// <summary>
      /// Диаметр в мм (Diameter × 1000), отформатированный с 3 знаками.
      /// </summary>
      public string Dstr { get => $"{Diameter:F3}"; set => str = value; }

      /// <summary>
      /// Радиус круга [м].
      /// </summary>
      public double Radius { get; set; }

      /// <summary>
      /// Площадь круга [м²] (π · r²).
      /// </summary>
      public double Area { get; set; }

      /// <summary>
      /// Конструктор по умолчанию.
      /// </summary>
      public CircleP() { }

      /// <summary>
      /// Создаёт круг с заданными координатами центра и радиусом.
      /// Диаметр и площадь вычисляются автоматически.
      /// </summary>
      /// <param name="x">Координата X центра [м].</param>
      /// <param name="y">Координата Y центра [м].</param>
      /// <param name="r">Радиус [м].</param>
      public CircleP(double x, double y, double r)
      {
         X = x;
         Y = y;
         Radius = r;
         Diameter = r * 2;
         Area = Math.PI * r * r;
      }

      /// <summary>
      /// Создаёт глубокую копию круга.
      /// </summary>
      /// <returns>Новый объект CircleP с теми же координатами и радиусом.</returns>
      public override CircleP Clone()
      {
         return new CircleP(X, Y, Radius);
      }

      /// <inheritdoc/>
      public override string ToString()
      {
         if (GeometrySet == null)
            return $"{Num:D3}#circle : {Tag} | <No GeometrySet>";
         else return $"{Num:D3}#circle : {Tag} | <{GeometrySet}>";
      }
   }
}