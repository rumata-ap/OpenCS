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

/// <summary>Построение сетки области через триангуляцию Рупперта.</summary>
public static class MeshBuilder
{
    /// <summary>
    /// Трианглирует область (внешний контур CCW + отверстия), возвращает сетку с
    /// классификацией узлов: OuterDofs (φ=0), HoleNodeSets[k] (φ=c_k).
    ///
    /// Алгоритм: предварительная дискретизация границы с шагом ≤ maxElementSize,
    /// затем Ruppert с MaxArea (без разбиения граничных рёбер).
    /// Это исключает каскад точек Штейнера на криволинейных контурах.
    /// </summary>
    public static TorsionMesh Build(TorsionBoundary boundary, double maxElementSize)
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

        // Ruppert: только внутренние узлы (граница уже подготовлена)
        var parms = new Rupp.TriangulationParams
        {
            MinAngleDeg      = 26.0,
            MaxArea          = maxElementSize * maxElementSize * Math.Sqrt(3.0) / 4.0,
            AllowBoundarySplit = false,
            DoRefine         = true
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
    private static Rupp.Vec2[] DiscretizeLoop(double[] x, double[] y, double maxLen)
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
    private static bool OnPoly(double px, double py, double[] x, double[] y, double tol2)
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
}
