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
    const double MergeM = FemPlanarMeshPlan.MergeToleranceM;
    readonly Dictionary<(long, long, long), FemNode> _byKey = [];
    readonly Dictionary<(long, long, long), List<FemNode>> _byCell = [];
    readonly Dictionary<(long, long, long), FemMeshNode> _kept = [];
    readonly Dictionary<string, FemNode> _byTag = new(StringComparer.Ordinal);
    int _next = 1;

    /// <summary>Узел в точке; маска закреплений объединяется с уже имеющейся.</summary>
    public FemNode Get(PlanarVector3 p, int dofMask = 0)
    {
        // Без сохранённой сетки — точное совпадение ключа, как раньше; с ней — и соседние ячейки (узлы сетки родителя и
        // точки деления стержня по плитам расходятся на ошибки округления).
        if ((_kept.Count == 0 ? Find(p) : FindNear(p)) is { } existing) { existing.DofMask |= dofMask; return existing; }
        var kept = Near(_kept, p);
        string tag;
        if (kept is not null && !_byTag.ContainsKey(kept.NodeTag)) tag = kept.NodeTag;
        else
        {
            while (reservedTags?.Contains(_next) == true || _byTag.ContainsKey(_next.ToString(CultureInfo.InvariantCulture))) _next++;
            tag = (_next++).ToString(CultureInfo.InvariantCulture);
        }
        var node = new FemNode
        {
            NodeTag = tag,
            X = kept?.X ?? p.X, Y = kept?.Y ?? p.Y, Z = kept?.Z ?? p.Z, DofMask = dofMask,
        };
        nodes.Add(node);
        _byTag[tag] = node;
        _byKey[Key(new PlanarVector3(node.X, node.Y, node.Z))] = node;
        var cell = Cell(new PlanarVector3(node.X, node.Y, node.Z));
        if (!_byCell.TryGetValue(cell, out var list)) _byCell[cell] = list = [];
        list.Add(node);
        return node;
    }

    /// <summary>
    /// Ближайший узел не дальше <see cref="FemPlanarMeshPlan.MergeToleranceM"/>, иначе новый в точке. Для узлов нагрузок:
    /// сетка всё равно сливает точки ближе допуска в один узел сетки, а он сопоставляется одному узлу схемы — нагрузка
    /// второго терялась бы (ветер ЛИРЫ «Мирной» — в 0,68 мм от угла плиты).
    /// </summary>
    public FemNode GetMerged(PlanarVector3 p)
    {
        var (cx, cy, cz) = Cell(p);
        FemNode? best = null;
        double bestD = MergeM;
        for (long dx = -1; dx <= 1; dx++)
            for (long dy = -1; dy <= 1; dy++)
                for (long dz = -1; dz <= 1; dz++)
                    if (_byCell.TryGetValue((cx + dx, cy + dy, cz + dz), out var list))
                        foreach (var n in list)
                        {
                            double d = (new PlanarVector3(n.X, n.Y, n.Z) - p).Length;
                            if (d <= bestD) (best, bestD) = (n, d);
                        }
        return best ?? Get(p);
    }

    /// <summary>Узел сохранённой сетки: новые узлы схемы в его точке берут его тег и координаты.</summary>
    public void Keep(FemMeshNode node) => _kept.TryAdd(Key(new PlanarVector3(node.X, node.Y, node.Z)), node);

    /// <summary>Узел по тегу (теги не подряд, если часть сетки родителя сохраняется).</summary>
    public FemNode? ByTag(string tag) => _byTag.GetValueOrDefault(tag);

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

    /// <summary>Ячейка решётки размером с допуск слияния: узлы в пределах допуска — в соседних ячейках.</summary>
    static (long, long, long) Cell(PlanarVector3 p) =>
        ((long)Math.Floor(p.X / MergeM), (long)Math.Floor(p.Y / MergeM), (long)Math.Floor(p.Z / MergeM));
}
