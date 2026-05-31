using NetTopologySuite.Geometries;
using Newtonsoft.Json;
using NetTopologySuite.IO;
using System.Collections.Generic;

namespace CScore
{
   /// <summary>
   /// Область сечения — описывает замкнутый контур (внешний и отверстия)
   /// с назначенным материалом. Является базовым классом для <see cref="FiberRegion"/>
   /// и <see cref="RCFiberRegion"/>. Поддерживает триангуляцию и нарезку на волокна.
   /// </summary>
   [Serializable]
   public class Region
   {
      string? str;

      /// <summary>
      /// Первичный ключ для EF Core.
      /// </summary>
      public int Id { get; set; }

      /// <summary>
      /// Порядковый номер области в коллекции.
      /// </summary>
      public int Num { get; set; }

      /// <summary>
      /// Наименование (тег) области.
      /// </summary>
      public string Tag { get; set; } = "";

      /// <summary>
      /// Список контуров: внешний (Hull) и отверстия (Holes).
      /// </summary>
      public List<Contour> Contours { get; set; } = [];

      /// <summary>
      /// Материал области.
      /// </summary>
      public Material? Material { get; set; }

      /// <summary>
      /// WKT-представление (Well-Known Text) полигона области.
      /// </summary>
      public string? WKT { get; set; }

      /// <summary>
      /// Высота сечения (размер по оси Y) [м].
      /// </summary>
      public double H { get; set; }

      /// <summary>
      /// Полигон NetTopologySuite, восстановленный из WKT.
      /// Не сохраняется в БД.
      /// </summary>
      [JsonIgnore]
      public Polygon Polygon { get => GetPolygon(); }

      /// <summary>
      /// Геометрические характеристики сечения (A, Sx, Sy, Ix, Iy и др.).
      /// Не сохраняется в БД.
      /// </summary>
      [JsonIgnore]
      public GeoProps Props { get => new(this); }

      /// <summary>
      /// Текстовое описание области.
      /// </summary>
      public string? Description { get; set; }

      /// <summary>
      /// Внешний контур (Hull) области. При установке автоматически
      /// помечается как <see cref="ContourType.Hull"/> и добавляется
      /// в начало списка контуров.
      /// </summary>
      public Contour? Hull
      {
         get { return GetHull(); }
         set
         {
            if (value != null)
            {
               value.Type = ContourType.Hull;
               if (Contours.Count > 0)
               {
                  var hullIndex = Contours.FindIndex(c => c.Type == ContourType.Hull);
                  if (hullIndex >= 0)
                     Contours[hullIndex] = value;
                  else
                     Contours.Insert(0, value);
               }
               else
               {
                  Contours.Add(value);
               }
            }
         }
      }

      /// <summary>
      /// Список отверстий (внутренних контуров) области.
      /// При установке помечает все контуры как <see cref="ContourType.Hole"/>.
      /// </summary>
      public IList<Contour>? Holes
      {
         get { return GetHoles(); }
         set
         {
            if (value != null && value.Count > 0)
            {
               var o = Hull;

               foreach (var item in value)
                  item.Type = ContourType.Hole;

               Contours.Clear();
               if(o != null) Contours.Add(o);
               Contours.AddRange(value);
            }
         }
      }

      /// <summary>
      /// Конструктор по умолчанию.
      /// </summary>
      public Region()
      {

      }

      /// <summary>
      /// Создаёт область из внешнего контура и набора отверстий.
      /// </summary>
      /// <param name="tag">Наименование (тег) области.</param>
      /// <param name="material">Материал области (может быть null).</param>
      /// <param name="outc">Внешний контур (Hull).</param>
      /// <param name="holes">Отверстия (может быть null).</param>
      public Region(string tag, Material material, Contour outc, IEnumerable<Contour> holes)
      {
         Tag = tag;
         Material = material;
         outc.Type = ContourType.Hull;
         Contours.Add(outc);

         if (holes != null)
         {
            foreach (Contour hole in holes)
               hole.Type = ContourType.Hole;
            Contours.AddRange(holes);
         };

         Polygon? poly = null;
         if (Contours.Count == 1) poly = new Polygon(Hull.LinearRing);
         else
         {
            var lrs = (from h in Contours where h.Type == ContourType.Hole select h.LinearRing).ToArray();
            poly = new Polygon(Hull.LinearRing, lrs);
         }

         WKT = poly.ToText();
         double ymin = poly.Envelope.Coordinates[0].Y;
         double ymax = poly.Envelope.Coordinates[1].Y;
         H = ymax - ymin;
      }

      /// <summary>
      /// Создаёт область из полигона NetTopologySuite. Отверстия извлекаются автоматически.
      /// </summary>
      /// <param name="tag">Наименование (тег) области.</param>
      /// <param name="material">Материал области (может быть null).</param>
      /// <param name="polygon">Полигон NetTopologySuite.</param>
      public Region(string tag, Material material, Polygon polygon)
      {
         Tag = tag;
         Material = material;
         Contour c = new(polygon, tag);
         Contours.Add(c);

         if (polygon.Holes.Length != 0)
         {
            Contours = new List<Contour>(polygon.Holes.Length);
            int j = 0;
            foreach (var item in polygon.Holes)
            {
               double[] x = new double[item.Coordinates.Length];
               double[] y = new double[item.Coordinates.Length];
               for (int i = 0; i < item.Coordinates.Length; i++)
               {
                  Coordinate crd = item.Coordinates[i];
                  x[i] = crd.X; y[i] = crd.Y;
               }
               Contour hole = new(x, y, $"{j++:D2}#hole");
               Contours.Add(hole);
            }
         }
      }

      /// <summary>
      /// Создаёт область из WKT-строки полигона.
      /// </summary>
      /// <param name="tag">Наименование (тег) области.</param>
      /// <param name="material">Материал области (может быть null).</param>
      /// <param name="polystring">WKT-строка полигона.</param>
      public Region(string tag, Material material, string polystring)
      {
         Tag = tag;
         Material = material;
         WKTReader reader = new WKTReader();
         var polygon = (Polygon)reader.Read(polystring);
         Contours.Add(new(polygon, "out"));
         if (polygon.Holes.Length != 0)
         {
            Contours = new List<Contour>(polygon.Holes.Length);
            int j = 0;
            foreach (var item in polygon.Holes)
            {
               double[] x = new double[item.Coordinates.Length];
               double[] y = new double[item.Coordinates.Length];
               for (int i = 0; i < item.Coordinates.Length; i++)
               {
                  Coordinate crd = item.Coordinates[i];
                  x[i] = crd.X; y[i] = crd.Y;
               }

               Contour hole = new(x, y, $"{j++:D2}#hole");
               Contours.Add(hole);
            }

         };

         WKT = polystring;
         double ymin = polygon.Envelope.Coordinates[0].Y;
         double ymax = polygon.Envelope.Coordinates[1].Y;
         H = ymax - ymin;
      }

      /// <summary>
      /// Создаёт прямоугольную область (плиту) заданной высоты с центром в (xc, yc).
      /// Ширина плиты — 1 м.
      /// </summary>
      /// <param name="id">Порядковый номер области.</param>
      /// <param name="material">Материал области.</param>
      /// <param name="h">Высота сечения [м].</param>
      /// <param name="xc">Координата X центра [м].</param>
      /// <param name="yc">Координата Y центра [м].</param>
      /// <param name="tag">Наименование (тег) области.</param>
      /// <param name="issliced">Не используется (зарезервировано).</param>
      public Region(int id, Material material, double h, double xc = 0, double yc = 0, string tag = "Plate", bool issliced = false)
      {
         Material = material;
         H = h;
         Tag = tag;
         Num = id;
         double[] X = new double[5];
         double[] Y = new double[5];
         X[0] = xc - 0.5; Y[0] = yc - 0.5 * h;
         X[1] = xc + 0.5; Y[1] = yc - 0.5 * h;
         X[2] = xc + 0.5; Y[2] = yc + 0.5 * h;
         X[3] = xc - 0.5; Y[3] = yc + 0.5 * h;
         X[4] = X[0]; Y[4] = Y[0];

         Hull = new Contour(X, Y, "plate");

         Hull.Points[0].Tag = "bot";
         Hull.Points[3].Tag = "top";

         WKT = new Polygon(Hull.LinearRing).ToText();
      }

      /// <inheritdoc/>
      public override string ToString()
      {
         if(Material==null)
            return $"{Num:D3}#region : {Tag} | <No Material>";
         else return $"{Num:D3}#region : {Tag} | <{Material.Tag}>";
      }

      /// <summary>
      /// Пересчитывает WKT-представление и высоту области по текущему Hull и отверстиям.
      /// </summary>
      public void SetWKT()
      {
         Polygon? poly = null;
         if (Hull == null) return;
         if (Contours.Count == 1) poly = new Polygon(Hull.LinearRing);
         else
         {
            var lrs = (from h in Contours where h.Type == ContourType.Hole select h.LinearRing).ToArray();
            poly = new Polygon(Hull.LinearRing, lrs);
         }

         WKT = poly.ToText();
         double ymin = poly.Envelope.Coordinates[0].Y;
         double ymax = poly.Envelope.Coordinates[1].Y;
         H = ymax - ymin;
      }

      /// <summary>
      /// Пересчитывает WKT-представление. Синоним <see cref="SetWKT"/>.
      /// </summary>
      public void GetPolystring()
      {
         SetWKT();
      }

      /// <summary>
      /// Возвращает внешний контур (Hull) из списка контуров.
      /// </summary>
      Contour GetHull()
      {
         if (Contours == null)
            return null;
         else
         {
            Contour res = null;
            foreach (var item in Contours)
            {
               if (item.Type == ContourType.Hull)
               {
                  res = item;
                  break;
               }
            }
            return res;
         }
      }

      /// <summary>
      /// Возвращает список отверстий из списка контуров.
      /// </summary>
      List<Contour> GetHoles()
      {
         if (Contours == null || Contours.Count == 0)
            return null;
         else
         {
            List<Contour> res = [];
            foreach (var item in Contours)
            {
               if (item.Type == ContourType.Hole)
                  res.Add(item);
               else
                  continue;
            }

            if (res.Count == 0) return null;
            else return res;
         }
      }

      /// <summary>
      /// Восстанавливает полигон NetTopologySuite из WKT-строки.
      /// </summary>
      Polygon GetPolygon()
      {
         WKTReader reader = new WKTReader();
         return (Polygon)reader.Read(WKT);
      }

      /// <summary>
      /// Разбивает область на волокна методом триангуляции.
      /// </summary>
      /// <param name="maxTrgArea">Максимальная площадь треугольника (доля от площади области, по умолчанию 0.01).</param>
      /// <param name="maxAngl">Минимальный угол треугольника в градусах (по умолчанию 30).</param>
      /// <returns>Область волокон <see cref="FiberRegion"/> с треугольными волокнами.</returns>
      public FiberRegion Triangulation(double maxTrgArea = 0.01, double maxAngl = 30)
      {
         Fiber[] res = Geo.Triangulation(this, maxTrgArea, maxAngl);
         return new FiberRegion(this, res);
      }

      /// <summary>
      /// Разбивает область на волокна методом нарезки прямоугольной сеткой (по X и Y).
      /// </summary>
      /// <param name="nx">Количество участков деления по оси X (по умолчанию 40).</param>
      /// <param name="ny">Количество участков деления по оси Y (по умолчанию 40).</param>
      /// <returns>Область волокон <see cref="FiberRegion"/>.</returns>
      public FiberRegion SliceXY(int nx = 40, int ny = 40)
      {
         Fiber[] res = Geo.SliceXY(this, nx, ny);

         return new FiberRegion(this, res);
      }

      /// <summary>
      /// Разбивает область на волокна методом нарезки вертикальными полосами (по X).
      /// </summary>
      /// <param name="nx">Количество участков деления по оси X (по умолчанию 40).</param>
      /// <returns>Область волокон <see cref="FiberRegion"/>.</returns>
      public FiberRegion SliceX(int nx = 40)
      {
         Fiber[] res = Geo.SliceX(this, nx);

         return new FiberRegion(this, res);
      }

      /// <summary>
      /// Разбивает область на волокна методом нарезки горизонтальными полосами (по Y).
      /// </summary>
      /// <param name="ny">Количество участков деления по оси Y (по умолчанию 40).</param>
      /// <returns>Область волокон <see cref="FiberRegion"/>.</returns>
      public FiberRegion SliceY(int ny = 40)
      {
         Fiber[] res = Geo.SliceY(this, ny);

         return new FiberRegion(this, res);
      }

      /// <summary>
      /// Добавляет отверстие (внутренний контур) в область.
      /// </summary>
      /// <param name="hole">Контур отверстия.</param>
      public void AddHole(Contour hole)
      {
         if (Contours == null) Contours = new List<Contour>() { hole };
         else Contours.Add(hole);
      }

      /// <summary>
      /// Вычисляет начальное приближение кривизны плоскости деформаций
      /// по заданной нагрузке, используя упругие геометрические характеристики.
      /// Формулы: e₀ = N/EA, k_y = My/EIy, k_z = Mz/EIx.
      /// </summary>
      /// <param name="load">Внешняя нагрузка (N, My, Mz).</param>
      /// <returns>Начальное приближение кривизны.</returns>
      public Kurvature Guess(Load load)
      {
         GeoProps pr = Props;

         return new Kurvature()
         {
            e0 = load.N / pr.EA,
            ky = load.My / pr.EIy,
            kz = load.Mz / pr.EIx
         };
      }

      /// <summary>
      /// Вычисляет кривизну плоскости деформаций с масштабным коэффициентом.
      /// </summary>
      /// <param name="load">Внешняя нагрузка.</param>
      /// <param name="k">Масштабный коэффициент для нагрузки.</param>
      /// <returns>Масштабированная кривизна.</returns>
      public Kurvature Guess(Load load, double k)
      {
         GeoProps pr = Props;

         return new Kurvature()
         {
            e0 = load.N * k / pr.EA,
            ky = load.My * k / pr.EIy,
            kz = load.Mz * k / pr.EIx
         };
      }

      /// <summary>
      /// Вычисляет напряжения в точках внешнего контура по линейному закону
      /// (упругая стадия): σ = E · ε, где ε вычисляется из кривизны.
      /// </summary>
      /// <param name="k">Кривизна плоскости деформаций.</param>
      public void SetStress(Kurvature k)
      {
         double E = Material.E;
         for (int i = 0; i < Hull.Points.Count; i++)
         {
            Hull.Points[i].Eps = k.e0 + k.ky * Hull.Points[i].Y + k.kz * Hull.Points[i].X;
            Hull.Points[i].Sig = E * Hull.Points[i].Eps;
         }
      }

      /// <summary>
      /// Возвращает минимальную и максимальную деформации в точках внешнего контура.
      /// </summary>
      /// <returns>Массив [ε_min, ε_max].</returns>
      public double[] StrainExtremums()
      {
         double[] result = new double[2];
         List<double> s = new List<double>(Hull.Points.Count);
         for (int i = 0; i < Hull.Points.Count; i++)
         {
            s.Add(Hull.Points[i].Eps);
         }
         result[0] = s.Min(); result[1] = s.Max();
         return result;
      }

      /// <summary>
      /// Создаёт глубокую копию области, включая контур и точки с напряжениями.
      /// </summary>
      /// <returns>Новый объект Region с копиями данных.</returns>
      public virtual Region Clone()
      {
         Region res = new Region(Tag + "_clone", Material, Hull, Contours);

         var pts = new List<StressPoint>(Hull.Points);
         Hull.Points.Clear();
         for (int i = 0; i < pts.Count; i++)
            res.Hull.Points.Add(pts[i].Clone());

         return res;
      }

      /// <summary>
      /// Сдвигает область на вектор (xy.X, xy.Y), обновляя Hull, контуры и WKT.
      /// </summary>
      public static Region operator +(Region r, XY xy)
      {
         r.Hull += xy;

         if (r.Contours != null)
            for (int i = 0; i < r.Contours.Count; i++)
               r.Contours[i] += xy;

         Polygon poly = r.Contours != null ? new Polygon(r.Hull.LinearRing) :
            new Polygon(r.Hull.LinearRing, (from h in r.Contours select h.LinearRing).ToArray()); ;

         r.WKT = poly.ToText();

         return r;
      }

      /// <summary>
      /// Сдвигает область на вектор, обратный (xy.X, xy.Y), обновляя Hull, контуры и WKT.
      /// </summary>
      public static Region operator -(Region r, XY xy)
      {
         r.Hull -= xy;

         if (r.Contours != null)
            for (int i = 0; i < r.Contours.Count; i++)
               r.Contours[i] -= xy;

         Polygon poly = r.Contours != null ? new Polygon(r.Hull.LinearRing) :
            new Polygon(r.Hull.LinearRing, (from h in r.Contours select h.LinearRing).ToArray()); ;

         r.WKT = poly.ToText();

         return r;
      }

      /// <summary>
      /// Масштабирует область на заданный коэффициент, обновляя Hull, контуры и WKT.
      /// </summary>
      public static Region operator *(Region r, double scale)
      {
         r.Hull *= scale;

         if (r.Contours != null)
            for (int i = 0; i < r.Contours.Count; i++)
               r.Contours[i] *= scale;

         Polygon poly = r.Contours != null ? new Polygon(r.Hull.LinearRing) :
            new Polygon(r.Hull.LinearRing, (from h in r.Contours select h.LinearRing).ToArray()); ;

         r.WKT = poly.ToText();

         return r;
      }
   }
}