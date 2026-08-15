using System.Globalization;
using System.Text.Json;
using CScore.Fem;
using OpenCS.OpenSees.Structural;

namespace OpenCS.ViewModels;

/// <summary>Входные данные построения preview набора усилий.</summary>
public sealed record FemMemberForceSetBuildInput(
    FemSchema Schema,
    FemMember? Member,
    IReadOnlyList<FemNode> SourceNodes,
    IReadOnlyList<FemMeshNode> MeshNodes,
    IReadOnlyList<FemElement> MeshElements,
    IReadOnlyList<FemElementEndForces> ElementForces,
    int StepIndex,
    string StepLabel,
    bool StepConverged);

/// <summary>Диагностический результат построения preview.</summary>
public enum FemMemberForceSetBuildError
{
    /// <summary>Ошибок нет.</summary>
    None,
    /// <summary>Конструктивный стержень не найден.</summary>
    MemberNotFound,
    /// <summary>Нельзя прочитать или определить исходные узлы.</summary>
    MissingSourceNode,
    /// <summary>Нельзя однозначно ориентировать цепочку.</summary>
    CannotOrientMember,
    /// <summary>Для стержня отсутствуют mesh-КЭ.</summary>
    NoMeshElements,
    /// <summary>У mesh-КЭ отсутствует узел в снимке сетки.</summary>
    MissingMeshNode,
    /// <summary>Для mesh-КЭ отсутствуют концевые усилия.</summary>
    MissingElementForce,
    /// <summary>Результат содержит нечисловое или бесконечное усилие.</summary>
    NonFiniteForce,
    /// <summary>Нарушена общая топология цепочки.</summary>
    InvalidTopology,
    /// <summary>Один и тот же КЭ повторно используется в цепочке.</summary>
    ReusedElement,
    /// <summary>Два разных КЭ используют одну пару узлов.</summary>
    DuplicateElementPair,
    /// <summary>У КЭ совпадают оба узла.</summary>
    EqualElementNodes,
    /// <summary>Длина КЭ равна нулю.</summary>
    ZeroLengthElement,
    /// <summary>Выбранный расчётный шаг не сошёлся.</summary>
    NotConvergedStep
}

/// <summary>Результат работы builder-а.</summary>
public sealed record FemMemberForceSetBuildResult(
    FemMemberForceSetPreview? Preview,
    FemMemberForceSetBuildError Error)
{
    /// <summary>Признак полностью построенного preview.</summary>
    public bool IsSuccess => Preview is not null && Error == FemMemberForceSetBuildError.None;
}

/// <summary>Восстанавливает последовательную mesh-цепочку конструктивного стержня.</summary>
public static class FemMemberForceSetBuilder
{
    const double LengthTolerance = 1e-12;
    const double CoordinateTolerance = 1e-8;

    sealed record MeshEdge(
        string ElementTag,
        int ElementTagNumber,
        string NodeI,
        string NodeJ,
        FemMeshNode NodeIInfo,
        FemMeshNode NodeJInfo,
        double Length);

    sealed class RowDraft
    {
        public RowDraft(string nodeTag, double positionS)
        {
            NodeTag = nodeTag;
            PositionS = positionS;
        }

        public string NodeTag { get; }
        public double PositionS { get; }
        public FemMemberForceCandidate? LeftCandidate { get; set; }
        public FemMemberForceCandidate? RightCandidate { get; set; }
    }

    /// <summary>Строит preview и возвращает диагностическую ошибку без частичного результата.</summary>
    public static FemMemberForceSetBuildResult Build(FemMemberForceSetBuildInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Member is null)
            return Failure(FemMemberForceSetBuildError.MemberNotFound);
        if (!input.StepConverged)
            return Failure(FemMemberForceSetBuildError.NotConvergedStep);
        if (!TryReadTwoTags(input.Member.NodeIdsJson, out string[] sourceTags))
            return Failure(FemMemberForceSetBuildError.MissingSourceNode);

        var memberElements = input.MeshElements
            .Where(element => element.SourceMemberTag == input.Member.ElemTag)
            .ToList();
        if (memberElements.Count == 0)
            return Failure(FemMemberForceSetBuildError.NoMeshElements);

        var meshNodesByTag = input.MeshNodes
            .GroupBy(node => node.NodeTag, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        if (meshNodesByTag.Any(pair => pair.Value.Count != 1))
            return Failure(FemMemberForceSetBuildError.InvalidTopology);

        var edges = new List<MeshEdge>(memberElements.Count);
        var elementTags = new HashSet<string>(StringComparer.Ordinal);
        var elementPairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (FemElement element in memberElements)
        {
            if (!int.TryParse(element.ElemTag, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int elementTagNumber) || !elementTags.Add(element.ElemTag))
                return Failure(FemMemberForceSetBuildError.ReusedElement);
            if (!TryReadTwoTags(element.NodeIdsJson, out string[] nodeTags))
                return Failure(FemMemberForceSetBuildError.InvalidTopology);

            string nodeI = nodeTags[0];
            string nodeJ = nodeTags[1];
            if (nodeI == nodeJ)
                return Failure(FemMemberForceSetBuildError.EqualElementNodes);
            if (!meshNodesByTag.TryGetValue(nodeI, out var nodeIList) ||
                !meshNodesByTag.TryGetValue(nodeJ, out var nodeJList))
                return Failure(FemMemberForceSetBuildError.MissingMeshNode);

            string pairKey = string.CompareOrdinal(nodeI, nodeJ) < 0
                ? $"{nodeI}\u001f{nodeJ}"
                : $"{nodeJ}\u001f{nodeI}";
            if (!elementPairs.Add(pairKey))
                return Failure(FemMemberForceSetBuildError.DuplicateElementPair);

            FemMeshNode nodeIInfo = nodeIList[0];
            FemMeshNode nodeJInfo = nodeJList[0];
            if (!CoordinatesAreFinite(nodeIInfo) || !CoordinatesAreFinite(nodeJInfo))
                return Failure(FemMemberForceSetBuildError.InvalidTopology);
            double length = Distance(nodeIInfo, nodeJInfo);
            if (length <= LengthTolerance)
                return Failure(FemMemberForceSetBuildError.ZeroLengthElement);

            edges.Add(new MeshEdge(
                element.ElemTag, elementTagNumber, nodeI, nodeJ,
                nodeIInfo, nodeJInfo, length));
        }

        var incident = edges
            .SelectMany(edge => new[]
            {
                (Node: edge.NodeI, Edge: edge),
                (Node: edge.NodeJ, Edge: edge)
            })
            .GroupBy(item => item.Node, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Edge).ToList(),
                StringComparer.Ordinal);

        if (incident.Any(pair => pair.Value.Count > 2))
            return Failure(FemMemberForceSetBuildError.InvalidTopology);
        var endpoints = incident
            .Where(pair => pair.Value.Count == 1)
            .Select(pair => pair.Key)
            .ToList();
        if (endpoints.Count != 2)
            return Failure(FemMemberForceSetBuildError.InvalidTopology);

        var forceGroups = input.ElementForces.GroupBy(force => force.ElemTag).ToList();
        if (forceGroups.Any(group => group.Count() != 1))
            return Failure(FemMemberForceSetBuildError.InvalidTopology);
        var forcesByTag = forceGroups.ToDictionary(group => group.Key, group => group.Single());

        string? mappedFirst = ResolveEndpoint(
            sourceTags[0], input.SourceNodes, meshNodesByTag, endpoints);
        string? mappedSecond = ResolveEndpoint(
            sourceTags[1], input.SourceNodes, meshNodesByTag, endpoints);
        if (mappedFirst is not null && mappedSecond is not null && mappedFirst == mappedSecond)
            return Failure(FemMemberForceSetBuildError.CannotOrientMember);

        string startNode;
        string endNode;
        if (mappedFirst is not null && mappedSecond is not null)
        {
            startNode = mappedFirst;
            endNode = mappedSecond;
        }
        else if (mappedFirst is not null)
        {
            startNode = mappedFirst;
            endNode = endpoints.Single(node => node != startNode);
        }
        else if (mappedSecond is not null)
        {
            startNode = endpoints.Single(node => node != mappedSecond);
            endNode = mappedSecond;
        }
        else
        {
            var orderedEndpoints = endpoints.OrderBy(EndpointSortKey, StringComparer.Ordinal).ToArray();
            startNode = orderedEndpoints[0];
            endNode = orderedEndpoints[1];
        }

        var pathNodes = new List<string>();
        var pathEdges = new List<MeshEdge>();
        var usedEdges = new HashSet<MeshEdge>();
        string currentNode = startNode;
        while (true)
        {
            pathNodes.Add(currentNode);
            if (currentNode == endNode && usedEdges.Count == edges.Count)
                break;

            MeshEdge? nextEdge = incident[currentNode].FirstOrDefault(edge => !usedEdges.Contains(edge));
            if (nextEdge is null)
                return Failure(FemMemberForceSetBuildError.InvalidTopology);

            usedEdges.Add(nextEdge);
            pathEdges.Add(nextEdge);
            currentNode = nextEdge.NodeI == currentNode ? nextEdge.NodeJ : nextEdge.NodeI;
            if (pathNodes.Count > edges.Count + 1)
                return Failure(FemMemberForceSetBuildError.InvalidTopology);
        }

        if (pathNodes.Count != edges.Count + 1 || currentNode != endNode)
            return Failure(FemMemberForceSetBuildError.InvalidTopology);

        var rows = pathNodes
            .Select((nodeTag, index) => new RowDraft(nodeTag, 0.0))
            .ToList();
        double positionS = 0.0;
        rows[0] = new RowDraft(pathNodes[0], positionS);
        for (int index = 0; index < pathEdges.Count; index++)
        {
            MeshEdge edge = pathEdges[index];
            string before = pathNodes[index];
            string after = pathNodes[index + 1];
            if (!forcesByTag.TryGetValue(edge.ElementTagNumber, out FemElementEndForces? force))
                return Failure(FemMemberForceSetBuildError.MissingElementForce);
            if (!AllForceValuesAreFinite(force))
                return Failure(FemMemberForceSetBuildError.NonFiniteForce);

            FemForceEndpointPair pair = FemForceEndpointConverter.Convert(
                force, FemForceEndpointSignPolicy.OpenSeesDefault);
            FemForceEndpointValues beforeValues;
            FemForceEndpointValues afterValues;
            if (edge.NodeI == before && edge.NodeJ == after)
            {
                beforeValues = pair.Start;
                afterValues = pair.End;
            }
            else if (edge.NodeI == after && edge.NodeJ == before)
            {
                beforeValues = pair.End;
                afterValues = pair.Start;
            }
            else
            {
                return Failure(FemMemberForceSetBuildError.InvalidTopology);
            }

            var candidateBefore = new FemMemberForceCandidate(edge.ElementTagNumber, beforeValues);
            var candidateAfter = new FemMemberForceCandidate(edge.ElementTagNumber, afterValues);
            if (index == 0)
                rows[index].RightCandidate = candidateBefore;
            else
                rows[index].RightCandidate = candidateBefore;

            positionS += edge.Length;
            rows[index + 1] = new RowDraft(pathNodes[index + 1], positionS)
            {
                LeftCandidate = candidateAfter
            };
        }

        var previewRows = rows
            .Select((row, index) => new FemMemberForceSetPreviewRow(
                row.NodeTag,
                row.PositionS,
                row.LeftCandidate,
                row.RightCandidate,
                index == 0 || index == rows.Count - 1
                    ? FemForceSourceSide.Only
                    : FemForceSourceSide.Left))
            .ToArray();

        return new FemMemberForceSetBuildResult(
            new FemMemberForceSetPreview(
                input.Schema.Id,
                input.Schema.Tag,
                input.Member.Id,
                input.Member.ElemTag,
                input.StepIndex,
                input.StepLabel,
                previewRows),
            FemMemberForceSetBuildError.None);
    }

    static FemMemberForceSetBuildResult Failure(FemMemberForceSetBuildError error) =>
        new(null, error);

    static bool TryReadTwoTags(string json, out string[] tags)
    {
        try
        {
            int[]? ids = JsonSerializer.Deserialize<int[]>(json);
            if (ids is not { Length: 2 })
            {
                tags = [];
                return false;
            }

            tags = ids.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray();
            return true;
        }
        catch (JsonException)
        {
            tags = [];
            return false;
        }
    }

    static string? ResolveEndpoint(
        string sourceTag,
        IReadOnlyList<FemNode> sourceNodes,
        IReadOnlyDictionary<string, List<FemMeshNode>> meshNodesByTag,
        IReadOnlyCollection<string> endpoints)
    {
        string? bySourceTag = meshNodesByTag.Values
            .SelectMany(nodes => nodes)
            .Where(node => endpoints.Contains(node.NodeTag) && node.SourceNodeTag == sourceTag)
            .Select(node => node.NodeTag)
            .OrderBy(node => EndpointSortKey(node), StringComparer.Ordinal)
            .FirstOrDefault();
        if (bySourceTag is not null)
            return bySourceTag;

        FemNode? sourceNode = sourceNodes.FirstOrDefault(node => node.NodeTag == sourceTag);
        if (sourceNode is null)
            return null;

        return meshNodesByTag.Values
            .SelectMany(nodes => nodes)
            .Where(node => endpoints.Contains(node.NodeTag))
            .Where(node => Distance(node, sourceNode) <= CoordinateTolerance)
            .OrderBy(node => EndpointSortKey(node.NodeTag), StringComparer.Ordinal)
            .Select(node => node.NodeTag)
            .FirstOrDefault();
    }

    static string EndpointSortKey(string nodeTag) =>
        int.TryParse(nodeTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? $"0:{value:D20}"
            : $"1:{nodeTag}";

    static bool CoordinatesAreFinite(FemMeshNode node) =>
        double.IsFinite(node.X) && double.IsFinite(node.Y) && double.IsFinite(node.Z);

    static bool AllForceValuesAreFinite(FemElementEndForces force) =>
        double.IsFinite(force.Ni) && double.IsFinite(force.Qyi) && double.IsFinite(force.Qzi) &&
        double.IsFinite(force.Mxi) && double.IsFinite(force.Myi) && double.IsFinite(force.Mzi) &&
        double.IsFinite(force.Nj) && double.IsFinite(force.Qyj) && double.IsFinite(force.Qzj) &&
        double.IsFinite(force.Mxj) && double.IsFinite(force.Myj) && double.IsFinite(force.Mzj);

    static double Distance(FemMeshNode first, FemMeshNode second) =>
        Math.Sqrt(
            Math.Pow(first.X - second.X, 2) +
            Math.Pow(first.Y - second.Y, 2) +
            Math.Pow(first.Z - second.Z, 2));

    static double Distance(FemMeshNode meshNode, FemNode sourceNode) =>
        Math.Sqrt(
            Math.Pow(meshNode.X - sourceNode.X, 2) +
            Math.Pow(meshNode.Y - sourceNode.Y, 2) +
            Math.Pow(meshNode.Z - sourceNode.Z, 2));
}
