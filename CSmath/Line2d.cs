
namespace CSmath.Geometry
{
   /// <summary>
   /// Отрезок прямой на плоскости, заданный начальной и конечной точками.
   /// Уравнение прямой: Ax + By + C = 0. Также хранит параметры наклонной формы y = kx + b
   /// и направляющие косинусы нормали.
   /// </summary>
   [Serializable]
   public class Line2d
   {
      protected Vector3D startPoint = null!;
      protected Vector3D endPoint = null!;
      protected Vector2D directive = null!;
      protected Vector2D normal = null!;

      /// <summary>
      /// Начальная точка отрезка. При изменении пересчитываются параметры прямой.
      /// </summary>
      public Vector3D StartPoint { get => startPoint; set { startPoint = value; if (EndPoint != null) { directive = endPoint.ToVector2d() - startPoint.ToVector2d(); CalcLine(); }; } }

      /// <summary>
      /// Конечная точка отрезка. При изменении пересчитываются параметры прямой.
      /// </summary>
      public Vector3D EndPoint { get => endPoint; set { endPoint = value; directive = endPoint.ToVector2d() - startPoint.ToVector2d(); CalcLine(); } }

      /// <summary>
      /// Центральная точка отрезка — середина между StartPoint и EndPoint.
      /// </summary>
      public Vector3D CenterPoint { get; private set; } = null!;

      /// <summary>
      /// Направляющий вектор отрезка (End - Start).
      /// </summary>
      public Vector2D Directive { get => directive; private set => directive = value; }

      /// <summary>
      /// Вектор нормали к прямой: (A, B).
      /// </summary>
      public Vector2D Normal { get => normal; private set => normal = value; }

      /// <summary>
      /// Коэффициент A уравнения прямой Ax + By + C = 0.
      /// </summary>
      public double A { get; private set; }

      /// <summary>
      /// Коэффициент B уравнения прямой Ax + By + C = 0.
      /// </summary>
      public double B { get; private set; }

      /// <summary>
      /// Свободный член C уравнения прямой Ax + By + C = 0.
      /// </summary>
      public double C { get; private set; }

      /// <summary>
      /// Угловой коэффициент прямой (k из уравнения y = kx + b).
      /// Равен <see cref="double.PositiveInfinity"/> для вертикальных прямых.
      /// </summary>
      public double k { get; private set; }

      /// <summary>
      /// Свободный член уравнения y = kx + b (смещение по оси Y).
      /// Равен <see cref="double.PositiveInfinity"/> для вертикальных прямых.
      /// </summary>
      public double b { get; private set; }

      /// <summary>
      /// Длина отрезка — модуль направляющего вектора.
      /// </summary>
      public double Length { get => Directive.Length; }

      /// <summary>
      /// Косинус угла между нормалью к прямой и осью X.
      /// </summary>
      public double cosAlfa { get; private set; }

      /// <summary>
      /// Косинус угла между нормалью к прямой и осью Y.
      /// </summary>
      public double cosBeta { get; private set; }

      /// <summary>
      /// Расстояние от начала координат до прямой (нормальное уравнение).
      /// </summary>
      public double p { get; private set; }

      /// <summary>
      /// Создаёт пустой экземпляр отрезка без инициализации точек.
      /// </summary>
      public Line2d()
      {

      }

      /// <summary>
      /// Создаёт отрезок по двум 2D-точкам.
      /// </summary>
      /// <param name="startPt">Начальная точка отрезка.</param>
      /// <param name="endPt">Конечная точка отрезка.</param>
      public Line2d(Vector2D startPt, Vector2D endPt)
      {
         startPoint = startPt.ToVector3d(); endPoint = endPt.ToVector3d();
         directive = startPt - endPt;
         CalcLine();
      }

      /// <summary>
      /// Создаёт отрезок по двум 3D-точкам (используются только X и Y координаты).
      /// </summary>
      /// <param name="startPt">Начальная точка отрезка.</param>
      /// <param name="endPt">Конечная точка отрезка.</param>
      public Line2d(Vector3D startPt, Vector3D endPt)
      {
         startPoint = startPt;
         endPoint = endPt;
         directive = endPoint.ToVector2d() - startPoint.ToVector2d();
         CalcLine();
      }

      protected void CalcLine()
      {
         A = Directive.Y;
         B = -Directive.X;
         normal = new Vector2D(A, B);
         C = Directive.X * StartPoint.Y - Directive.Y * StartPoint.X;
         if (Directive.X != 0) { k = Directive.Y / Directive.X; }
         else { k = Double.PositiveInfinity; }
         if (Directive.X != 0) { b = -Directive.Y / Directive.X * StartPoint.X + StartPoint.Y; }
         else { b = Double.PositiveInfinity; }
         double normC = 1 / Math.Sqrt(A * A + B * B);
         if (C < 0) normC *= -1;
         cosAlfa = A * normC;
         cosBeta = B * normC;
         p = C * normC;
         double dx = 0.5 * (EndPoint.X - StartPoint.X);
         double dy = 0.5 * (EndPoint.Y - StartPoint.Y);
         CenterPoint = new Vector3D(StartPoint.X + dx, StartPoint.Y + dy, 0);
      }

      /// <summary>
      /// Возвращает единичный вектор нормали к прямой.
      /// </summary>
      /// <returns>Единичный вектор нормали (cosAlfa, cosBeta).</returns>
      public Vector2D GetUnitNormal()
      {
         return new Vector2D(cosAlfa, cosBeta);
      }

      /// <summary>
      /// Вычисляет расстояние со знаком от заданной 2D-точки до прямой.
      /// Положительное значение — точка по сторону нормали, отрицательное — противоположная.
      /// </summary>
      /// <param name="point">Точка, расстояние до которой вычисляется.</param>
      /// <returns>Расстояние со знаком от точки до прямой.</returns>
      public double LengthTo(Vector2D point)
      {
         double normC = 1 / Math.Sqrt(A * A + B * B);
         if (C < 0) normC *= -1;
         cosAlfa = A * normC;
         cosBeta = B * normC;
         p = C * normC;
         return normC * (A * point.X + B * point.Y + C);
      }

      /// <summary>
      /// Вычисляет расстояние со знаком от заданной 3D-точки до прямой (используются X и Y).
      /// Положительное значение — точка по сторону нормали, отрицательное — противоположная.
      /// </summary>
      /// <param name="point">Точка, расстояние до которой вычисляется.</param>
      /// <returns>Расстояние со знаком от точки до прямой.</returns>
      public double LengthTo(Vector3D point)
      {
         double normC = 1 / Math.Sqrt(A * A + B * B);
         if (C < 0) normC *= -1;
         cosAlfa = A * normC;
         cosBeta = B * normC;
         p = C * normC;
         return normC * (A * point.X + B * point.Y + C);
      }

      /// <summary>
      /// Вычисляет координату Y по заданной координате X на прямой (y = kx + b).
      /// </summary>
      /// <param name="x">Координата X.</param>
      /// <returns>Координата Y на прямой, или <see cref="double.PositiveInfinity"/> для вертикальной прямой.</returns>
      public double Interpolation(double x)
      {
         if (B == 0) return double.PositiveInfinity;
         else return (-A * x - C) / B;
      }

      /// <summary>
      /// Находит точку пересечения данной прямой с другой прямой (как бесконечные линии).
      /// Если прямые параллельны или совпадают, результат содержит res = false.
      /// </summary>
      /// <param name="line">Вторая прямая для нахождения пересечения.</param>
      /// <param name="res">Результат пересечения: содержит точку пересечения и признак успешного нахождения.</param>
      public void Intersection(Line2d line, out IntersectResult res)
      {
         res = new IntersectResult();

         double A1 = this.A;
         double B1 = this.B;
         double C1 = this.C;
         double A2 = line.A;
         double B2 = line.B;
         double C2 = line.C;

         if (A1 == 0 || B2 - A2 * B1 / A1 == 0) return;

         double y = (A2 * C1 / A1 - C2) / (B2 - A2 * B1 / A1);
         double x = (-C1 - B1 * y) / A1;

         res.pts = new List<Vector2D> { new Vector2D(x, y) };
         res.res = true;
      }

      /// <summary>
      /// Находит точку пересечения двух отрезков. Точка считается пересечением,
      /// если она лежит в пределах обоих отрезков (по осям X и Y).
      /// </summary>
      /// <param name="line">Второй отрезок для нахождения пересечения.</param>
      /// <param name="res">Результат пересечения: содержит точку пересечения и признак успешного нахождения.</param>
      public void IntersectionSegments(Line2d line, out IntersectResult res)
      {
         res = new IntersectResult();
         IntersectResult resl;
         Intersection(line, out resl);
         if (resl.res == false) return;
         Vector2D respt = resl.pts[0];
         Range Xrange = new Range(StartPoint.X, EndPoint.X);
         Range Yrange = new Range(StartPoint.Y, EndPoint.Y);
         Range Xrangel = new Range(line.StartPoint.X, line.EndPoint.X);
         Range Yrangel = new Range(line.StartPoint.Y, line.EndPoint.Y);
         if (Xrange.Contains(respt.X) && Yrange.Contains(respt.Y) && Xrangel.Contains(respt.X) && Yrangel.Contains(respt.Y))
         {
            res.res = true;
            res.pts = new List<Vector2D> { respt };
         }
      }
   }

   /// <summary>
   /// Результат пересечения двух прямых или отрезков.
   /// </summary>
   [Serializable]
   public struct IntersectResult
   {
      /// <summary>
      /// Список точек пересечения.
      /// </summary>
      public List<Vector2D> pts;

      /// <summary>
      /// Признак того, что пересечение найдено. True, если прямые/отрезки пересекаются.
      /// </summary>
      public bool res;
   }
}
