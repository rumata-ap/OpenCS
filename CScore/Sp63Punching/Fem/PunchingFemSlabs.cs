using CScore.Fem;
using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Sp63Punching.Fem;

/// <summary>Пластина сетки: геометрия, нормаль (ориентирована вверх по Z, у вертикальной — как есть) и центр.</summary>
/// <param name="Flipped">Нормаль обхода контура КЭ противоположна <see cref="Normal"/>.</param>
public sealed record PunchingFemShell(FemElement Element, FemElementGeometry Geometry, PlanarVector3 Normal, PlanarVector3 Centroid, bool Flipped);

/// <summary>
/// Плита — связная по общим рёбрам компонента пластин одной плоскости: нормаль, точка плоскости, КЭ, узлы и петли
/// свободных рёбер (рёбер одного КЭ компоненты) — край и отверстия.
/// </summary>
public sealed class PunchingFemSlab
{
    public int Index { get; init; }
    public required PlanarVector3 Normal { get; init; }
    public required PlanarVector3 Point { get; init; }
    public required IReadOnlyList<PunchingFemShell> Shells { get; init; }
    public required IReadOnlySet<string> NodeTags { get; init; }
    /// <summary>Петли свободных рёбер: теги узлов по порядку (без повтора первого).</summary>
    public required IReadOnlyList<IReadOnlyList<string>> Loops { get; init; }

    /// <summary>Отклонение нормали от вертикали, град.</summary>
    public double TiltDeg => Math.Acos(Math.Clamp(Math.Abs(Normal.Z), 0, 1)) * 180 / Math.PI;

    /// <summary>Расстояние точки до плоскости со знаком (по нормали), м.</summary>
    public double Distance(PlanarVector3 p) => (p - Point).Dot(Normal);
}

/// <summary>Разбиение пластин сетки на плиты (компоненты одной плоскости) и их свободные рёбра.</summary>
public static class PunchingFemSlabs
{
    /// <summary>Косинус наибольшего угла между нормалями соседних КЭ одной плиты (1°).</summary>
    const double CoplanarCos = 0.99985;

    /// <summary>Пластины сетки с геометрией; КЭ с неизвестными узлами пропускаются.</summary>
    public static List<PunchingFemShell> Shells(PunchingFemModel model)
    {
        var list = new List<PunchingFemShell>();
        foreach (var e in model.Elements)
        {
            if (e.ElemType != "shell" || FemElementGeometry.Of(e, model.Nodes) is not { IsShell: true } g) continue;
            PlanarVector3 n;
            try { n = g.ShellFrame().Z; }
            catch (InvalidOperationException) { continue; }
            bool flip = n.Z < -1e-9 || Math.Abs(n.Z) <= 1e-9 && (n.Y < -1e-9 || Math.Abs(n.Y) <= 1e-9 && n.X < 0);
            list.Add(new PunchingFemShell(e, g, flip ? n * -1 : n, g.Centroid, flip));
        }
        return list;
    }

    /// <summary>Плиты: связные по общему ребру пластины с параллельными нормалями и в одной плоскости.</summary>
    public static List<PunchingFemSlab> Build(IReadOnlyList<PunchingFemShell> shells, double planeTol)
    {
        var byEdge = new Dictionary<(string, string), List<int>>();
        for (int i = 0; i < shells.Count; i++)
            foreach (var key in Edges(shells[i].Geometry))
            {
                if (!byEdge.TryGetValue(key, out var l)) byEdge[key] = l = [];
                l.Add(i);
            }

        var comp = Enumerable.Repeat(-1, shells.Count).ToArray();
        var slabs = new List<PunchingFemSlab>();
        for (int s = 0; s < shells.Count; s++)
        {
            if (comp[s] >= 0) continue;
            int id = slabs.Count;
            var members = new List<int> { s };
            comp[s] = id;
            var n0 = shells[s].Normal;
            var p0 = shells[s].Centroid;
            for (int k = 0; k < members.Count; k++)
            {
                var a = shells[members[k]];
                foreach (var key in Edges(a.Geometry))
                    foreach (int j in byEdge[key])
                    {
                        if (comp[j] >= 0) continue;
                        var b = shells[j];
                        if (Math.Abs(b.Normal.Dot(n0)) < CoplanarCos || Math.Abs((b.Centroid - p0).Dot(n0)) > planeTol) continue;
                        comp[j] = id;
                        members.Add(j);
                    }
            }

            var list = members.Select(i => shells[i]).ToList();
            var nodes = list.SelectMany(x => x.Geometry.NodeTags).ToHashSet(StringComparer.Ordinal);
            var counts = new Dictionary<(string, string), int>();
            var ordered = new Dictionary<(string, string), (string A, string B)>();
            foreach (var sh in list)
            {
                var g = sh.Geometry;
                // Обход — против часовой стрелки, если смотреть с конца нормали плиты: внутренность слева.
                bool reverse = sh.Flipped != (sh.Normal.Dot(n0) < 0);
                for (int k = 0; k < g.Contour.Count; k++)
                {
                    string a = g.NodeTags[g.Contour[k]], b = g.NodeTags[g.Contour[(k + 1) % g.Contour.Count]];
                    if (reverse) (a, b) = (b, a);
                    var key = Key(a, b);
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                    ordered.TryAdd(key, (a, b));
                }
            }
            var free = counts.Where(kv => kv.Value == 1).Select(kv => ordered[kv.Key]).ToList();
            var avg = list.Aggregate(PlanarVector3.Zero, (acc, x) => acc + x.Normal * (x.Normal.Dot(n0) >= 0 ? 1 : -1));
            var normal = avg.Length > 1e-12 ? avg.Normalize() : n0;
            slabs.Add(new PunchingFemSlab
            {
                Index = id, Normal = normal, Point = p0, Shells = list, NodeTags = nodes, Loops = Loops(free),
            });
        }
        return slabs;
    }

    static (string, string) Key(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);

    static IEnumerable<(string, string)> Edges(FemElementGeometry g)
    {
        for (int k = 0; k < g.Contour.Count; k++)
            yield return Key(g.NodeTags[g.Contour[k]], g.NodeTags[g.Contour[(k + 1) % g.Contour.Count]]);
    }

    /// <summary>Петли свободных рёбер: обход по смежности; узел-«восьмёрка» (степень 4) разводится по порядку рёбер.</summary>
    static List<IReadOnlyList<string>> Loops(IReadOnlyList<(string A, string B)> edges)
    {
        var next = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < edges.Count; i++)
        {
            if (!next.TryGetValue(edges[i].A, out var l)) next[edges[i].A] = l = [];
            l.Add(i);
        }
        var used = new bool[edges.Count];
        var loops = new List<IReadOnlyList<string>>();
        for (int s = 0; s < edges.Count; s++)
        {
            if (used[s]) continue;
            var loop = new List<string>();
            int cur = s;
            while (!used[cur])
            {
                used[cur] = true;
                loop.Add(edges[cur].A);
                string to = edges[cur].B;
                if (!next.TryGetValue(to, out var outs)) break;
                int nxt = outs.FirstOrDefault(i => !used[i], -1);
                if (nxt < 0) break;
                cur = nxt;
            }
            if (loop.Count >= 3) loops.Add(loop);
        }
        return loops;
    }
}
