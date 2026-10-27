using CScore.Fem.Loads;

namespace CScore.Fem.Fragment;

/// <summary>
/// Сопоставление сетки фрагмента с опорной сеткой (спека разд. 5): тег КЭ фрагмента → КЭ родителя. Одно правило для
/// всех потребителей — нагрузок-полей, c1, армирования: пластина — КЭ родителя той же плоскости, содержащий центр
/// (нет — ближайший в пределах половины размера КЭ фрагмента); стержень — КЭ родителя на той же оси, содержащий середину.
/// </summary>
public sealed class FragmentParentMap
{
    FragmentParentMap(Dictionary<string, FragmentRefElement> byTag, List<string> unmapped)
    {
        ByTag = byTag;
        Unmapped = unmapped;
    }

    /// <summary>КЭ родителя по тегу КЭ фрагмента.</summary>
    public IReadOnlyDictionary<string, FragmentRefElement> ByTag { get; }

    /// <summary>КЭ фрагмента без пары в опорной сетке.</summary>
    public IReadOnlyList<string> Unmapped { get; }

    /// <summary>Сопоставить КЭ <paramref name="elements"/> (узлы — <paramref name="nodesByTag"/>); КЭ без геометрии пропускаются.</summary>
    public static FragmentParentMap Build(IEnumerable<FemElement> elements, IReadOnlyDictionary<string, FemMeshNode> nodesByTag,
        ParentMeshMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(mapper);
        var byTag = new Dictionary<string, FragmentRefElement>(StringComparer.Ordinal);
        var unmapped = new List<string>();
        foreach (var e in elements)
        {
            if (FemElementGeometry.Of(e, nodesByTag) is not { } g || !(g.IsBar || g.IsShell)) continue;
            if (MapElement(mapper, g) is { } r) byTag[e.ElemTag] = r;
            else unmapped.Add(e.ElemTag);
        }
        return new FragmentParentMap(byTag, unmapped);
    }

    /// <summary>КЭ родителя для КЭ фрагмента с геометрией <paramref name="g"/>; null — пары нет.</summary>
    public static FragmentRefElement? MapElement(ParentMeshMapper mapper, FemElementGeometry g)
    {
        if (g.IsShell) return mapper.MapShell(g.Centroid, g.ShellFrame().Z, Math.Sqrt(g.Area) / 2);
        if (!g.IsBar || g.Length <= 0) return null;
        return mapper.MapBar(g.Centroid, (g.Points[1] - g.Points[0]).Normalize());
    }
}
