using System.Globalization;
using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>
/// Узлы фрагмента по координатам: совпадающие (до 1 мкм) — один узел; теги с 1 по порядку создания, минуя
/// <paramref name="reservedTags"/> (теги сетки родителя, когда её часть сохраняется). Узел, созданный в точке
/// сохранённого узла сетки (<see cref="Keep"/>), получает его тег и точные координаты — тег узла схемы совпадает с тегом
/// сетки только в той же точке.
/// </summary>
sealed class FragmentNodeTable(List<FemNode> nodes, IReadOnlySet<int>? reservedTags = null)
{
    const double Snap = 1e-6;
    readonly Dictionary<(long, long, long), FemNode> _byKey = [];
    readonly Dictionary<(long, long, long), FemMeshNode> _kept = [];
    readonly HashSet<string> _used = new(StringComparer.Ordinal);
    int _next = 1;

    /// <summary>Узел в точке; маска закреплений объединяется с уже имеющейся.</summary>
    public FemNode Get(PlanarVector3 p, int dofMask = 0)
    {
        // Без сохранённой сетки — точное совпадение ключа, как раньше; с ней — и соседние ячейки (узлы сетки родителя и
        // точки деления стержня по плитам расходятся на ошибки округления).
        if ((_kept.Count == 0 ? Find(p) : FindNear(p)) is { } existing) { existing.DofMask |= dofMask; return existing; }
        var kept = Near(_kept, p);
        string tag;
        if (kept is not null && !_used.Contains(kept.NodeTag)) tag = kept.NodeTag;
        else
        {
            while (reservedTags?.Contains(_next) == true || _used.Contains(_next.ToString(CultureInfo.InvariantCulture))) _next++;
            tag = (_next++).ToString(CultureInfo.InvariantCulture);
        }
        var node = new FemNode
        {
            NodeTag = tag,
            X = kept?.X ?? p.X, Y = kept?.Y ?? p.Y, Z = kept?.Z ?? p.Z, DofMask = dofMask,
        };
        nodes.Add(node);
        _used.Add(tag);
        _byKey[Key(new PlanarVector3(node.X, node.Y, node.Z))] = node;
        return node;
    }

    /// <summary>Узел сохранённой сетки: новые узлы схемы в его точке берут его тег и координаты.</summary>
    public void Keep(FemMeshNode node) => _kept.TryAdd(Key(new PlanarVector3(node.X, node.Y, node.Z)), node);

    /// <summary>Узел в точке, если он уже есть.</summary>
    public FemNode? Find(PlanarVector3 p) => _byKey.GetValueOrDefault(Key(p));

    /// <summary>Узел в точке ± 1 мкм (соседние ячейки ключа), если он уже есть.</summary>
    public FemNode? FindNear(PlanarVector3 p) => Near(_byKey, p);

    static T? Near<T>(Dictionary<(long, long, long), T> map, PlanarVector3 p) where T : class
    {
        var (x, y, z) = Key(p);
        if (map.TryGetValue((x, y, z), out var hit)) return hit;
        for (long dx = -1; dx <= 1; dx++)
            for (long dy = -1; dy <= 1; dy++)
                for (long dz = -1; dz <= 1; dz++)
                    if (map.TryGetValue((x + dx, y + dy, z + dz), out hit)) return hit;
        return null;
    }

    static (long, long, long) Key(PlanarVector3 p) =>
        ((long)Math.Round(p.X / Snap), (long)Math.Round(p.Y / Snap), (long)Math.Round(p.Z / Snap));
}
