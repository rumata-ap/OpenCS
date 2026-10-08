namespace CScore.Fem.Loads;

/// <summary>Свойства КЭ для собственного веса: удельный вес и площадь сечения стержня (толщина пластины — у КЭ).</summary>
public interface IFemSelfWeightSource
{
    /// <summary>Удельный вес материала КЭ, Н/м³; null — неизвестен.</summary>
    double? UnitWeight(FemElement element);

    /// <summary>Площадь сечения стержня, м²; null — неизвестна.</summary>
    double? BarArea(FemElement element);
}

/// <summary>
/// Сетка схемы для разрешения нагрузок: узлы и КЭ по тегам, КЭ конструктивных элементов, группы, геометрия КЭ
/// (с кэшем) и источник свойств собственного веса.
/// </summary>
public sealed class FemLoadMeshContext
{
    readonly Dictionary<string, FemElementGeometry?> _geometry = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, FemMeshNode> NodesByTag { get; }
    public IReadOnlyDictionary<string, FemElement> ElementsByTag { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<FemElement>> ElementsByMemberTag { get; }
    public IReadOnlyDictionary<int, FemMemberGroup> GroupsById { get; }
    public IReadOnlyList<FemElement> Elements { get; }
    public IFemSelfWeightSource? SelfWeight { get; }
    /// <summary>Поворот сечения конструктивных элементов своей схемы, град, по тегу КонЭ.</summary>
    public IReadOnlyDictionary<string, double> MemberRotationDeg { get; }

    public FemLoadMeshContext(IReadOnlyList<FemMeshNode> nodes, IReadOnlyList<FemElement> elements,
        IReadOnlyList<FemMemberGroup>? groups = null, IFemSelfWeightSource? selfWeight = null,
        IReadOnlyDictionary<string, double>? memberRotationDeg = null)
    {
        MemberRotationDeg = memberRotationDeg ?? new Dictionary<string, double>(StringComparer.Ordinal);
        var nodesByTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in nodes) nodesByTag.TryAdd(n.NodeTag, n);
        NodesByTag = nodesByTag;
        var byTag = new Dictionary<string, FemElement>(StringComparer.Ordinal);
        foreach (var e in elements) byTag.TryAdd(e.ElemTag, e);
        ElementsByTag = byTag;
        Elements = elements;
        ElementsByMemberTag = elements.Where(e => e.SourceMemberTag != null)
            .GroupBy(e => e.SourceMemberTag!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<FemElement>)g.ToList(), StringComparer.Ordinal);
        GroupsById = (groups ?? []).GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First());
        SelfWeight = selfWeight;
    }

    /// <summary>
    /// Поворот сечения стержня от осей <see cref="BeamLocalAxisConvention"/>, град: свой у КЭ
    /// (<see cref="FemElement.BeamRotationDeg"/>, импорт), иначе — у конструктивного элемента своей схемы (нет — 0).
    /// Null — импортный КЭ, оси которого не прочитаны.
    /// </summary>
    public double? BarRotationDeg(FemElement element) =>
        element.BeamRotationDeg
        ?? (element.Origin == FemMember.MeshSourceImported ? null
            : element.SourceMemberTag is { } tag && MemberRotationDeg.TryGetValue(tag, out double r) ? r : 0);

    /// <summary>Геометрия КЭ; null — у КЭ неизвестный узел или не 2–4 узла.</summary>
    public FemElementGeometry? Geometry(FemElement element)
    {
        if (!_geometry.TryGetValue(element.ElemTag, out var g))
            _geometry[element.ElemTag] = g = FemElementGeometry.Of(element, NodesByTag);
        return g;
    }
}
