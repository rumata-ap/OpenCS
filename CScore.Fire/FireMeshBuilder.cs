using CScore;
using CSTriangulation;
using CSfea.Thermal;
using CSfea.Thermal.Elements;
using Rupp = CSTriangulation.Ruppert;

namespace CScore.Fire;

/// <summary>
/// Построитель тепловой T3-сетки для огневого расчёта из геометрии поперечного сечения.
/// </summary>
public static class FireMeshBuilder
{
    /// <summary>
    /// Построить тепловую сетку, граничные рёбра и привязку арматуры к элементам.
    /// </summary>
    /// <param name="section">Исходное сечение CScore.</param>
    /// <param name="meshStepM">Целевой шаг сетки, м.</param>
    /// <param name="algorithm">Алгоритм триангуляции: <c>ruppert</c> или <c>advancing_front</c>.</param>
    /// <param name="smoothIterTri">Число итераций сглаживания треугольной сетки.</param>
    /// <param name="useQuadratic">Повысить линейную сетку до квадратичной T6.</param>
    public static FireMeshBuildResult Build(
        CrossSection section,
        double meshStepM,
        string algorithm,
        int smoothIterTri,
        bool useQuadratic = false)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (meshStepM <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(meshStepM), "Шаг сетки должен быть больше нуля.");

        string algo = (algorithm ?? "ruppert").Trim().ToLowerInvariant();
        if (algo == "advancing_front")
            throw new NotSupportedException("Алгоритм advancing_front пока не реализован в FireMeshBuilder (MVP: ruppert).");
        if (algo != "ruppert")
            throw new ArgumentException($"Неизвестный алгоритм триангуляции: '{algorithm}'.", nameof(algorithm));

        var concreteArea = GetPrimaryConcreteArea(section);
        var outer = ExtractContourVertices(concreteArea.Hull!);
        var holes = concreteArea.Holes.Select(ExtractContourVertices).ToList();

        var tri = new Rupp.Triangulator();
        tri.SetOuterPolygon(outer.Select(p => new Rupp.Vec2(p.X, p.Y)).ToArray());
        foreach (var hole in holes)
            tri.AddHole(hole.Select(p => new Rupp.Vec2(p.X, p.Y)).ToArray());

        var triParams = new Rupp.TriangulationParams
        {
            MinAngleDeg = 25.0,
            MaxArea = 0.433 * meshStepM * meshStepM,
            DoRefine = true
        };
        var result = tri.Triangulate(triParams);

        var boundaryNodes = new HashSet<int>();
        foreach (var (a, b) in result.ConstrainedEdges)
        {
            boundaryNodes.Add(a);
            boundaryNodes.Add(b);
        }

        var optimized = Optimize.OptimizeTriangular(
            new TriangulationResult
            {
                Nodes = result.Vertices.Select(v => new[] { v.X, v.Y }).ToArray(),
                Triangles = result.Triangles.Select(t => new[] { t.Item1, t.Item2, t.Item3 }).ToArray(),
                IsBoundary = result.Vertices.Select((_, i) => boundaryNodes.Contains(i)).ToArray()
            },
            nIter: Math.Max(0, smoothIterTri),
            chi: 2.0);

        var linearMesh = new HeatMesh(
            optimized.Nodes.Select(n => n[0]).ToArray(),
            optimized.Nodes.Select(n => n[1]).ToArray(),
            optimized.Triangles.Select(t => new[] { t[0], t[1], t[2] }).ToArray());

        HeatMesh mesh = linearMesh;
        if (useQuadratic)
            mesh = HeatMeshQuadratic.Promote(linearMesh);

        var boundaryInfos = BuildBoundaryInfos(mesh, outer, holes, meshStepM);
        var rebars = LocateRebars(section, mesh);

        return new FireMeshBuildResult
        {
            Mesh = mesh,
            LinearMesh = useQuadratic ? linearMesh : null,
            BoundaryEdges = boundaryInfos,
            Rebars = rebars
        };
    }

    private static MaterialArea GetPrimaryConcreteArea(CrossSection section)
    {
        static bool IsPointOnly(MaterialArea area)
        {
            if (area.Fibers.Count == 0)
                return false;
            return area.Fibers.All(f => f.TypeFiber == FiberType.point);
        }

        var preferred = section.Areas.FirstOrDefault(a =>
            a.Hull != null &&
            !IsPointOnly(a) &&
            (a.Category == AreaCategory.Region || a.Material?.Type == MatType.Concrete));
        if (preferred != null)
            return preferred;

        var fallback = section.Areas.FirstOrDefault(a => a.Hull != null && !IsPointOnly(a));
        if (fallback != null)
            return fallback;

        throw new InvalidOperationException("Не найдена основная бетонная область с внешним контуром Hull.");
    }

    private static List<(double X, double Y)> ExtractContourVertices(Contour contour)
    {
        var pts = contour.X.Zip(contour.Y, (x, y) => (X: x, Y: y)).ToList();
        if (pts.Count < 3)
            throw new InvalidOperationException("Контур должен содержать минимум 3 точки.");

        if (pts.Count > 1 && NearlyEqual(pts[0].X, pts[^1].X) && NearlyEqual(pts[0].Y, pts[^1].Y))
            pts.RemoveAt(pts.Count - 1);

        if (pts.Count < 3)
            throw new InvalidOperationException("После удаления замыкающей точки в контуре осталось меньше 3 вершин.");
        return pts;
    }

    private static List<FireBoundaryEdgeInfo> BuildBoundaryInfos(
        HeatMesh mesh,
        IReadOnlyList<(double X, double Y)> outer,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> holes,
        double meshStepM)
    {
        var originalSegments = new List<OriginalSegment>();
        for (int i = 0; i < outer.Count; i++)
        {
            int j = (i + 1) % outer.Count;
            originalSegments.Add(new OriginalSegment(outer[i], outer[j], i, "outer", null));
        }

        for (int h = 0; h < holes.Count; h++)
        {
            var hole = holes[h];
            for (int i = 0; i < hole.Count; i++)
            {
                int j = (i + 1) % hole.Count;
                originalSegments.Add(new OriginalSegment(hole[i], hole[j], i, "hole", h));
            }
        }

        var usage = new Dictionary<(int, int), int>();
        foreach (var tri in mesh.Elements)
        {
            AccumulateEdge(usage, tri[0], tri[1]);
            AccumulateEdge(usage, tri[1], tri[2]);
            AccumulateEdge(usage, tri[2], tri[0]);
        }

        double tol = Math.Max(meshStepM * 1e-4, 1e-9);
        var edges = new List<FireBoundaryEdgeInfo>();
        foreach (var kv in usage.Where(kv => kv.Value == 1))
        {
            int a = kv.Key.Item1;
            int b = kv.Key.Item2;
            var pa = (X: mesh.X[a], Y: mesh.Y[a]);
            var pb = (X: mesh.X[b], Y: mesh.Y[b]);

            OriginalSegment? best = null;
            double bestErr = double.MaxValue;
            foreach (var segment in originalSegments)
            {
                if (!PointOnSegment(pa, segment.A, segment.B, tol) || !PointOnSegment(pb, segment.A, segment.B, tol))
                    continue;

                double err = PointSegmentDistance(pa, segment.A, segment.B) + PointSegmentDistance(pb, segment.A, segment.B);
                if (err < bestErr)
                {
                    bestErr = err;
                    best = segment;
                }
            }

            if (best == null)
                throw new InvalidOperationException("Не удалось сопоставить граничное ребро сетки с исходным контуром.");

            edges.Add(new FireBoundaryEdgeInfo
            {
                NodeA = a,
                NodeB = b,
                LengthM = Distance(pa, pb),
                OriginalEdgeIndex = best.EdgeIndex,
                ContourType = best.ContourType,
                HoleIndex = best.HoleIndex
            });
        }

        return edges;
    }

    private static void AccumulateEdge(Dictionary<(int, int), int> usage, int a, int b)
    {
        var key = a < b ? (a, b) : (b, a);
        usage[key] = usage.TryGetValue(key, out int count) ? count + 1 : 1;
    }

    private static List<FireRebarLocation> LocateRebars(CrossSection section, HeatMesh mesh)
    {
        var points = section.Areas
            .SelectMany(a => a.Fibers)
            .Where(f => f.TypeFiber == FiberType.point)
            .Select((f, id) => (Fiber: f, Id: id))
            .ToList();

        var result = new List<FireRebarLocation>(points.Count);
        foreach (var p in points)
        {
            if (!TryFindContainingElement(mesh, p.Fiber.X, p.Fiber.Y, out int eIdx, out double xi1, out double xi2, out double xi3, out double[]? shapeWeights))
                throw new InvalidOperationException($"Арматурная точка id={p.Id} не попала ни в один элемент.");

            result.Add(new FireRebarLocation
            {
                Id = p.Id,
                X = p.Fiber.X,
                Y = p.Fiber.Y,
                ElementIndex = eIdx,
                Xi1 = xi1,
                Xi2 = xi2,
                Xi3 = xi3,
                ShapeWeights = shapeWeights
            });
        }

        return result;
    }

    private static bool TryFindContainingElement(
        HeatMesh mesh,
        double x,
        double y,
        out int elementIndex,
        out double xi1,
        out double xi2,
        out double xi3,
        out double[]? shapeWeights)
    {
        const double tol = 1e-9;
        for (int e = 0; e < mesh.Elements.Length; e++)
        {
            var tri = mesh.Elements[e];
            var a = (X: mesh.X[tri[0]], Y: mesh.Y[tri[0]]);
            var b = (X: mesh.X[tri[1]], Y: mesh.Y[tri[1]]);
            var c = (X: mesh.X[tri[2]], Y: mesh.Y[tri[2]]);
            if (!TryBarycentric((x, y), a, b, c, out xi1, out xi2, out xi3))
                continue;

            if (xi1 >= -tol && xi2 >= -tol && xi3 >= -tol)
            {
                elementIndex = e;
                if (tri.Length == 6)
                {
                    Span<double> w = stackalloc double[6];
                    HeatTri6.ShapeFunctions(xi1, xi2, w);
                    shapeWeights = w.ToArray();
                }
                else
                    shapeWeights = null;
                return true;
            }
        }

        elementIndex = -1;
        xi1 = xi2 = xi3 = 0.0;
        shapeWeights = null;
        return false;
    }

    private static bool TryBarycentric(
        (double X, double Y) p,
        (double X, double Y) a,
        (double X, double Y) b,
        (double X, double Y) c,
        out double l1,
        out double l2,
        out double l3)
    {
        double det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (Math.Abs(det) < 1e-14)
        {
            l1 = l2 = l3 = 0.0;
            return false;
        }

        l1 = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) / det;
        l2 = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / det;
        l3 = 1.0 - l1 - l2;
        return true;
    }

    private static bool PointOnSegment(
        (double X, double Y) p,
        (double X, double Y) a,
        (double X, double Y) b,
        double tol)
    {
        return PointSegmentDistance(p, a, b) <= tol;
    }

    private static double PointSegmentDistance(
        (double X, double Y) p,
        (double X, double Y) a,
        (double X, double Y) b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-20)
            return Distance(p, a);

        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
        t = Math.Max(0.0, Math.Min(1.0, t));
        double px = a.X + t * dx;
        double py = a.Y + t * dy;
        return Math.Sqrt((p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py));
    }

    private static double Distance((double X, double Y) p1, (double X, double Y) p2)
    {
        double dx = p1.X - p2.X;
        double dy = p1.Y - p2.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool NearlyEqual(double a, double b)
    {
        return Math.Abs(a - b) <= 1e-12;
    }

    private sealed record OriginalSegment(
        (double X, double Y) A,
        (double X, double Y) B,
        int EdgeIndex,
        string ContourType,
        int? HoleIndex);
}
