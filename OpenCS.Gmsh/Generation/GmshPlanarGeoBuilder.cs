using System.Globalization;
using System.Text;
using CScore;
using CScore.Planar;

namespace OpenCS.Gmsh.Generation;

/// <summary>Gmsh-геометрия host-региона и карта «линия контура → участок границы».</summary>
public sealed record GmshPlanarGeoPlan(string Geo, IReadOnlyDictionary<int, PlanarBoundaryKey> BoundaryLines);

/// <summary>Детерминированно строит локальную Gmsh-геометрию host-региона и его loci.
///
/// Все отрезки — контура и constraint-ов — предварительно «нодируются»: каждый делится во всех
/// вершинах, лежащих на нём, и в точках взаимного пересечения; совпадающие участки становятся одной
/// линией. Поэтому точка constraint на кромке (колонна у края плиты) делит линию контура, участок
/// кривой по кромке (балка по краю) — это сама линия контура и в поверхность не встраивается, а
/// кривые, пересекающиеся или касающиеся друг друга (сетка балок), имеют общую точку. Для
/// OpenCASCADE точка, линия на границе или пересечение встроенных линий без общей вершины дают
/// «Impossible to recover edge» и пустую сетку. Точки с одинаковыми координатами — одна сущность:
/// копия вершины контура для OCC — другая точка, и сетка вырождается.</summary>
public static class GmshPlanarGeoBuilder
{
    const int OuterPhysicalGroup = 1001;
    const int HolePhysicalGroupBase = 1002;
    const int SurfacePhysicalGroup = 2001;
    const int ConstraintPhysicalGroupBase = 3001;

    /// <summary>Минимальный допуск совпадения точек и принадлежности точки отрезку, м.</summary>
    const double MinToleranceM = 1e-9;

    public static string Build(
        PlanarRegion region,
        PlanarMeshSettings settings,
        IReadOnlyList<PlanarConstraintObject>? constraintObjects = null) =>
        Plan(region, settings, constraintObjects).Geo;

    public static GmshPlanarGeoPlan Plan(
        PlanarRegion region,
        PlanarMeshSettings settings,
        IReadOnlyList<PlanarConstraintObject>? constraintObjects = null)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var constraints = (constraintObjects ?? region.ConstraintObjects)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var tolerance = constraints
            .Select(item => item.ToleranceM)
            .Where(value => double.IsFinite(value) && value > 0)
            .DefaultIfEmpty(MinToleranceM)
            .Max();
        tolerance = Math.Max(tolerance, MinToleranceM);
        var graph = new Graph(tolerance);

        // Контур: вершины получают первые номера, отрезки — ключи участков границы.
        var boundarySegments = new List<Segment>();
        var holeIndex = 0;
        var loops = new List<(ContourType Type, int HoleIndex, List<Segment> Segments)>();
        foreach (var contour in region.Contours)
        {
            var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
            var ids = Enumerable.Range(0, x.Length).Select(i => graph.Vertex(x[i], y[i])).ToArray();
            var loopKind = contour.Type == ContourType.Hull ? BoundaryLoop.Outer : BoundaryLoop.Hole;
            var currentHole = loopKind == BoundaryLoop.Hole ? holeIndex++ : 0;
            var segments = new List<Segment>();
            for (var i = 0; i < ids.Length; i++)
            {
                var segment = new Segment(ids[i], ids[(i + 1) % ids.Length])
                {
                    BoundaryKey = new PlanarBoundaryKey(loopKind, currentHole, i, (i + 1) % ids.Length)
                };
                segments.Add(segment);
            }
            boundarySegments.AddRange(segments);
            loops.Add((contour.Type, currentHole, segments));
        }

        // Constraint-ы: точки, ломаные кривых, замкнутые ломаные областей.
        var constraintSegments = new List<Segment>();
        var shapes = new List<ConstraintShape>();
        foreach (var constraint in constraints)
        {
            var points = constraint.Geometry.Points;
            var ids = points.Select(p => graph.Vertex(p.U, p.V)).ToArray();
            var segments = new List<Segment>();
            if (constraint.Geometry.Kind == PlanarConstraintGeometryKind.Curve)
                for (var i = 0; i + 1 < ids.Length; i++)
                    segments.Add(new Segment(ids[i], ids[i + 1]));
            else if (constraint.Geometry.Kind == PlanarConstraintGeometryKind.Region)
                for (var i = 0; i < ids.Length; i++)
                    segments.Add(new Segment(ids[i], ids[(i + 1) % ids.Length]));
            segments.RemoveAll(segment => segment.A == segment.B);
            constraintSegments.AddRange(segments);
            shapes.Add(new ConstraintShape(constraint, ids, segments));
        }

        // Точки взаимного пересечения отрезков constraint-ов между собой и с контуром.
        var all = boundarySegments.Concat(constraintSegments).ToArray();
        for (var i = 0; i < constraintSegments.Count; i++)
        {
            var s = constraintSegments[i];
            foreach (var other in boundarySegments.Concat(constraintSegments.Skip(i + 1)))
                if (graph.ProperIntersection(s, other) is { } p)
                    graph.Vertex(p.U, p.V);
        }

        // Деление каждого отрезка всеми вершинами, лежащими на нём.
        foreach (var segment in all)
            segment.Chain = graph.Split(segment);

        // Линии: сначала участки контура (по порядку контуров), затем constraint-ов.
        var lines = new LineRegistry();
        foreach (var segment in boundarySegments)
            segment.Lines = lines.Register(segment.Chain, segment.BoundaryKey);
        foreach (var segment in constraintSegments)
            segment.Lines = lines.Register(segment.Chain, null);

        var result = new StringBuilder();
        result.AppendLine("SetFactory(\"OpenCASCADE\");");
        result.AppendLine("Mesh.ElementOrder = 1;");
        result.AppendLine($"Mesh.Algorithm = {settings.Algorithm};");
        result.AppendLine($"Mesh.CharacteristicLengthMax = {Fmt(settings.MaxElementSizeM)};");
        if (settings.ElementMode is PlanarMeshElementMode.Quads or PlanarMeshElementMode.Mixed)
            result.AppendLine("Mesh.RecombineAll = 1;");

        foreach (var vertex in graph.Vertices)
            result.AppendLine($"Point({vertex.Id}) = {{{Fmt(vertex.U)}, {Fmt(vertex.V)}, 0, {Fmt(settings.MaxElementSizeM)}}};");
        foreach (var line in lines.All)
            result.AppendLine($"Line({line.Id}) = {{{line.A}, {line.B}}};");

        var claimed = new HashSet<int>();
        var boundaryVertices = new HashSet<int>();
        var loopId = 1;
        foreach (var (type, hole, segments) in loops)
        {
            var signed = segments.SelectMany(segment => segment.Lines).ToArray();
            result.AppendLine($"Curve Loop({loopId++}) = {{{string.Join(", ", signed)}}};");
            var physical = type == ContourType.Hull ? OuterPhysicalGroup : HolePhysicalGroupBase + hole;
            var physicalName = type == ContourType.Hull ? "host:outer" : $"host:hole:{hole}";
            result.AppendLine($"Physical Curve(\"{physicalName}\", {physical}) = {{{string.Join(", ", signed.Select(Math.Abs))}}};");
            foreach (var segment in segments)
                foreach (var vertex in segment.Chain)
                    boundaryVertices.Add(vertex);
            foreach (var id in signed) claimed.Add(Math.Abs(id));
        }

        result.AppendLine($"Plane Surface(1) = {{{string.Join(", ", Enumerable.Range(1, loops.Count))}}};");
        result.AppendLine($"Physical Surface(\"host:surface\", {SurfacePhysicalGroup}) = {{1}};");

        var embeddedLines = new HashSet<int>(claimed);
        var embeddedPoints = new HashSet<int>(boundaryVertices);
        var physicalGroup = ConstraintPhysicalGroupBase;
        foreach (var shape in shapes)
        {
            var constraint = shape.Constraint;
            var group = physicalGroup++;
            var name = SafeName(constraint.Id);
            switch (constraint.Geometry.Kind)
            {
                case PlanarConstraintGeometryKind.Point:
                {
                    var id = shape.Vertices[0];
                    // Вершина контура уже принадлежит границе поверхности — повторно не встраивается.
                    if (embeddedPoints.Add(id))
                        result.AppendLine($"Point {{{id}}} In Surface {{1}};");
                    result.AppendLine($"Physical Point(\"constraint:{name}:point\", {group}) = {{{id}}};");
                    break;
                }
                case PlanarConstraintGeometryKind.Curve:
                case PlanarConstraintGeometryKind.Region:
                {
                    var signed = shape.Segments.SelectMany(segment => segment.Lines).ToArray();
                    var ids = signed.Select(Math.Abs).Distinct().ToArray();
                    var embed = ids.Where(embeddedLines.Add).ToArray();
                    if (embed.Length > 0)
                        result.AppendLine($"Line {{{string.Join(", ", embed)}}} In Surface {{1}};");
                    if (constraint.KeepVertices && ids.Length > 0)
                        result.AppendLine($"Transfinite Curve {{{string.Join(", ", ids)}}} = 2;");
                    if (constraint.Geometry.Kind == PlanarConstraintGeometryKind.Region && signed.Length > 0)
                        result.AppendLine($"Curve Loop({loopId++}) = {{{string.Join(", ", signed)}}};");
                    // Gmsh пишет элементы линии один раз, под первой физической группой: общие с контуром
                    // или с другим constraint-ом линии в группу не входят, их сопоставление — геометрическое.
                    var own = ids.Where(claimed.Add).ToArray();
                    var kind = constraint.Geometry.Kind == PlanarConstraintGeometryKind.Curve ? "curve" : "region";
                    if (own.Length > 0)
                        result.AppendLine($"Physical Curve(\"constraint:{name}:{kind}\", {group}) = {{{string.Join(", ", own)}}};");
                    break;
                }
            }
        }

        var boundaryLines = new Dictionary<int, PlanarBoundaryKey>();
        foreach (var segment in boundarySegments)
            foreach (var id in segment.Lines)
                boundaryLines[Math.Abs(id)] = segment.BoundaryKey!;
        return new GmshPlanarGeoPlan(result.ToString(), boundaryLines);
    }

    static string SafeName(string value) => value.Replace("\"", "_");
    static string Fmt(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    sealed record Vertex(int Id, double U, double V);

    sealed record ConstraintShape(PlanarConstraintObject Constraint, int[] Vertices, List<Segment> Segments);

    /// <summary>Отрезок контура или constraint-а; после нодирования — цепочка вершин и знаковые линии.</summary>
    sealed class Segment(int a, int b)
    {
        public int A { get; } = a;
        public int B { get; } = b;
        public PlanarBoundaryKey? BoundaryKey { get; init; }
        public int[] Chain { get; set; } = [];
        public int[] Lines { get; set; } = [];
    }

    sealed class Graph(double tolerance)
    {
        readonly List<Vertex> _vertices = [];

        public IReadOnlyList<Vertex> Vertices => _vertices;

        /// <summary>Номер вершины: существующей при совпадении координат, иначе новой.</summary>
        public int Vertex(double u, double v)
        {
            foreach (var vertex in _vertices)
                if (Math.Abs(vertex.U - u) <= tolerance && Math.Abs(vertex.V - v) <= tolerance)
                    return vertex.Id;
            var created = new Vertex(_vertices.Count + 1, u, v);
            _vertices.Add(created);
            return created.Id;
        }

        Vertex At(int id) => _vertices[id - 1];

        /// <summary>Точка пересечения внутренностей двух непараллельных отрезков; касание и
        /// наложение — null (их обрабатывает деление вершинами).</summary>
        public (double U, double V)? ProperIntersection(Segment first, Segment second)
        {
            var a = At(first.A); var b = At(first.B);
            var c = At(second.A); var d = At(second.B);
            double rx = b.U - a.U, ry = b.V - a.V, sx = d.U - c.U, sy = d.V - c.V;
            var denominator = rx * sy - ry * sx;
            var lengths = Math.Sqrt(rx * rx + ry * ry) * Math.Sqrt(sx * sx + sy * sy);
            if (lengths == 0 || Math.Abs(denominator) <= 1e-12 * lengths) return null;
            double qx = c.U - a.U, qy = c.V - a.V;
            var t = (qx * sy - qy * sx) / denominator;
            var u = (qx * ry - qy * rx) / denominator;
            var lengthFirst = Math.Sqrt(rx * rx + ry * ry);
            var lengthSecond = Math.Sqrt(sx * sx + sy * sy);
            if (t * lengthFirst <= tolerance || (1 - t) * lengthFirst <= tolerance ||
                u * lengthSecond <= tolerance || (1 - u) * lengthSecond <= tolerance) return null;
            return (a.U + t * rx, a.V + t * ry);
        }

        /// <summary>Вершины на отрезке (концы и внутренние в пределах допуска) по порядку от A к B.</summary>
        public int[] Split(Segment segment)
        {
            var a = At(segment.A); var b = At(segment.B);
            double rx = b.U - a.U, ry = b.V - a.V;
            var length = Math.Sqrt(rx * rx + ry * ry);
            var inner = new List<(double T, int Id)>();
            foreach (var vertex in _vertices)
            {
                if (vertex.Id == segment.A || vertex.Id == segment.B) continue;
                double px = vertex.U - a.U, py = vertex.V - a.V;
                var t = (px * rx + py * ry) / (length * length);
                if (t * length <= tolerance || (1 - t) * length <= tolerance) continue;
                var distance = Math.Abs(px * ry - py * rx) / length;
                if (distance <= tolerance) inner.Add((t, vertex.Id));
            }
            return [segment.A, .. inner.OrderBy(item => item.T).Select(item => item.Id), segment.B];
        }
    }

    sealed record Line(int Id, int A, int B);

    /// <summary>Линии по неупорядоченной паре вершин: совпадающие участки разных отрезков — одна линия.</summary>
    sealed class LineRegistry
    {
        readonly Dictionary<(int, int), Line> _byPair = [];
        readonly List<Line> _all = [];

        public IReadOnlyList<Line> All => _all;

        /// <summary>Знаковые номера линий цепочки (минус — линия проходится от B к A).</summary>
        public int[] Register(IReadOnlyList<int> chain, PlanarBoundaryKey? key)
        {
            var result = new int[chain.Count - 1];
            for (var i = 0; i + 1 < chain.Count; i++)
            {
                int a = chain[i], b = chain[i + 1];
                var pair = a < b ? (a, b) : (b, a);
                if (!_byPair.TryGetValue(pair, out var line))
                {
                    line = new Line(_all.Count + 1, a, b);
                    _byPair[pair] = line;
                    _all.Add(line);
                }
                result[i] = line.A == a ? line.Id : -line.Id;
            }
            return result;
        }
    }
}
