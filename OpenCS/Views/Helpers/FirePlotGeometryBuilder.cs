using CScore.Fire;
using CSfea.Thermal;
using System.Windows;

namespace OpenCS.Views.Helpers;

/// <summary>Контуры сечения и рёбра T3-сетки для наложения на карту.</summary>
public static class FirePlotGeometryBuilder
{
    const double MmPerM = 1000.0;

    /// <summary>Замкнутые полилинии внешнего контура и отверстий (мм).</summary>
    public static IReadOnlyList<FireSectionContourDraw> BuildSectionContours(FireMeshBuildResult meshInfo)
    {
        var mesh = meshInfo.Mesh;
        var groups = meshInfo.BoundaryEdges
            .GroupBy(e => (Type: NormContourType(e.ContourType), e.HoleIndex));

        var result = new List<FireSectionContourDraw>();
        foreach (var g in groups)
        {
            var edgePairs = g.Select(e => (e.NodeA, e.NodeB)).ToList();
            foreach (var loop in ChainBoundaryLoops(edgePairs))
            {
                if (loop.Count < 2)
                    continue;

                var pts = new Point[loop.Count];
                for (int i = 0; i < loop.Count; i++)
                {
                    int ni = loop[i];
                    pts[i] = new Point(mesh.X[ni] * MmPerM, mesh.Y[ni] * MmPerM);
                }

                bool isHole = g.Key.Type == "hole";
                result.Add(new FireSectionContourDraw(pts, isHole));
            }
        }

        return result;
    }

    /// <summary>Рёбра T3-элементов (мм), без дубликатов.</summary>
    public static IReadOnlyList<FireMeshEdgeDraw> BuildMeshEdges(HeatMesh mesh)
    {
        var seen = new HashSet<(int, int)>();
        var edges = new List<FireMeshEdgeDraw>();

        foreach (var el in mesh.Elements)
        {
            if (el.Length != 3)
                continue;

            AddEdge(el[0], el[1]);
            AddEdge(el[1], el[2]);
            AddEdge(el[2], el[0]);
        }

        return edges;

        void AddEdge(int a, int b)
        {
            if (a == b)
                return;
            var key = a < b ? (a, b) : (b, a);
            if (!seen.Add(key))
                return;

            edges.Add(new FireMeshEdgeDraw(
                new Point(mesh.X[a] * MmPerM, mesh.Y[a] * MmPerM),
                new Point(mesh.X[b] * MmPerM, mesh.Y[b] * MmPerM)));
        }
    }

    static string NormContourType(string? t)
        => string.Equals(t?.Trim(), "hole", StringComparison.OrdinalIgnoreCase) ? "hole" : "outer";

    static List<List<int>> ChainBoundaryLoops(List<(int A, int B)> edges)
    {
        if (edges.Count == 0)
            return [];

        var adj = new Dictionary<int, List<int>>();
        foreach (var (a, b) in edges)
        {
            AddAdj(a, b);
            AddAdj(b, a);
        }

        var used = new HashSet<(int, int)>();
        var loops = new List<List<int>>();

        foreach (var (a, b) in edges)
        {
            if (used.Contains(EdgeKey(a, b)))
                continue;

            var loop = WalkLoop(a, b, adj, used);
            if (loop.Count >= 3)
                loops.Add(loop);
        }

        return loops;

        void AddAdj(int u, int v)
        {
            if (!adj.TryGetValue(u, out var list))
            {
                list = [];
                adj[u] = list;
            }
            if (!list.Contains(v))
                list.Add(v);
        }
    }

    static List<int> WalkLoop(
        int startA, int startB,
        Dictionary<int, List<int>> adj,
        HashSet<(int, int)> used)
    {
        var loop = new List<int> { startA };
        MarkUsed(startA, startB);
        int prev = startA;
        int cur = startB;

        while (cur != startA && loop.Count <= adj.Count + 2)
        {
            loop.Add(cur);
            int? next = null;
            if (adj.TryGetValue(cur, out var nbrs))
            {
                foreach (int n in nbrs)
                {
                    if (n == prev)
                        continue;
                    if (!used.Contains(EdgeKey(cur, n)))
                    {
                        next = n;
                        break;
                    }
                }
            }

            if (next is null)
                break;

            MarkUsed(cur, next.Value);
            prev = cur;
            cur = next.Value;
        }

        return loop;

        void MarkUsed(int u, int v)
        {
            used.Add(EdgeKey(u, v));
            used.Add(EdgeKey(v, u));
        }
    }

    static (int, int) EdgeKey(int a, int b) => a < b ? (a, b) : (b, a);
}

/// <summary>Замкнутый контур сечения на карте.</summary>
public sealed record FireSectionContourDraw(IReadOnlyList<Point> PointsMm, bool IsHole);

/// <summary>Ребро T3-сетки на карте.</summary>
public sealed record FireMeshEdgeDraw(Point AMm, Point BMm);
