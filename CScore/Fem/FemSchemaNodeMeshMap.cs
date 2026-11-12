using System.Globalization;

namespace CScore.Fem;

/// <summary>
/// Узел схемы → узлы сетки: по <see cref="FemMeshNode.SourceNodeTag"/>, иначе ближайший узел сетки не дальше
/// <see cref="FemMeshDiscretizer.CollinearToleranceM"/>. Сетка сливает точки ближе допуска в один узел, а у него один
/// источник: узел схемы рядом с другим (свободный узел с нагрузкой у узла КонЭ, опора в 0,3 мм от угла плиты) своего
/// узла сетки не получает — без запасного пути его опора, пружина, нагрузка терялись бы. Сопоставленные по положению
/// копятся в <see cref="ByPosition"/> для информационного сообщения.
/// </summary>
public sealed class FemSchemaNodeMeshMap
{
    const double Tolerance = FemMeshDiscretizer.CollinearToleranceM;

    readonly IReadOnlyList<FemMeshNode> _meshNodes;
    readonly ILookup<string, string> _bySource;
    readonly Dictionary<string, FemNode> _nodes = new(StringComparer.Ordinal);
    Dictionary<(long, long, long), List<FemMeshNode>>? _grid;

    public FemSchemaNodeMeshMap(IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(meshNodes);
        ArgumentNullException.ThrowIfNull(nodes);
        _meshNodes = meshNodes;
        _bySource = meshNodes.Where(n => n.SourceNodeTag != null).ToLookup(n => n.SourceNodeTag!, n => n.NodeTag, StringComparer.Ordinal);
        foreach (var n in nodes) _nodes.TryAdd(n.NodeTag, n);
    }

    /// <summary>Сопоставленные по положению: тег узла схемы, тег узла сетки, расстояние, м.</summary>
    public List<(string NodeTag, string MeshNodeTag, double DistanceM)> ByPosition { get; } = [];

    /// <summary>Узлы сетки узла схемы; пусто — ни по источнику, ни по положению.</summary>
    public IReadOnlyList<string> Find(string nodeTag)
    {
        var bySource = _bySource[nodeTag].ToList();
        if (bySource.Count > 0 || !_nodes.TryGetValue(nodeTag, out var n)) return bySource;
        if (Nearest(n) is not { } m) return [];
        if (!ByPosition.Any(p => p.NodeTag == nodeTag))
            ByPosition.Add((nodeTag, m.Node.NodeTag, m.Distance));
        return [m.Node.NodeTag];
    }

    /// <summary>Первый узел сетки узла схемы или null.</summary>
    public string? FindFirst(string nodeTag) => Find(nodeTag) is { Count: > 0 } tags ? tags[0] : null;

    (FemMeshNode Node, double Distance)? Nearest(FemNode n)
    {
        _grid ??= BuildGrid();
        var (i, j, k) = Cell(n.X, n.Y, n.Z);
        FemMeshNode? best = null;
        double bestD = double.MaxValue;
        for (long a = i - 1; a <= i + 1; a++)
            for (long b = j - 1; b <= j + 1; b++)
                for (long c = k - 1; c <= k + 1; c++)
                    if (_grid.TryGetValue((a, b, c), out var list))
                        foreach (var m in list)
                        {
                            double dx = m.X - n.X, dy = m.Y - n.Y, dz = m.Z - n.Z, d = dx * dx + dy * dy + dz * dz;
                            if (d <= Tolerance * Tolerance && d < bestD) (best, bestD) = (m, d);
                        }
        return best is null ? null : (best, Math.Sqrt(bestD));
    }

    Dictionary<(long, long, long), List<FemMeshNode>> BuildGrid()
    {
        var grid = new Dictionary<(long, long, long), List<FemMeshNode>>();
        foreach (var m in _meshNodes)
        {
            var key = Cell(m.X, m.Y, m.Z);
            if (!grid.TryGetValue(key, out var list)) grid[key] = list = [];
            list.Add(m);
        }
        return grid;
    }

    static (long, long, long) Cell(double x, double y, double z) =>
        ((long)Math.Floor(x / Tolerance), (long)Math.Floor(y / Tolerance), (long)Math.Floor(z / Tolerance));

    /// <summary>
    /// Информационное сообщение <c>schema_node_mesh_by_position</c> о сопоставленных по положению (<paramref name="what"/> —
    /// что переносится: «опоры и пружины», «узловые нагрузки»…); null — таких нет.
    /// </summary>
    public FemValidationDiagnostic? Diagnostic(string what)
    {
        if (ByPosition.Count == 0) return null;
        var sample = ByPosition.Take(5).Select(p => string.Create(CultureInfo.CurrentCulture,
            $"{p.NodeTag} → {p.MeshNodeTag} ({p.DistanceM * 1e3:0.##} мм)"));
        return new FemValidationDiagnostic("schema_node_mesh_by_position",
            $"{what}: узлы схемы без своего узла сетки (ближе 1 мм к другому узлу — сетка сливает такие точки) " +
            $"сопоставлены ближайшему узлу сетки ({ByPosition.Count}): {string.Join(", ", sample)}" +
            (ByPosition.Count > 5 ? " …" : "") + ".", false, [.. ByPosition.Select(p => p.NodeTag)]);
    }
}
