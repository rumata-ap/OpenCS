using System.Globalization;
using System.Text;

namespace CScore.Import.Cad;

/// <summary>Настройки распознавания сечений на чертеже.</summary>
public sealed class CadImportOptions
{
   /// <summary>Метров в единице чертежа (0,001 — чертёж в мм).</summary>
   public double MetersPerUnit { get; set; } = 0.001;

   /// <summary>Роли слоёв; слой без записи — <see cref="CadLayerRole.Ignore"/>.</summary>
   public Dictionary<string, CadLayerRole> Roles { get; } = new(StringComparer.OrdinalIgnoreCase);

   /// <summary>Способ дискретизации дуг и окружностей; по умолчанию — по отклонению хорды.</summary>
   public CadArcMode ArcMode { get; set; } = CadArcMode.Deviation;

   /// <summary>Наибольшее отклонение хорды от дуги, м (<see cref="CadArcMode.Deviation"/>).</summary>
   public double ArcDeviationM { get; set; } = CadArcDiscretization.DefaultDeviationM;

   /// <summary>Длина хорды, м (<see cref="CadArcMode.Chord"/>).</summary>
   public double ArcChordM { get; set; } = 0.01;

   /// <summary>Число отрезков на дугу (<see cref="CadArcMode.FixedSegments"/>).</summary>
   public int ArcSegments { get; set; } = 16;

   /// <summary>Дискретизация в единицах чертежа.</summary>
   public CadArcDiscretization Arcs =>
      CadArcDiscretization.FromMeters(ArcMode, ArcDeviationM, ArcChordM, ArcSegments, MetersPerUnit);

   /// <summary>Объединять одинаковые по геометрии сечения в одно (имена — списком).</summary>
   public bool MergeIdentical { get; set; } = true;

   /// <summary>Шаг квантования координат при сравнении сечений, м.</summary>
   public double QuantumM { get; set; } = 0.0005;

   /// <summary>Округлять диаметры стержней до сортамента, если отличие не больше 0,5 мм.</summary>
   public bool SnapDiameters { get; set; } = true;

   public CadLayerRole RoleOf(string layer) => Roles.TryGetValue(layer, out var r) ? r : CadLayerRole.Ignore;

   /// <summary>Роли по именам слоёв снимка (<see cref="CadLayerRules.Guess"/>), не трогая уже заданные.</summary>
   public CadImportOptions GuessRoles(CadSnapshot snapshot)
   {
      foreach (var l in snapshot.Layers())
         if (!Roles.ContainsKey(l.Name))
            Roles[l.Name] = CadLayerRules.Guess(l);
      return this;
   }
}

/// <summary>Область распознанного сечения в местных осях, м: внешний контур против часовой, отверстия — по часовой.</summary>
public sealed class CadSectionRegion
{
   public CadLayerRole Role { get; init; }
   public string Layer { get; init; } = "";
   public List<CadPoint> Outer { get; init; } = [];
   public List<List<CadPoint>> Holes { get; } = [];

   /// <summary>Площадь за вычетом отверстий, м².</summary>
   public double Area => Math.Abs(CadGeometry.SignedArea(Outer)) - Holes.Sum(h => Math.Abs(CadGeometry.SignedArea(h)));

   /// <summary>Точка внутри области (во внешнем контуре и не в отверстии).</summary>
   public bool Contains(double x, double y) =>
      CadGeometry.Inside(Outer, x, y) && !Holes.Any(h => CadGeometry.Inside(h, x, y));
}

/// <summary>Стержень распознанного сечения в местных осях, м.</summary>
public sealed class CadSectionBar
{
   public string Layer { get; init; } = "";
   public double X { get; init; }
   public double Y { get; init; }
   public double DiameterM { get; init; }

   /// <summary>Индекс области в <see cref="CadSectionCandidate.Regions"/>, в которой лежит стержень; null — вне областей.</summary>
   public int? Host { get; init; }
}

/// <summary>Распознанное сечение (возможно, объединённое из нескольких одинаковых на чертеже).</summary>
public sealed class CadSectionCandidate
{
   /// <summary>Имя (первое из <see cref="Names"/>).</summary>
   public string Name => Names[0];

   /// <summary>Имена всех экземпляров на чертеже.</summary>
   public List<string> Names { get; } = [];

   /// <summary>Габариты экземпляров в единицах чертежа (для показа в CAD).</summary>
   public List<CadBounds> Instances { get; } = [];

   /// <summary>Начало местных осей первого экземпляра в единицах чертежа (центр тяжести областей).</summary>
   public CadPoint Origin { get; init; }

   public List<CadSectionRegion> Regions { get; } = [];
   public List<CadSectionBar> Bars { get; } = [];
   public List<string> Warnings { get; } = [];

   /// <summary>Сигнатура геометрии для сравнения сечений.</summary>
   public string Signature { get; set; } = "";

   public int Count => Instances.Count;
}

/// <summary>Результат распознавания.</summary>
public sealed class CadRecognitionResult
{
   public List<CadSectionCandidate> Sections { get; } = [];

   /// <summary>Объекты, не попавшие ни в одно сечение, и прочие общие замечания.</summary>
   public List<string> Warnings { get; } = [];

   /// <summary>Число распознанных экземпляров до объединения одинаковых.</summary>
   public int Instances => Sections.Sum(s => s.Count);
}

/// <summary>
/// Распознавание множества поперечных сечений на одном листе чертежа.
/// <para>Сегментация: если есть слои роли «Рамка» — каждая рамка одно сечение (объект относится к самой внутренней
/// рамке, в которой лежит); иначе каждое дерево вложенности контуров бетона/стали с корнем верхнего уровня.</para>
/// <para>Внутри сечения: контур «Отверстие» — пустота области, в которой лежит; контур того же слоя, что и
/// охватывающая область, — тоже пустота (полое сечение, нарисованное двумя полилиниями); контур другого
/// материала — пустота в охватывающей области и самостоятельная область (сталь в бетоне, бетон в трубе);
/// контур в пустоте — снова область. Окружности «Арматура» — стержни, окружности бетона/стали/отверстий — круглые контуры.</para>
/// </summary>
public static class CadSectionRecognizer
{
   /// <summary>Сортамент диаметров стержней, мм.</summary>
   public static readonly double[] StandardDiametersMm =
      [3, 4, 5, 6, 8, 10, 12, 14, 16, 18, 20, 22, 25, 28, 32, 36, 40, 45, 50, 55, 60, 70, 80];

   enum Kind { Region, Hole, Skip }

   sealed class Node(CadPolygon poly, CadLayerRole role)
   {
      public CadPolygon Poly { get; } = poly;
      public CadLayerRole Role { get; } = role;
      public Node? Parent { get; set; }
      public Kind Kind { get; set; }

      /// <summary>Область, в которой этот контур вырезает пустоту.</summary>
      public Node? HoleOf { get; set; }
   }

   public static CadRecognitionResult Recognize(CadSnapshot snapshot, CadImportOptions options)
   {
      var result = new CadRecognitionResult();
      double mpu = options.MetersPerUnit;

      var frames = new List<CadPolygon>();
      var nodes = new List<Node>();
      var bars = new List<CadCircle>();
      var labels = new List<CadText>();

      foreach (var p in snapshot.Polygons)
      {
         var role = options.RoleOf(p.Layer);
         if (role == CadLayerRole.Frame) frames.Add(p);
         else if (role is CadLayerRole.Concrete or CadLayerRole.Steel or CadLayerRole.Hole) nodes.Add(new Node(p, role));
      }
      foreach (var c in snapshot.Circles)
      {
         var role = options.RoleOf(c.Layer);
         if (role == CadLayerRole.Rebar) bars.Add(c);
         else if (role is CadLayerRole.Concrete or CadLayerRole.Steel or CadLayerRole.Hole or CadLayerRole.Frame)
         {
            var poly = new CadPolygon(c.Layer, CadGeometry.CirclePoints(c.X, c.Y, c.R, options.Arcs));
            if (role == CadLayerRole.Frame) frames.Add(poly);
            else nodes.Add(new Node(poly, role));
         }
      }
      foreach (var t in snapshot.Texts)
         if (options.RoleOf(t.Layer) == CadLayerRole.Label && !string.IsNullOrWhiteSpace(t.Text))
            labels.Add(t);

      var groups = frames.Count > 0
         ? ByFrames(frames, nodes, bars, labels, result)
         : ByContours(nodes, bars, labels, result);

      var sections = new List<CadSectionCandidate>();
      foreach (var g in groups)
         if (Build(g, options) is { } s)
            sections.Add(s);
      if (frames.Count > 0 && groups.Count > sections.Count)
         result.Warnings.Add($"рамок без контуров бетона или стали: {groups.Count - sections.Count}");

      Order(sections);
      for (int i = 0; i < sections.Count; i++)
         if (sections[i].Names.Count == 0)
            sections[i].Names.Add($"Сеч. {i + 1}");

      if (options.MergeIdentical)
      {
         var bySig = new Dictionary<string, CadSectionCandidate>();
         foreach (var s in sections)
         {
            if (bySig.TryGetValue(s.Signature, out var first))
            {
               first.Names.AddRange(s.Names);
               first.Instances.AddRange(s.Instances);
            }
            else
            {
               bySig[s.Signature] = s;
               result.Sections.Add(s);
            }
         }
      }
      else
         result.Sections.AddRange(sections);
      return result;
   }

   sealed record Group(List<Node> Nodes, List<CadCircle> Bars, List<CadText> Labels, CadBounds? Frame);

   // ── Сегментация ─────────────────────────────────────────────────────────

   static List<Group> ByFrames(List<CadPolygon> frames, List<Node> nodes, List<CadCircle> bars, List<CadText> labels,
                               CadRecognitionResult result)
   {
      // Самая внутренняя рамка — с наименьшей площадью среди содержащих.
      var order = frames.OrderBy(f => Math.Abs(f.Area)).ToList();
      var groups = order.Select(f => new Group([], [], [], f.Bounds)).ToList();

      int? FrameOf(double x, double y, CadBounds b)
      {
         for (int i = 0; i < order.Count; i++)
            if (order[i].Bounds.Contains(b) && CadGeometry.Inside(order[i].Points, x, y))
               return i;
         return null;
      }

      int lostNodes = 0, lostBars = 0;
      foreach (var n in nodes)
      {
         var v = n.Poly.Points[0];
         if (FrameOf(v.X, v.Y, n.Poly.Bounds) is int i) groups[i].Nodes.Add(n);
         else lostNodes++;
      }
      foreach (var c in bars)
      {
         if (FrameOf(c.X, c.Y, c.Bounds) is int i) groups[i].Bars.Add(c);
         else lostBars++;
      }
      foreach (var t in labels)
         if (FrameOf(t.X, t.Y, new CadBounds(t.X, t.Y, t.X, t.Y)) is int i)
            groups[i].Labels.Add(t);

      if (lostNodes > 0) result.Warnings.Add($"контуров вне рамок: {lostNodes}");
      if (lostBars > 0) result.Warnings.Add($"стержней вне рамок: {lostBars}");
      return groups;
   }

   static List<Group> ByContours(List<Node> nodes, List<CadCircle> bars, List<CadText> labels, CadRecognitionResult result)
   {
      Nest(nodes);
      var roots = nodes.Where(n => n.Parent == null && n.Role != CadLayerRole.Hole).ToList();
      int orphanHoles = nodes.Count(n => n.Parent == null && n.Role == CadLayerRole.Hole);
      if (orphanHoles > 0) result.Warnings.Add($"отверстий вне областей: {orphanHoles}");

      var index = roots.Select((r, i) => (r, i)).ToDictionary(x => x.r, x => x.i);
      var groups = roots.Select(r => new Group([], [], [], null)).ToList();
      foreach (var n in nodes)
      {
         var r = n;
         while (r.Parent != null) r = r.Parent;
         if (index.TryGetValue(r, out int gi)) groups[gi].Nodes.Add(n);
      }

      int lost = 0;
      foreach (var c in bars)
      {
         int gi = roots.FindIndex(r => r.Poly.Bounds.Contains(c.X, c.Y) && CadGeometry.Inside(r.Poly.Points, c.X, c.Y));
         if (gi >= 0) groups[gi].Bars.Add(c);
         else lost++;
      }
      if (lost > 0) result.Warnings.Add($"стержней вне контуров: {lost}");

      // Подпись — к ближайшему сечению: внутри габарита или не дальше полутора его размеров.
      foreach (var t in labels)
      {
         int best = -1;
         double bestScore = double.MaxValue;
         for (int i = 0; i < roots.Count; i++)
         {
            var b = roots[i].Poly.Bounds;
            double size = Math.Max(b.Width, b.Height);
            double d = b.DistanceTo(t.X, t.Y);
            if (d > 1.5 * size) continue;
            double score = d / size;
            if (score < bestScore) { bestScore = score; best = i; }
         }
         if (best >= 0) groups[best].Labels.Add(t);
      }
      return groups;
   }

   /// <summary>Дерево вложенности: родитель — наименьший содержащий контур.</summary>
   static void Nest(List<Node> nodes)
   {
      var sorted = nodes.OrderByDescending(n => Math.Abs(n.Poly.Area)).ToList();
      for (int i = 0; i < sorted.Count; i++)
      {
         sorted[i].Parent = null;
         for (int j = i - 1; j >= 0; j--) // от меньших к большим — первый подходящий и есть наименьший
            if (CadGeometry.Contains(sorted[j].Poly, sorted[i].Poly))
            {
               sorted[i].Parent = sorted[j];
               break;
            }
      }
      // Родители идут раньше детей — роли определяются за один проход.
      foreach (var n in sorted)
      {
         n.HoleOf = null;
         var p = n.Parent;
         if (n.Role == CadLayerRole.Hole)
         {
            if (p?.Kind == Kind.Region) { n.Kind = Kind.Hole; n.HoleOf = p; }
            else n.Kind = Kind.Skip;
         }
         else if (p == null || p.Kind != Kind.Region)
            n.Kind = Kind.Region;
         else if (string.Equals(p.Poly.Layer, n.Poly.Layer, StringComparison.OrdinalIgnoreCase))
         {
            n.Kind = Kind.Hole;
            n.HoleOf = p;
         }
         else
         {
            n.Kind = Kind.Region;
            n.HoleOf = p;
         }
      }
   }

   // ── Сборка сечения ──────────────────────────────────────────────────────

   static CadSectionCandidate? Build(Group g, CadImportOptions o)
   {
      Nest(g.Nodes);
      var regionNodes = g.Nodes.Where(n => n.Kind == Kind.Region).ToList();
      if (regionNodes.Count == 0) return null;
      double mpu = o.MetersPerUnit;

      // Начало осей — центр тяжести областей за вычетом пустот (в единицах чертежа).
      double a = 0, sx = 0, sy = 0;
      foreach (var n in g.Nodes)
      {
         // Область +1, пустота −1; сталь в бетоне — и то и другое (0): её площадь уже во внешнем контуре бетона.
         double sign = (n.Kind == Kind.Region ? 1 : 0) - (n.HoleOf != null ? 1 : 0);
         if (sign == 0) continue;
         double ar = Math.Abs(n.Poly.Area);
         var c = CadGeometry.Centroid(n.Poly.Points);
         a += sign * ar; sx += sign * ar * c.X; sy += sign * ar * c.Y;
      }
      var bounds = regionNodes.Select(n => n.Poly.Bounds).Aggregate((x, y) => x.Union(y));
      var origin = a > 1e-12 ? new CadPoint(sx / a, sy / a) : new CadPoint(bounds.CenterX, bounds.CenterY);

      List<CadPoint> Local(CadPolygon p, bool ccw)
      {
         var pts = p.Points.Select(v => new CadPoint((v.X - origin.X) * mpu, (v.Y - origin.Y) * mpu)).ToList();
         if ((p.Area > 0) != ccw) pts.Reverse();
         return pts;
      }

      var s = new CadSectionCandidate { Origin = origin };
      s.Instances.Add(g.Frame ?? bounds);

      // Области: крупные первыми — так стабильнее сигнатура и подписи.
      var map = new Dictionary<Node, CadSectionRegion>();
      foreach (var n in regionNodes.OrderByDescending(n => Math.Abs(n.Poly.Area)))
      {
         var r = new CadSectionRegion { Role = n.Role, Layer = n.Poly.Layer, Outer = Local(n.Poly, ccw: true) };
         map[n] = r;
         s.Regions.Add(r);
         if (CadGeometry.SelfIntersects(n.Poly.Points))
            s.Warnings.Add($"контур на слое «{n.Poly.Layer}» самопересекается");
      }
      foreach (var n in g.Nodes)
         if (n.HoleOf != null && map.TryGetValue(n.HoleOf, out var host))
            host.Holes.Add(Local(n.Poly, ccw: false));
      int skipped = g.Nodes.Count(n => n.Kind == Kind.Skip);
      if (skipped > 0) s.Warnings.Add($"отверстий вне областей: {skipped}");

      // Стержни.
      int outside = 0, crossing = 0, unsnapped = 0;
      foreach (var c in g.Bars)
      {
         double x = (c.X - origin.X) * mpu, y = (c.Y - origin.Y) * mpu, r = c.R * mpu;
         double d = 2 * r;
         if (o.SnapDiameters)
         {
            double mm = d * 1000;
            double near = StandardDiametersMm.MinBy(v => Math.Abs(v - mm));
            if (Math.Abs(near - mm) <= 0.5) d = near / 1000;
            else unsnapped++;
         }
         int? hostIdx = null;
         double hostArea = double.MaxValue;
         for (int i = 0; i < s.Regions.Count; i++)
         {
            var reg = s.Regions[i];
            if (!reg.Contains(x, y)) continue;
            double ar = Math.Abs(CadGeometry.SignedArea(reg.Outer));
            if (ar < hostArea) { hostArea = ar; hostIdx = i; }
         }
         if (hostIdx is int h)
         {
            var reg = s.Regions[h];
            double dist = Math.Min(CadGeometry.DistanceToBoundary(reg.Outer, x, y),
               reg.Holes.Count > 0 ? reg.Holes.Min(hl => CadGeometry.DistanceToBoundary(hl, x, y)) : double.MaxValue);
            if (dist < r * (1 - 1e-6)) crossing++;
         }
         else outside++;
         s.Bars.Add(new CadSectionBar { Layer = c.Layer, X = x, Y = y, DiameterM = d, Host = hostIdx });
      }
      if (outside > 0) s.Warnings.Add($"стержней вне бетона и стали: {outside}");
      if (crossing > 0) s.Warnings.Add($"стержней, пересекающих границу области: {crossing}");
      if (unsnapped > 0) s.Warnings.Add($"диаметров вне сортамента: {unsnapped}");
      int overlaps = 0;
      for (int i = 0; i < s.Bars.Count; i++)
         for (int j = i + 1; j < s.Bars.Count; j++)
         {
            var p = s.Bars[i];
            var q = s.Bars[j];
            double dx = p.X - q.X, dy = p.Y - q.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < (p.DiameterM + q.DiameterM) / 2 * (1 - 1e-6)) overlaps++;
         }
      if (overlaps > 0) s.Warnings.Add($"перекрывающихся стержней: {overlaps}");
      if (s.Bars.Count == 0 && s.Regions.Any(r => r.Role == CadLayerRole.Concrete))
         s.Warnings.Add("нет арматуры");

      // Имя: подпись внутри габарита сечения, затем ближайшая; при равенстве — крупнее.
      if (g.Labels.Count > 0)
      {
         var best = g.Labels
            .OrderBy(t => bounds.Contains(t.X, t.Y) ? 0 : 1)
            .ThenByDescending(t => Math.Round(t.Height, 6))
            .ThenBy(t => bounds.DistanceTo(t.X, t.Y))
            .First();
         s.Names.Add(best.Text.Trim());
      }

      s.Signature = Signature(s, o.QuantumM);
      return s;
   }

   /// <summary>Сверху вниз по рядам, в ряду слева направо.</summary>
   static void Order(List<CadSectionCandidate> sections)
   {
      if (sections.Count < 2) return;
      var heights = sections.Select(s => s.Instances[0].Height).OrderBy(h => h).ToList();
      double tol = heights[heights.Count / 2] / 2;
      var sorted = sections.OrderByDescending(s => s.Instances[0].MaxY).ToList();
      var rows = new List<List<CadSectionCandidate>>();
      foreach (var s in sorted)
      {
         if (rows.Count > 0 && rows[^1][0].Instances[0].MaxY - s.Instances[0].MaxY <= tol) rows[^1].Add(s);
         else rows.Add([s]);
      }
      sections.Clear();
      foreach (var row in rows)
         sections.AddRange(row.OrderBy(s => s.Instances[0].MinX));
   }

   /// <summary>Сигнатура геометрии: квантованные контуры по ролям и слоям, стержни, — без имён и положения на листе.</summary>
   static string Signature(CadSectionCandidate s, double q)
   {
      var sb = new StringBuilder();
      long Q(double v) => (long)Math.Round(v / q);
      void Ring(List<CadPoint> ring)
      {
         // Начало кольца — наименьшая квантованная вершина: сигнатура не зависит от первой вершины полилинии.
         var pts = ring.Select(p => (X: Q(p.X), Y: Q(p.Y))).ToList();
         int start = 0;
         for (int i = 1; i < pts.Count; i++)
            if (pts[i].X < pts[start].X || (pts[i].X == pts[start].X && pts[i].Y < pts[start].Y)) start = i;
         sb.Append('[');
         for (int i = 0; i < pts.Count; i++)
         {
            var p = pts[(start + i) % pts.Count];
            sb.Append(p.X).Append(',').Append(p.Y).Append(';');
         }
         sb.Append(']');
      }
      foreach (var r in s.Regions.OrderBy(r => r.Role).ThenBy(r => r.Layer, StringComparer.OrdinalIgnoreCase)
                                 .ThenByDescending(r => Math.Round(r.Area / (q * q))))
      {
         sb.Append(r.Role).Append('|').Append(r.Layer.ToLowerInvariant());
         Ring(r.Outer);
         foreach (var h in r.Holes.Select(h => (h, k: h.Min(p => (Q(p.X), Q(p.Y))))).OrderBy(x => x.k))
            Ring(h.h);
         sb.Append('/');
      }
      foreach (var b in s.Bars.Select(b => (L: b.Layer.ToLowerInvariant(), X: Q(b.X), Y: Q(b.Y), D: Q(b.DiameterM)))
                              .OrderBy(b => b.X).ThenBy(b => b.Y).ThenBy(b => b.L, StringComparer.Ordinal))
         sb.Append(CultureInfo.InvariantCulture, $"{b.L}:{b.X},{b.Y},{b.D};");
      return sb.ToString();
   }
}
