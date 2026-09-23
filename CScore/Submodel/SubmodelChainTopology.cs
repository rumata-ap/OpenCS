using CScore.Fem;

namespace CScore.Submodel;

/// <summary>Сегмент цепочки в терминах родительского mesh: теги концов в порядке узлов элемента (i, j).
/// Дочерний КЭ — клон родительского (тот же <c>NodeIdsJson</c>), поэтому i/j у них совпадают.</summary>
public sealed record ChainSegmentTopology(
    SubmodelExtractionSegment Segment, string ParentNodeI, string ParentNodeJ);

/// <summary>
/// Топология извлечённой цепочки поверх родительского mesh-снимка: концы, внутренние узлы,
/// соответствие родительских и дочерних тегов. Все теги — канонические строковые.
/// </summary>
public sealed class SubmodelChainTopology
{
    SubmodelChainTopology(
        IReadOnlyList<ChainSegmentTopology> segments,
        string startParentNodeTag, string endParentNodeTag,
        IReadOnlyDictionary<string, string> childNodeByParent,
        IReadOnlySet<string> chainNodes)
    {
        Segments = segments;
        StartParentNodeTag = startParentNodeTag;
        EndParentNodeTag = endParentNodeTag;
        ChildNodeByParent = childNodeByParent;
        ChainNodes = chainNodes;
        SegmentByParentElement = segments.ToDictionary(s => s.Segment.ParentElementTag, StringComparer.Ordinal);
    }

    /// <summary>Сегменты в порядке цепочки (по <c>Ordinal</c>).</summary>
    public IReadOnlyList<ChainSegmentTopology> Segments { get; }
    public IReadOnlyDictionary<string, ChainSegmentTopology> SegmentByParentElement { get; }
    public string StartParentNodeTag { get; }
    public string EndParentNodeTag { get; }
    public IReadOnlyDictionary<string, string> ChildNodeByParent { get; }
    /// <summary>Все родительские узлы цепочки, включая концы.</summary>
    public IReadOnlySet<string> ChainNodes { get; }

    public bool IsEnd(string parentNodeTag) =>
        parentNodeTag == StartParentNodeTag || parentNodeTag == EndParentNodeTag;

    public bool IsInterior(string parentNodeTag) => ChainNodes.Contains(parentNodeTag) && !IsEnd(parentNodeTag);

    /// <summary>Строит топологию; при несогласованности извлечения с родительским снимком — диагностики и null.</summary>
    public static SubmodelChainTopology? Build(
        SubmodelExtraction extraction,
        IReadOnlyList<FemElement> parentMeshElements,
        List<FemValidationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(parentMeshElements);
        var elementByTag = new Dictionary<string, FemElement>(StringComparer.Ordinal);
        foreach (var element in parentMeshElements) elementByTag.TryAdd(element.ElemTag, element);

        var ordered = extraction.Segments.OrderBy(s => s.Ordinal).ToList();
        if (ordered.Count == 0)
        {
            diagnostics.Add(new(BoundaryScenarioDiagnostics.ExtractionInvalid, "Извлечение не содержит сегментов цепочки.", true, []));
            return null;
        }

        var segments = new List<ChainSegmentTopology>();
        foreach (var segment in ordered)
        {
            if (!elementByTag.TryGetValue(segment.ParentElementTag, out var element))
            {
                diagnostics.Add(new(BoundaryScenarioDiagnostics.ExtractionInvalid,
                    $"Элемент {segment.ParentElementTag} цепочки отсутствует в родительском mesh-снимке.", true, [segment.ParentElementTag]));
                continue;
            }
            var tags = FemMeshTopology.ReadNodeTags(element, 2);
            if (tags is null)
            {
                diagnostics.Add(new(BoundaryScenarioDiagnostics.ExtractionInvalid,
                    $"Элемент {segment.ParentElementTag} цепочки имеет некорректную связность.", true, [segment.ParentElementTag]));
                continue;
            }
            segments.Add(new ChainSegmentTopology(segment, tags[0], tags[1]));
        }
        if (segments.Count != ordered.Count) return null;

        string start = segments[0].Segment.IsReversed ? segments[0].ParentNodeJ : segments[0].ParentNodeI;
        string end = segments[^1].Segment.IsReversed ? segments[^1].ParentNodeI : segments[^1].ParentNodeJ;
        var chainNodes = new HashSet<string>(segments.SelectMany(s => new[] { s.ParentNodeI, s.ParentNodeJ }), StringComparer.Ordinal);

        var childByParent = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in extraction.Nodes)
            if (FemMeshTopology.CanonicalNodeTag(node.ParentNodeTag) is string parent)
                childByParent[parent] = node.SubmodelNodeTag;
        foreach (var tag in chainNodes)
            if (!childByParent.ContainsKey(tag))
            {
                diagnostics.Add(new(BoundaryScenarioDiagnostics.ExtractionInvalid,
                    $"Узел {tag} цепочки не имеет соответствия в дочерней схеме.", true, [tag]));
                return null;
            }

        return new SubmodelChainTopology(segments, start, end, childByParent, chainNodes);
    }
}
