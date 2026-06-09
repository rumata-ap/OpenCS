using System.Text.Json.Serialization;
using System.Collections.ObjectModel;

namespace CScore
{
   public enum RegionType { Contour = 1, MaterialArea = 2, Fiber = 3, CrossSection = 4 }
   public enum ContourType { Hull = 1, Hole = 2, None = 0 }

   [Serializable]
   /// <summary>
   /// Представление плоского замкнутого контура, состоящего из прямолинейных сегментов.
   /// </summary>
   public class Contour
   {
      string str = null!;
      ObservableCollection<StressPoint> points = [];
      public int Num { get; set; }
      public string Tag { get; set; } = "";
      public string? GeometrySet { get; set; }
      public string WKT { get; set; } = "";
      public IList<double> X { get; set; } = [];
      public IList<double> Y { get; set; } = [];
      public ContourType Type { get; set; } = ContourType.None;

      [JsonIgnore]
      public ObservableCollection<StressPoint> Points 
      { 
         get => points; 
         set { points = value; PointsToXYs(); } 
      }
      [JsonIgnore]
      public GeoProps Props { get => new(this); }
      [JsonIgnore] public string? Json { get; set; }
      [JsonIgnore] public int Id { get; set; }
      public string Description { get => ToString(); set => str = value; }

      public Contour() { }

      /// <summary>
      /// Создает плоский замкнутый самонепересекающийся контур, состоящий из прямолинейных сегментов.
      /// </summary>
      /// <param name="vertices">Массив координат вершин контура</param>
      /// <exception cref="Exception"></exception>
      /// <remarks>
      /// Координаты первой вершины должны быть равны координатам последней. Кроме того
      /// минимальное количество вершин божно быть равно 4, что соответсвует замкнутому контуру треугольника.
      /// </remarks>
      public Contour(IEnumerable<StressPoint> vertices, string tag)
      {
         Tag = tag;
         if (vertices == null) throw new Exception("Значение массива вершин равно null");
         else if (vertices.Count() < 4) throw new Exception("Массив вершин должен содержать больше трех элементов");
         X = (from v in vertices select v.X).ToList();
         Y = (from v in vertices select v.Y).ToList();

         SetWKT();
         Points = [.. vertices];
      }

      public Contour(IEnumerable<double> x, IEnumerable<double> y, string tag)
      {
         if (x.Count() != y.Count())
            throw new ArgumentException("Длины массивов x, y и dy должны совпадать.");
         if (x.Count() < 5)
            throw new ArgumentException("Массивы x и y должны содержать минимум 4 элемента.");

         Tag = tag;

         X = x.ToList();
         Y = y.ToList();
         
         SetWKT();
         Points = XYsToPoints();
      }

      /// <summary>
      /// Создаёт контур из массивов координат (внешнее кольцо полигона WKT).
      /// Отверстия не извлекаются — используйте Region для этого.
      /// </summary>
      public Contour(string wkt, string tag)
      {
         Tag = tag;
         WktHelper.ParseWKTPolygon(wkt, out var ox, out var oy, out _, out _);
         X = ox;
         Y = oy;
         WKT = wkt;
         Points = XYsToPoints();
      }

      public override string ToString()
      {
         if (GeometrySet == null)
            return $"{Num:D3}#contour : {Tag} | '{Type}' | No GeometrySet";
         else return $"{Num:D3}#contour : {Tag} | '{Type}'  | '{GeometrySet}'";
      }

      public void SetWKT()
      {
         WKT = WktHelper.PolygonToWKT(X, Y, null);
      }

      public void Scale(double scale)
      {
         X = (from x in X select x * scale).ToList();
         Y = (from y in Y select y * scale).ToList();
      }

      XY CalcI()
      {
         double tempX = 0;
         double tempY = 0;
         for (int i = 0; i < Points.Count - 1; i++)
         {
            tempX += (Math.Pow(X[i], 2) + X[i] * X[i + 1] + Math.Pow(X[i + 1], 2)) * (X[i] * Y[i + 1] - Y[i] * X[i + 1]);
            tempY += (Math.Pow(Y[i], 2) + Y[i] * Y[i + 1] + Math.Pow(Y[i + 1], 2)) * (X[i] * Y[i + 1] - Y[i] * X[i + 1]);
         }
         return new XY(Math.Abs(tempY / 12), Math.Abs(tempX / 12));
      }

      internal XY CalcCentroid(out double area)
      {
         CalcI();
         area = CalcArea();
         XY temp = new XY();
         for (int i = 0; i < Points.Count - 1; i++)
         {
            temp.X += 1 / (6 * area) * (X[i] + X[i + 1]) * (X[i] * Y[i + 1] - Y[i] * X[i + 1]);
            temp.Y += 1 / (6 * area) * (Y[i] + Y[i + 1]) * (X[i] * Y[i + 1] - Y[i] * X[i + 1]);
         }
         area = Math.Abs(area);
         return temp;
      }

      double CalcArea()
      {
         double temp = 0;
         for (int i = 0; i < Points.Count - 1; i++)
         {
            temp += 0.5 * (X[i] * Y[i + 1] - X[i + 1] * Y[i]);
         }
         return temp;
      }

      public ObservableCollection<StressPoint> XYsToPoints()
      {
         StressPoint[] res = new StressPoint[X.Count];
         for (int i = 0; i < X.Count; i++)
            res[i] = new StressPoint(X[i], Y[i]);

         return [.. res];
      }

      public void PointsToXYs()
      {
         if (points == null) throw new Exception("Значение массива вершин равно null");
         if (points.Count() < 4) throw new Exception("Массив вершин должен содержать больше трех элементов");
         X = (from v in points select v.X).ToList();
         Y = (from v in points select v.Y).ToList();
      }

      public static Contour operator +(Contour c, XY xy)
      {
         c.X = (from x in c.X select x + xy.X).ToList();
         c.Y = (from y in c.Y select y + xy.Y).ToList();
         c.SetWKT();
         return c;
      }

      public static Contour operator -(Contour c, XY xy)
      {
         c.X = (from x in c.X select x - xy.X).ToList();
         c.Y = (from y in c.Y select y - xy.Y).ToList();
         c.SetWKT();
         return c;
      }

      public static Contour operator +(Contour c, double xy)
      {
         c.X = (from x in c.X select x + xy).ToList();
         c.Y = (from y in c.Y select y + xy).ToList();
         c.SetWKT();
         return c;
      }

      public static Contour operator -(Contour c, double xy)
      {
         c.X = (from x in c.X select x - xy).ToList();
         c.Y = (from y in c.Y select y - xy).ToList();
         c.SetWKT();
         return c;
      }

      public static Contour operator *(Contour c, double scale)
      {
         c.X = (from x in c.X select x * scale).ToList();
         c.Y = (from y in c.Y select y * scale).ToList();
         c.SetWKT();
         return c;
      }
   }
}