namespace CScore.Import.Cad;

/// <summary>Точка чертежа в единицах чертежа.</summary>
public readonly record struct CadPoint(double X, double Y);

/// <summary>Габарит в единицах чертежа.</summary>
public readonly record struct CadBounds(double MinX, double MinY, double MaxX, double MaxY)
{
   public double Width => MaxX - MinX;
   public double Height => MaxY - MinY;
   public double CenterX => (MinX + MaxX) / 2;
   public double CenterY => (MinY + MaxY) / 2;

   public bool Contains(double x, double y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

   /// <summary>Габарит целиком внутри этого.</summary>
   public bool Contains(CadBounds b) => b.MinX >= MinX && b.MaxX <= MaxX && b.MinY >= MinY && b.MaxY <= MaxY;

   public bool Intersects(CadBounds b) => b.MinX <= MaxX && b.MaxX >= MinX && b.MinY <= MaxY && b.MaxY >= MinY;

   public CadBounds Union(CadBounds b) =>
      new(Math.Min(MinX, b.MinX), Math.Min(MinY, b.MinY), Math.Max(MaxX, b.MaxX), Math.Max(MaxY, b.MaxY));

   public CadBounds Inflate(double d) => new(MinX - d, MinY - d, MaxX + d, MaxY + d);

   /// <summary>Расстояние от точки до габарита (0 — внутри).</summary>
   public double DistanceTo(double x, double y)
   {
      double dx = Math.Max(0, Math.Max(MinX - x, x - MaxX));
      double dy = Math.Max(0, Math.Max(MinY - y, y - MaxY));
      return Math.Sqrt(dx * dx + dy * dy);
   }

   public static CadBounds Of(IReadOnlyList<CadPoint> pts)
   {
      double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
      foreach (var p in pts)
      {
         if (p.X < x0) x0 = p.X;
         if (p.Y < y0) y0 = p.Y;
         if (p.X > x1) x1 = p.X;
         if (p.Y > y1) y1 = p.Y;
      }
      return new(x0, y0, x1, y1);
   }
}

/// <summary>
/// Замкнутый контур чертежа: вершины без повтора первой в конце, дуги уже заменены ломаной
/// (замкнутая LWPOLYLINE/POLYLINE, в том числе из блока — в мировых координатах).
/// </summary>
public sealed class CadPolygon
{
   public CadPolygon(string layer, IReadOnlyList<CadPoint> points)
   {
      Layer = layer;
      Points = points;
      Bounds = CadBounds.Of(points);
      Area = CadGeometry.SignedArea(points);
   }

   public string Layer { get; }
   public IReadOnlyList<CadPoint> Points { get; }
   public CadBounds Bounds { get; }

   /// <summary>Площадь со знаком: &gt; 0 — обход против часовой стрелки.</summary>
   public double Area { get; }
}

/// <summary>
/// Линия чертежа: полилиния (дуги заменены ломаной) или цепочка LINE/ARC/полилиний после сшивки — до превращения
/// в контур. Замкнутые линии дублируют контуры <see cref="CadSnapshot.Polygons"/>; линии нужны роли «Хомуты»
/// (ось хомута, ширина полилинии = диаметр, незамкнутые хомуты с крючками и шпильки).
/// </summary>
public sealed class CadPath
{
   public CadPath(string layer, IReadOnlyList<CadPoint> points, bool closed, double width)
   {
      Layer = layer;
      Points = points;
      Closed = closed;
      Width = width;
      Bounds = CadBounds.Of(points);
   }

   public string Layer { get; }

   /// <summary>Вершины; у замкнутой первая в конце не повторяется.</summary>
   public IReadOnlyList<CadPoint> Points { get; }

   public bool Closed { get; }

   /// <summary>Постоянная ширина полилинии в единицах чертежа; 0 — без ширины или ширина переменная.</summary>
   public double Width { get; }

   public CadBounds Bounds { get; }
}

/// <summary>Окружность чертежа.</summary>
public sealed record CadCircle(string Layer, double X, double Y, double R)
{
   public CadBounds Bounds => new(X - R, Y - R, X + R, Y + R);
}

/// <summary>Однострочный или многострочный текст (без кодов форматирования).</summary>
public sealed record CadText(string Layer, double X, double Y, double Height, string Text);

/// <summary>
/// Снимок геометрии чертежа, общий для DXF-файла и открытого документа AutoCAD/nanoCAD:
/// распознавание сечений работает только с ним и не зависит от источника.
/// </summary>
public sealed class CadSnapshot
{
   public List<CadPolygon> Polygons { get; } = [];
   public List<CadCircle> Circles { get; } = [];
   public List<CadText> Texts { get; } = [];
   public List<CadPath> Paths { get; } = [];

   /// <summary>Объекты, которые не удалось прочитать или не поддерживаются (незамкнутые полилинии и т. п.).</summary>
   public List<string> Warnings { get; } = [];

   /// <summary>Имя источника (файл или документ CAD).</summary>
   public string Source { get; set; } = "";

   /// <summary>Метров в единице чертежа по $INSUNITS; null — единицы не заданы.</summary>
   public double? MetersPerUnit { get; set; }

   /// <summary>Слои, на которых есть объекты, с их числом.</summary>
   public IReadOnlyList<CadLayerInfo> Layers()
   {
      var map = new Dictionary<string, CadLayerInfo>(StringComparer.OrdinalIgnoreCase);
      CadLayerInfo Get(string l) => map.TryGetValue(l, out var i) ? i : map[l] = new CadLayerInfo(l);
      foreach (var p in Polygons) Get(p.Layer).Polygons++;
      foreach (var c in Circles) Get(c.Layer).Circles++;
      foreach (var t in Texts) Get(t.Layer).Texts++;
      foreach (var l in Paths)
         if (!l.Closed) Get(l.Layer).Lines++;
      return [.. map.Values.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)];
   }

   /// <summary>Объекты, целиком лежащие в рамке (выбор рамкой слева направо в CAD).</summary>
   public CadSnapshot Within(CadBounds window)
   {
      var s = new CadSnapshot { Source = Source, MetersPerUnit = MetersPerUnit };
      s.Polygons.AddRange(Polygons.Where(p => window.Contains(p.Bounds)));
      s.Circles.AddRange(Circles.Where(c => window.Contains(c.Bounds)));
      s.Texts.AddRange(Texts.Where(t => window.Contains(t.X, t.Y)));
      s.Paths.AddRange(Paths.Where(l => window.Contains(l.Bounds)));
      s.Warnings.AddRange(Warnings);
      return s;
   }

   /// <summary>Габарит всех объектов; null — снимок пуст.</summary>
   public CadBounds? Extents()
   {
      CadBounds? b = null;
      void Add(CadBounds x) => b = b is { } v ? v.Union(x) : x;
      foreach (var p in Polygons) Add(p.Bounds);
      foreach (var c in Circles) Add(c.Bounds);
      foreach (var t in Texts) Add(new CadBounds(t.X, t.Y, t.X, t.Y));
      foreach (var l in Paths) Add(l.Bounds);
      return b;
   }
}

/// <summary>Слой снимка и число объектов на нём по видам.</summary>
public sealed class CadLayerInfo(string name)
{
   public string Name { get; } = name;
   public int Polygons { get; set; }
   public int Circles { get; set; }
   public int Texts { get; set; }

   /// <summary>Незамкнутые линии (замкнутые учтены в <see cref="Polygons"/>).</summary>
   public int Lines { get; set; }
}
