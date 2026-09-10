using System.Text.Json;
using CScore.Fem;

namespace CScore.Submodel;

/// <summary>Строит автономный mesh-снимок принятой прямой стержневой цепочки.</summary>
public static class StraightBeamSubmodelBuilder
{
    /// <summary>Создаёт неизменяемый проект субмодели без обращения к базе данных.</summary>
    public static SubmodelDraftBuildResult Build(
        int parentSchemaId,
        StraightBeamChainAnalysis analysis,
        IReadOnlyList<FemElement> parentElements,
        IReadOnlyList<FemMeshNode> parentNodes)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(parentElements);
        ArgumentNullException.ThrowIfNull(parentNodes);

        var diagnostics = new List<FemValidationDiagnostic>();
        if (parentSchemaId <= 0)
            diagnostics.Add(new("submodel_draft_parent_invalid", "Идентификатор родительской FEM-схемы должен быть положительным.", true, []));
        if (analysis.Verdict == ChainVerdict.NotExtractable || analysis.Chain is null)
            diagnostics.Add(new("submodel_draft_chain_not_extractable", "Прямая цепочка не пригодна для извлечения субмодели.", true, []));
        if (diagnostics.Any(x => x.IsError)) return new(null, diagnostics);

        var elements = Index(parentElements, x => x.ElemTag, "submodel_draft_element_ambiguous", "В mesh-снимке повторяется тег конечного элемента.", diagnostics);
        var nodes = Index(parentNodes, x => x.NodeTag, "submodel_draft_node_ambiguous", "В mesh-снимке повторяется тег узла.", diagnostics);
        if (diagnostics.Any(x => x.IsError)) return new(null, diagnostics);

        var meshNodes = new List<FemMeshNode>();
        var meshElements = new List<FemElement>();
        var nodeDrafts = new List<SubmodelNodeDraft>();
        var segmentDrafts = new List<SubmodelSegmentDraft>();
        var addedNodes = new HashSet<string>(StringComparer.Ordinal);
        var chain = analysis.Chain!;

        foreach (var (ordered, ordinal) in chain.Segments.Select((x, i) => (x, i)))
        {
            if (!elements.TryGetValue(ordered.Source.SourceKey, out var source))
            {
                diagnostics.Add(new("submodel_draft_element_missing", $"Элемент {ordered.Source.SourceKey} цепочки отсутствует в mesh-снимке.", true, [ordered.Source.SourceKey]));
                continue;
            }
            if (source.ElemType != "beam" || !TryNodeTags(source.NodeIdsJson, out var tags) || tags.Count != 2)
            {
                diagnostics.Add(new("submodel_draft_element_invalid", $"Элемент {source.ElemTag} не является корректным двухузловым стержнем.", true, [source.ElemTag]));
                continue;
            }
            if (!sourceIsFinite(source) || !ordered.Source.IsFinite || !double.IsFinite(ordered.StartStation) || !double.IsFinite(ordered.EndStation) || !double.IsFinite(ordered.LengthM) || !double.IsFinite(ordered.AngleToAxisDeg))
            {
                diagnostics.Add(new("submodel_draft_element_invalid", $"Элемент {source.ElemTag} содержит неконечные параметры.", true, [source.ElemTag]));
                continue;
            }

            var resolvedNodes = new List<FemMeshNode>(2);
            foreach (var tag in tags)
            {
                if (!nodes.TryGetValue(tag, out var node))
                {
                    diagnostics.Add(new("submodel_draft_node_missing", $"Элемент {source.ElemTag} ссылается на отсутствующий узел {tag}.", true, [source.ElemTag, tag]));
                    continue;
                }
                if (!double.IsFinite(node.X) || !double.IsFinite(node.Y) || !double.IsFinite(node.Z))
                {
                    diagnostics.Add(new("submodel_draft_node_invalid", $"Узел {tag} содержит неконечные координаты.", true, [tag]));
                    continue;
                }
                resolvedNodes.Add(node);
            }
            if (resolvedNodes.Count != 2) continue;

            foreach (var node in resolvedNodes)
            {
                if (!addedNodes.Add(node.NodeTag)) continue;
                var clone = Clone(node);
                meshNodes.Add(clone);
                nodeDrafts.Add(new(node.Id, node.NodeTag, node.SourceNodeTag, node.SourceMemberTag, clone));
            }

            var elementClone = Clone(source);
            meshElements.Add(elementClone);
            segmentDrafts.Add(new(
                ordinal, source.Id, source.ElemTag, source.SourceMemberTag,
                ordered.IsReversed, ordered.StartStation, ordered.EndStation,
                ordered.LengthM, ordered.AngleToAxisDeg, ordered.Source.BetaDeg,
                ordered.Source.BetaSource, elementClone));
        }

        if (diagnostics.Any(x => x.IsError)) return new(null, diagnostics);
        return new(new(parentSchemaId, meshNodes, meshElements, nodeDrafts, segmentDrafts,
            analysis.Tolerances, analysis.Metrics, analysis.Diagnostics), diagnostics);
    }

    static Dictionary<string, T> Index<T>(IReadOnlyList<T> source, Func<T, string> key, string code, string message, List<FemValidationDiagnostic> diagnostics)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in source)
        {
            var value = key(item);
            if (!result.TryAdd(value, item)) diagnostics.Add(new(code, $"{message} Тег: {value}.", true, [value]));
        }
        return result;
    }

    static bool TryNodeTags(string json, out IReadOnlyList<string> tags)
    {
        tags = [];
        try
        {
            var ids = JsonSerializer.Deserialize<int[]>(json);
            if (ids is null) return false;
            tags = ids.Select(x => x.ToString()).ToList();
            return true;
        }
        catch (JsonException) { return false; }
    }

    static bool sourceIsFinite(FemElement source) =>
        source.GjManualValue is null || double.IsFinite(source.GjManualValue.Value);

    static FemMeshNode Clone(FemMeshNode node) => new()
    {
        NodeTag = node.NodeTag, X = node.X, Y = node.Y, Z = node.Z,
        SourceNodeTag = node.SourceNodeTag, SourceMemberTag = node.SourceMemberTag
    };

    static FemElement Clone(FemElement element) => new()
    {
        ElemTag = element.ElemTag, ElemType = element.ElemType, NodeIdsJson = element.NodeIdsJson,
        SourceMemberTag = element.SourceMemberTag, SectionTag = element.SectionTag,
        MaterialTag = element.MaterialTag, ThicknessM = element.ThicknessM,
        CrossSectionId = element.CrossSectionId, GjStrategy = element.GjStrategy,
        GjManualValue = element.GjManualValue, GjTorsionTaskId = element.GjTorsionTaskId
    };
}
