using CScore.Fem;

namespace CScore.Submodel;

/// <summary>
/// Проверка, что mesh-снимок дочерней схемы по-прежнему тот, что сохранило извлечение: теги,
/// координаты, связность, отсутствие лишних объектов и дубликатов. Пересоздание сетки
/// (<c>SaveFemMeshSnapshot</c>) меняет <c>Id</c> строк — это ловит режим <c>checkIds</c>.
/// </summary>
public static class SubmodelMeshIntegrity
{
    const double DefaultCoincidenceM = 1e-6;

    /// <param name="checkIds">Сверять <c>SubmodelNodeId</c>/<c>SubmodelElementId</c> с <c>Id</c> строк
    /// (только для объектов, прочитанных из БД).</param>
    public static IReadOnlyList<FemValidationDiagnostic> Check(SubmodelExtraction extraction,
        IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemElement> meshElements, bool checkIds)
    {
        var errors = new List<FemValidationDiagnostic>();
        void Error(string message, string key) =>
            errors.Add(new(SubmodelMaterializationDiagnostics.MeshStale, message, true, [key]));

        double tolerance = extraction.Tolerances?.NodeCoincidenceM is { } t && t > 0 ? t : DefaultCoincidenceM;

        var nodeByTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var node in meshNodes)
            if (!nodeByTag.TryAdd(node.NodeTag, node))
                Error($"Дубликат тега mesh-узла {node.NodeTag} в дочерней схеме.", node.NodeTag);
        var elementByTag = new Dictionary<string, FemElement>(StringComparer.Ordinal);
        foreach (var element in meshElements)
            if (!elementByTag.TryAdd(element.ElemTag, element))
                Error($"Дубликат тега КЭ {element.ElemTag} в дочерней схеме.", element.ElemTag);

        var extractionNodeTags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expected in extraction.Nodes)
        {
            extractionNodeTags.Add(expected.SubmodelNodeTag);
            if (!nodeByTag.TryGetValue(expected.SubmodelNodeTag, out var node))
            {
                Error($"Mesh-узел {expected.SubmodelNodeTag} извлечения отсутствует в дочерней схеме.", expected.SubmodelNodeTag);
                continue;
            }
            if (checkIds && node.Id != expected.SubmodelNodeId)
                Error($"Mesh-узел {node.NodeTag} пересоздан (id {node.Id} вместо {expected.SubmodelNodeId}): сетка дочерней схемы изменилась после извлечения.", node.NodeTag);
            double offset = Math.Sqrt(Math.Pow(node.X - expected.X, 2) + Math.Pow(node.Y - expected.Y, 2) + Math.Pow(node.Z - expected.Z, 2));
            if (offset > tolerance)
                Error($"Mesh-узел {node.NodeTag} смещён на {offset:G6} м относительно извлечения.", node.NodeTag);
        }

        var segmentTags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segment in extraction.Segments)
        {
            segmentTags.Add(segment.SubmodelElementTag);
            if (!elementByTag.TryGetValue(segment.SubmodelElementTag, out var element))
            {
                Error($"КЭ {segment.SubmodelElementTag} извлечения отсутствует в дочерней схеме.", segment.SubmodelElementTag);
                continue;
            }
            if (checkIds && element.Id != segment.SubmodelElementId)
                Error($"КЭ {element.ElemTag} пересоздан (id {element.Id} вместо {segment.SubmodelElementId}): сетка дочерней схемы изменилась после извлечения.", element.ElemTag);
            var ends = FemMeshTopology.ReadNodeTags(element, 2);
            if (ends is null)
                Error($"КЭ {element.ElemTag}: не читается связность NodeIdsJson.", element.ElemTag);
            else if (!extractionNodeTags.Contains(ends[0]) || !extractionNodeTags.Contains(ends[1]) || ends[0] == ends[1])
                Error($"КЭ {element.ElemTag}: узлы {ends[0]}, {ends[1]} не совпадают с узлами извлечения.", element.ElemTag);
        }

        foreach (var node in meshNodes)
            if (!extractionNodeTags.Contains(node.NodeTag))
                Error($"Лишний mesh-узел {node.NodeTag}: его нет в извлечении.", node.NodeTag);
        foreach (var element in meshElements)
            if (!segmentTags.Contains(element.ElemTag))
                Error($"Лишний КЭ {element.ElemTag}: его нет в извлечении.", element.ElemTag);

        return errors;
    }
}
