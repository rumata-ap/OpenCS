using CSmath.Geometry;

using System.Text.Json.Serialization;

namespace CScore
{
   /// <summary>
   /// Тип конечного элемента (волокна) сечения.
   /// </summary>
   public enum FiberType { tri = 2, poly = 1, none = 0 }

   /// <summary>
   /// Конечный элемент (волокно) — дискретная площадка поперечного сечения,
   /// получаемая в результате разбиения (триангуляции или нарезки) области сечения.
   /// Каждое волокно характеризуется координатами центра тяжести, площадью,
   /// напряжениями и деформациями, вычисляемыми по диаграмме работы материала.
   /// </summary>
   [Serializable]
   public class Fiber
   {
      string str;
      double nu1 = 1;
      double nu2 = 1;

      /// <summary>
      /// Секущий модуль упругости (средний модуль на данном шаге нагружения).
      /// </summary>
      public double E { get; set; }

      /// <summary>
      /// Касательный модуль упругости (производная диаграммы σ-ε в текущей точке).
      /// </summary>
      public double E2 { get; set; }

      /// <summary>
      /// Напряжение в волокне [МПа].
      /// </summary>
      public double Sig { get; set; }

      /// <summary>
      /// Полная деформация волокна (от внешней нагрузки).
      /// </summary>
      public double Eps { get; set; }

      /// <summary>
      /// Предварительная деформация волокна (от предварительного напряжения и др.).
      /// </summary>
      public double Eps_p { get; set; }

      /// <summary>
      /// Коэффициент упругости ν₁ (коэффициент приведения для первой группы предельных состояний).
      /// По умолчанию равен 1.
      /// </summary>
      public double Nu1 { get => nu1; set => nu1 = value; }

      /// <summary>
      /// Коэффициент упругости ν₂.
      /// По умолчанию равен 1.
      /// </summary>
      public double Nu2 { get => nu2; set => nu2 = value; }

      /// <summary>
      /// Метка (тег) волокна — обычно совпадает с тегом родительской области.
      /// </summary>
      public string Tag { get; set; } = "";

      /// <summary>
      /// Трёхмерная точка (X, Y, Eps) — координаты центра волокна и его деформация.
      /// Не сохраняется в БД.
      /// </summary>
      internal Vector3D Point { get => new(X, Y, Eps); }

      /// <summary>
      /// Продольное усилие в волокне: N = σ · A.
      /// </summary>
      public double N { get; set; }

      /// <summary>
      /// Площадь волокна [м²].
      /// </summary>
      public double Area { get; set; }

      /// <summary>
      /// Изгибающий момент относительно оси X (M_y = σ · A · y).
      /// </summary>
      public double My { get; set; }

      /// <summary>
      /// Изгибающий момент относительно оси Y (M_z = σ · A · x).
      /// </summary>
      public double Mz { get; set; }

      /// <summary>
      /// WKT-представление (Well-Known Text) полигона волокна.
      /// Используется для сериализации геометрии волокна.
      /// </summary>
      public string? WKT { get; set; }

      /// <summary>
      /// Тип волокна: треугольное (tri), многоугольное (poly) или не задано (none).
      /// </summary>
      public FiberType TypeFiber { get; set; } = FiberType.none;

      /// <summary>
      /// Координата X центра тяжести волокна [м].
      /// </summary>
      public double X { get; set; }

      /// <summary>
      /// Координата Y центра тяжести волокна [м].
      /// </summary>
      public double Y { get; set; }

      /// <summary>
      /// Первичный ключ для EF Core.
      /// </summary>
      [JsonIgnore] public int Id { get; set; }

      /// <summary>
      /// Порядковый номер волокна в коллекции.
      /// </summary>
      public int Num { get; set; }

      /// <summary>
      /// Текстовое описание волокна (возвращает ToString).
      /// </summary>
      public string Description { get => ToString(); set => str = value; }

      /// <summary>
      /// Площадь волокна в см² (Area × 10000), отформатированная с 2 знаками после запятой.
      /// </summary>
      public string Astr { get => $"{10000*Area:F2}"; set => str = value; }

      /// <summary>
      /// Ссылка на родительскую область FiberRegion. Не сериализуется.
      /// </summary>
      [JsonIgnore] public FiberRegion? Region { get; set; }

      /// <summary>
      /// Внешний ключ для связи с FiberRegion. Не сериализуется.
      /// </summary>
      [JsonIgnore] public int RegionId { get; set; }

      /// <inheritdoc/>
      public override string ToString()
      {
         if (Region == null)
            return $"{Num:D3}#fiber : {Tag} | <No Region>";
         else return $"{Num:D3}#fiber : {Tag} | <{Region}>";
      }

      /// <summary>
      /// Конструктор по умолчанию.
      /// </summary>
      public Fiber()
      {
      }

      /// <summary>
      /// Создаёт волокно с заданными координатами и коэффициентами упругости.
      /// </summary>
      /// <param name="x">Координата X центра тяжести [м].</param>
      /// <param name="y">Координата Y центра тяжести [м].</param>
      /// <param name="nu1">Кэффициент упругости ν₁ (по умолчанию 1).</param>
      /// <param name="nu2">Кэффициент упругости ν₂ (по умолчанию 1).</param>
      public Fiber(double x = 0, double y = 0, double nu1 = 1, double nu2 = 1)
      {
         X = x;
         Y = y;
         Nu1 = nu1;
         Nu2 = nu2;
      }

      /// <summary>
      /// Создаёт волокно из WKT-строки полигона. Координаты и площадь вычисляются
      /// как центроид и площадь полигона.
      /// </summary>
      /// <param name="num">Порядковый номер волокна.</param>
      /// <param name="tag">Метка (тег) волокна.</param>
      /// <param name="polygon">WKT-строка, описывающая полигон волокна.</param>
      public Fiber(int num, string tag, string polygon)
      {
         Num = num;
         Tag = tag;
         WktHelper.ParseWKTPolygon(polygon, out var xs, out var ys, out var holeXs, out var holeYs);
         
         var (cx, cy) = WktHelper.PolygonCentroid(xs, ys);
         X = cx; Y = cy;
         Area = WktHelper.PolygonArea(xs, ys);
         Nu1 = 1;
         Nu2 = 1;
         WKT = polygon;
      }

      /// <summary>
      /// Создаёт копию волокна с сохранением координат, площади, WKT, преддеформации и тега.
      /// </summary>
      /// <returns>Новый объект Fiber с теми же значениями полей.</returns>
      public Fiber Clone()
      {
         return new(X, Y, Nu1, Nu2)
         {
            Area = Area,
            WKT = WKT,
            Eps_p = Eps_p,
            Tag = Tag
         };
      }

      /// <summary>
      /// Сдвигает волокно на вектор (xy.X, xy.Y). Если у волокна есть WKT-геометрия,
      /// полигон также сдвигается.
      /// </summary>
      /// <param name="fa">Волокно для сдвига.</param>
      /// <param name="xy">Вектор смещения.</param>
      /// <returns>Тот же объект волокна после сдвига.</returns>
      public static Fiber operator +(Fiber fa, XY xy)
      {
         if (fa.WKT != "")
         {
            WktHelper.ParseWKTPolygon(fa.WKT, out var xs, out var ys, out var holeXs, out var holeYs);
            for (int i = 0; i < xs.Count; i++) { xs[i] += xy.X; ys[i] += xy.Y; }
            List<List<(double X, double Y)>> holes = null;
            if (holeXs != null && holeXs.Count > 0)
            {
               holes = [];
               for (int h = 0; h < holeXs.Count; h++)
               {
                  var hPts = new List<(double X, double Y)>();
                  for (int i = 0; i < holeXs[h].Count; i++)
                     hPts.Add((holeXs[h][i] + xy.X, holeYs[h][i] + xy.Y));
                  holes.Add(hPts);
               }
            }
            var outerPts = new List<(double X, double Y)>();
            for (int i = 0; i < xs.Count; i++) outerPts.Add((xs[i], ys[i]));
            fa.WKT = WktHelper.PolygonToWKT(xs, ys, holes);
         }
         fa.X += xy.X; fa.Y += xy.Y;

         return fa;
      }

      /// <summary>
      /// Сдвигает волокно на вектор, обратный (xy.X, xy.Y). Если у волокна есть WKT-геометрия,
      /// полигон также сдвигается.
      /// </summary>
      /// <param name="fa">Волокно для сдвига.</param>
      /// <param name="xy">Вектор смещения (вычитается).</param>
      /// <returns>Тот же объект волокна после сдвига.</returns>
      public static Fiber operator -(Fiber fa, XY xy)
      {
         if (fa.WKT != "")
         {
            WktHelper.ParseWKTPolygon(fa.WKT, out var xs, out var ys, out var holeXs, out var holeYs);
            for (int i = 0; i < xs.Count; i++) { xs[i] -= xy.X; ys[i] -= xy.Y; }
            List<List<(double X, double Y)>> holes = null;
            if (holeXs != null && holeXs.Count > 0)
            {
               holes = [];
               for (int h = 0; h < holeXs.Count; h++)
               {
                  var hPts = new List<(double X, double Y)>();
                  for (int i = 0; i < holeXs[h].Count; i++)
                     hPts.Add((holeXs[h][i] - xy.X, holeYs[h][i] - xy.Y));
                  holes.Add(hPts);
               }
            }
            fa.WKT = WktHelper.PolygonToWKT(xs, ys, holes);
         }
         fa.X -= xy.X; fa.Y -= xy.Y;

         return fa;
      }

      /// <summary>
      /// Масштабирует волокно с коэффициентом scale. Координаты и площадь умножаются на scale.
      /// Если у волокна есть WKT-геометрия, создаётся новое волокно с масштабированным полигоном.
      /// </summary>
      /// <param name="fa">Волокно для масштабирования.</param>
      /// <param name="scale">Коэффициент масштабирования.</param>
      /// <returns>Масштабированное волокно.</returns>
      public static Fiber operator *(Fiber fa, double scale)
      {
         if (fa.WKT != "")
         {
            WktHelper.ParseWKTPolygon(fa.WKT, out var xs, out var ys, out var holeXs, out var holeYs);
            for (int i = 0; i < xs.Count; i++) { xs[i] *= scale; ys[i] *= scale; }
            List<List<(double X, double Y)>> holes = null;
            if (holeXs != null && holeXs.Count > 0)
            {
               holes = [];
               for (int h = 0; h < holeXs.Count; h++)
               {
                  var hPts = new List<(double X, double Y)>();
                  for (int i = 0; i < holeXs[h].Count; i++)
                     hPts.Add((holeXs[h][i] * scale, holeYs[h][i] * scale));
                  holes.Add(hPts);
               }
            }
            var newWkt = WktHelper.PolygonToWKT(xs, ys, holes);
            fa = new Fiber(fa.Num, fa.Tag, newWkt) { };
            return fa;
         }
         fa.X *= scale; fa.Y *= scale; fa.Area *= scale;

         return fa;
      }
   }
}