using CSTriangulation;
using Rupp = CSTriangulation.Ruppert;

namespace CSfea.Torsion;

/// <summary>Результат построения сетки для МКЭ-кручения.</summary>
public sealed class TorsionMesh
{
    public double[] NodesX { get; init; } = [];
    public double[] NodesY { get; init; } = [];
    /// <summary>Треугольники: [i0, i1, i2] на элемент.</summary>
    public int[][] Triangles { get; init; } = [];
    /// <summary>Узлы внешнего контура: φ=0 (Дирихле).</summary>
    public int[] OuterDofs { get; init; } = [];
    /// <summary>Узлы k-го отверстия: φ=c_k (неизвестная константа Прандтля).</summary>
    public int[][] HoleNodeSets { get; init; } = [];
    /// <summary>Все узлы внешнего контура (псевдоним OuterDofs для совместимости).</summary>
    public int[] FixedDofs => OuterDofs;
}

/// <summary>Построение сетки области через триангуляцию (AdvancingFront или Ruppert).</summary>
public static class MeshBuilder
{
    /// <summary>
    /// Трианглирует область (внешний контур CCW + отверстия), возвращает сетку с
    /// классификацией узлов: OuterDofs (φ=0), HoleNodeSets[k] (φ=c_k).
    /// </summary>
    public static TorsionMesh Build(TorsionBoundary boundary, double maxElementSize,
        TriangulationMethod method = TriangulationMethod.AdvancingFront)
    {
        return method switch
        {
            TriangulationMethod.AdvancingFront => BuildAdvancingFront(boundary, maxElementSize),
            TriangulationMethod.Ruppert => BuildRuppert(boundary, maxElementSize),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
    }

    /// <summary>
    /// Повышает линейную (T3) сетку до квадратичной (T6): для каждого треугольника добавляет
    /// 3 узла-середины рёбер (дедуп по общим рёбрам соседних треугольников). Новые серединные
    /// узлы на границе классифицируются в OuterDofs/HoleNodeSets той же геометрической проверкой
    /// (OnPoly), что и угловые узлы — без отслеживания топологии триангуляции.
    /// </summary>
    public static TorsionMesh Promote(TorsionMesh linear, TorsionBoundary boundary)
    {
        foreach (var tri in linear.Triangles)
        {
            if (tri.Length != 3)
                throw new ArgumentException("Promote ожидает линейную (3-узловую) сетку.", nameof(linear));
        }

        var x = linear.NodesX.ToList();
        var y = linear.NodesY.ToList();
        var midIndex = new Dictionary<(int, int), int>();

        int MidNode(int a, int b)
        {
            int i = Math.Min(a, b);
            int j = Math.Max(a, b);
            if (!midIndex.TryGetValue((i, j), out int mid))
            {
                mid = x.Count;
                midIndex[(i, j)] = mid;
                x.Add(0.5 * (linear.NodesX[i] + linear.NodesX[j]));
                y.Add(0.5 * (linear.NodesY[i] + linear.NodesY[j]));
            }
            return mid;
        }

        var triangles = new int[linear.Triangles.Length][];
        for (int e = 0; e < linear.Triangles.Length; e++)
        {
            int n0 = linear.Triangles[e][0];
            int n1 = linear.Triangles[e][1];
            int n2 = linear.Triangles[e][2];
            triangles[e] = [n0, n1, n2, MidNode(n0, n1), MidNode(n1, n2), MidNode(n2, n0)];
        }

        double[] nx = [.. x];
        double[] ny = [.. y];

        double minX = nx.Min(), maxX = nx.Max();
        double minY = ny.Min(), maxY = ny.Max();
        double scale = Math.Max(Math.Max(maxX - minX, maxY - minY), 1e-12);
        double tol2 = Math.Pow(scale * 1e-6, 2.0);

        int nHoles = boundary.Holes?.Count ?? 0;
        var outerSet = new HashSet<int>(linear.OuterDofs);
        var holeSets = new HashSet<int>[nHoles];
        for (int k = 0; k < nHoles; k++) holeSets[k] = new HashSet<int>(linear.HoleNodeSets[k]);

        for (int mid = linear.NodesX.Length; mid < nx.Length; mid++)
        {
            double px = nx[mid], py = ny[mid];
            if (OnPoly(px, py, boundary.OuterX, boundary.OuterY, tol2))
            {
                outerSet.Add(mid);
                continue;
            }
            if (boundary.Holes == null) continue;
            for (int k = 0; k < nHoles; k++)
            {
                if (OnPoly(px, py, boundary.Holes[k].X, boundary.Holes[k].Y, tol2))
                {
                    holeSets[k].Add(mid);
                    break;
                }
            }
        }

        return new TorsionMesh
        {
            NodesX = nx,
            NodesY = ny,
            Triangles = triangles,
            OuterDofs = outerSet.Order().ToArray(),
            HoleNodeSets = Array.ConvertAll(holeSets, h => h.Order().ToArray()),
        };
    }

    /// <summary>Построение сетки методом продвижения фронта.</summary>
    static TorsionMesh BuildAdvancingFront(TorsionBoundary boundary, double maxElementSize)
    {
        Rupp.Vec2[] outerRaw = DiscretizeLoop(boundary.OuterX, boundary.OuterY, maxElementSize);
        var outerLoop = Vec2ToAFLoop(DeduplicateLoop(outerRaw), LoopKind.Hull, maxElementSize);
        List<ContourLoop>? holeLoops = null;
        if (boundary.Holes is { Count: > 0 })
        {
            holeLoops = new List<ContourLoop>();
            for (int k = 0; k < boundary.Holes.Count; k++)
            {
                var (hx, hy) = boundary.Holes[k];
                Rupp.Vec2[] holeRaw = DiscretizeLoop(hx, hy, maxElementSize);
                holeLoops.Add(Vec2ToAFLoop(DeduplicateLoop(holeRaw), LoopKind.Hole, maxElementSize));
            }
        }

        var afInput = new AdvancingFrontInput { Outer = outerLoop, Holes = holeLoops ?? new() };
        var afResult = AdvancingFront.Triangulate(afInput, 90.0);

        int n = afResult.Nodes.Length;
        var nx = new double[n];
        var ny = new double[n];
        for (int i = 0; i < n; i++) { nx[i] = afResult.Nodes[i][0]; ny[i] = afResult.Nodes[i][1]; }

        var tris = afResult.Triangles;

        var outerList = new List<int>();
        var holeLists = new List<int>[boundary.Holes?.Count ?? 0];
        for (int k = 0; k < holeLists.Length; k++) holeLists[k] = new List<int>();

        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            if (nx[i] < minX) minX = nx[i]; if (nx[i] > maxX) maxX = nx[i];
            if (ny[i] < minY) minY = ny[i]; if (ny[i] > maxY) maxY = ny[i];
        }
        double L = Math.Max(maxX - minX, maxY - minY);
        double tol2 = Math.Pow(Math.Max(L, 1e-12) * 1e-6, 2.0);

        for (int i = 0; i < n; i++)
        {
            if (!afResult.IsBoundary[i]) continue;
            if (OnPoly(nx[i], ny[i], boundary.OuterX, boundary.OuterY, tol2))
                outerList.Add(i);
            else if (boundary.Holes != null)
            {
                for (int k = 0; k < boundary.Holes.Count; k++)
                {
                    if (OnPoly(nx[i], ny[i], boundary.Holes[k].X, boundary.Holes[k].Y, tol2))
                    {
                        holeLists[k].Add(i);
                        break;
                    }
                }
            }
        }

        return new TorsionMesh
        {
            NodesX = nx, NodesY = ny, Triangles = tris,
            OuterDofs = outerList.Order().ToArray(),
            HoleNodeSets = Array.ConvertAll(holeLists, h => h.Order().ToArray()),
        };
    }

    /// <summary>Удаляет последовательные дубликаты из массива точек контура.</summary>
    static Rupp.Vec2[] DeduplicateLoop(Rupp.Vec2[] pts)
    {
        if (pts.Length <= 2) return pts;
        var result = new List<Rupp.Vec2>(pts.Length);
        result.Add(pts[0]);
        for (int i = 1; i < pts.Length; i++)
        {
            double dx = pts[i].X - pts[i - 1].X, dy = pts[i].Y - pts[i - 1].Y;
            if (dx * dx + dy * dy > 1e-20)
                result.Add(pts[i]);
        }
        if (result.Count >= 2)
        {
            var first = result[0];
            var last = result[^1];
            double dx = last.X - first.X, dy = last.Y - first.Y;
            if (dx * dx + dy * dy <= 1e-20)
                result.RemoveAt(result.Count - 1);
        }
        return result.Count >= 3 ? result.ToArray() : pts;
    }

    /// <summary>Строит ContourLoop из предварительно дискретизированных точек.</summary>
    static ContourLoop Vec2ToAFLoop(Rupp.Vec2[] pts, LoopKind kind, double h)
    {
        int n = pts.Length;
        var nodes = new ContourNode[n];
        for (int i = 0; i < n; i++)
            nodes[i] = new ContourNode(pts[i].X, pts[i].Y, h);

        var faces = new List<ContourFace>(n);
        for (int i = 0; i < n; i++)
            faces.Add(ContourFace.Linear(nodes[i], nodes[(i + 1) % n]));

        return new ContourLoop(kind, faces);
    }

    /// <summary>Построение сетки методом Рупперта (CDT + рефайнмент).</summary>
    static TorsionMesh BuildRuppert(TorsionBoundary boundary, double maxElementSize)
    {
        // Предварительная дискретизация: разбить рёбра до ≤ maxElementSize
        Rupp.Vec2[] outerPts = DiscretizeLoop(boundary.OuterX, boundary.OuterY, maxElementSize);

        Rupp.Vec2[][]? holes = null;
        if (boundary.Holes is { Count: > 0 })
        {
            holes = new Rupp.Vec2[boundary.Holes.Count][];
            for (int k = 0; k < boundary.Holes.Count; k++)
            {
                var (hx, hy) = boundary.Holes[k];
                holes[k] = DiscretizeLoop(hx, hy, maxElementSize);
            }
        }

        // Рефайнмент только по площади: граница не дробится (AllowBoundarySplit=false),
        // угловой критерий приводил к сотням тысяч точек Штейнера на вогнутых контурах.
        double triArea = maxElementSize * maxElementSize * Math.Sqrt(3.0) / 4.0;
        var parms = new Rupp.TriangulationParams
        {
            MinAngleDeg        = 0.0,
            MaxArea            = triArea,
            AllowBoundarySplit = false,
            DoRefine           = true,
            MaxSteinerPoints   = EstimateMaxSteiner(boundary, triArea),
        };
        Rupp.RuppertResult result = Rupp.Triangulator.FromPolygon(outerPts, holes, parms);

        int n = result.Vertices.Length;
        var nx = new double[n];
        var ny = new double[n];
        for (int i = 0; i < n; i++) { nx[i] = result.Vertices[i].X; ny[i] = result.Vertices[i].Y; }

        var tris = new int[result.Triangles.Length][];
        for (int t = 0; t < tris.Length; t++)
            tris[t] = [result.Triangles[t].Item1, result.Triangles[t].Item2, result.Triangles[t].Item3];

        // Граничные узлы из constrained edges
        var boundarySet = new HashSet<int>();
        foreach (var (a, b) in result.ConstrainedEdges) { boundarySet.Add(a); boundarySet.Add(b); }

        // Tolerance для proximity-проверки (1e-6 × характерный размер)
        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            if (nx[i] < minX) minX = nx[i]; if (nx[i] > maxX) maxX = nx[i];
            if (ny[i] < minY) minY = ny[i]; if (ny[i] > maxY) maxY = ny[i];
        }
        double L = Math.Max(maxX - minX, maxY - minY);
        double tol2 = Math.Pow(Math.Max(L, 1e-12) * 1e-6, 2.0);

        // Классифицировать граничные узлы: внешний контур vs k-е отверстие
        int nHoles = boundary.Holes?.Count ?? 0;
        var outerList = new List<int>();
        var holeLists = new List<int>[nHoles];
        for (int k = 0; k < nHoles; k++) holeLists[k] = new List<int>();

        foreach (int i in boundarySet)
        {
            double px = nx[i], py = ny[i];
            if (OnPoly(px, py, boundary.OuterX, boundary.OuterY, tol2))
            {
                outerList.Add(i);
            }
            else if (boundary.Holes != null)
            {
                for (int k = 0; k < nHoles; k++)
                {
                    if (OnPoly(px, py, boundary.Holes[k].X, boundary.Holes[k].Y, tol2))
                    {
                        holeLists[k].Add(i);
                        break;
                    }
                }
            }
        }

        return new TorsionMesh
        {
            NodesX       = nx,
            NodesY       = ny,
            Triangles    = tris,
            OuterDofs    = outerList.Order().ToArray(),
            HoleNodeSets = Array.ConvertAll(holeLists, h => h.Order().ToArray()),
        };
    }

    /// <summary>
    /// Разбивает замкнутый контур x[]/y[] на отрезки длиной ≤ maxLen.
    /// Исходные вершины сохраняются.
    /// </summary>
    static Rupp.Vec2[] DiscretizeLoop(double[] x, double[] y, double maxLen)
    {
        var pts = new List<Rupp.Vec2>(x.Length * 2);
        int n = x.Length;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            double dx = x[j] - x[i], dy = y[j] - y[i];
            double len = Math.Sqrt(dx * dx + dy * dy);
            int m = Math.Max(1, (int)Math.Ceiling(len / maxLen));
            for (int k = 0; k < m; k++)
            {
                double t = (double)k / m;
                pts.Add(new Rupp.Vec2(x[i] + t * dx, y[i] + t * dy));
            }
        }
        return pts.ToArray();
    }

    /// <summary>True если (px, py) лежит на ребре замкнутого полигона x[]/y[].</summary>
    static bool OnPoly(double px, double py, double[] x, double[] y, double tol2)
    {
        int m = x.Length;
        for (int i = 0; i < m; i++)
        {
            int j = (i + 1) % m;
            double ax = x[i], ay = y[i];
            double bx = x[j], by = y[j];
            double abx = bx - ax, aby = by - ay;
            double ab2 = abx * abx + aby * aby;
            if (ab2 < 1e-20) continue;
            double t = ((px - ax) * abx + (py - ay) * aby) / ab2;
            if (t < -1e-9 || t > 1.0 + 1e-9) continue;
            double dx = px - ax - t * abx;
            double dy = py - ay - t * aby;
            if (dx * dx + dy * dy <= tol2) return true;
        }
        return false;
    }

    /// <summary>Оценка лимита точек Штейнера по площади bbox и целевому размеру элемента.</summary>
    static int EstimateMaxSteiner(TorsionBoundary boundary, double triArea)
    {
        double minX = boundary.OuterX.Min(), maxX = boundary.OuterX.Max();
        double minY = boundary.OuterY.Min(), maxY = boundary.OuterY.Max();
        double bboxArea = Math.Max((maxX - minX) * (maxY - minY), 1e-12);
        int est = (int)Math.Ceiling(bboxArea / Math.Max(triArea, 1e-18) * 4.0);
        return Math.Clamp(est, 500, 20_000);
    }
}
