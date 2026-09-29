using System.Text.Json;

namespace CScore.Fem;

/// <summary>Дискретизатор конструктивных стержней FEM-схемы в сетку двухузловых элементов.</summary>
public static class FemMeshDiscretizer
{
    /// <summary>Допуск проверки принадлежности узла отрезку, м.</summary>
    public const double CollinearToleranceM = 0.001;

    /// <summary>Строит сетку beam-стержней с встраиванием существующих узлов и дроблением по целевой длине.</summary>
    public static (List<FemMeshNode> Nodes, List<FemElement> Elements) Discretize(
        int schemaId,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemMember> members,
        double? defaultTargetLengthM)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(members);

        var meshNodes = new List<FemMeshNode>();
        var meshElements = new List<FemElement>();
        var meshNodeBySourceTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        var reservedNodeTags = nodes
            .Select(node => int.TryParse(node.NodeTag, out var tag) && tag > 0 ? tag : 0)
            .Where(tag => tag > 0)
            .ToHashSet();
        var usedNodeTags = new HashSet<int>();
        var nodeByTag = nodes
            .Where(node => int.TryParse(node.NodeTag, out var tag) && tag > 0)
            .GroupBy(node => int.Parse(node.NodeTag), EqualityComparer<int>.Default)
            .ToDictionary(group => group.Key, group => group.First());
        var nextNodeTag = 1;
        var nextSyntheticNodeTag = -1;
        var nextElementTag = 1;

        foreach (var member in members)
        {
            if (member.ElemType != "beam")
                continue;

            if (!TryReadEndpointTags(member.NodeIdsJson, out var endpointTags) ||
                !nodeByTag.TryGetValue(endpointTags[0], out var firstSourceNode) ||
                !nodeByTag.TryGetValue(endpointTags[1], out var secondSourceNode))
                continue;

            var direction = new Vector(
                secondSourceNode.X - firstSourceNode.X,
                secondSourceNode.Y - firstSourceNode.Y,
                secondSourceNode.Z - firstSourceNode.Z);
            var lengthSquared = direction.Dot(direction);
            var length = Math.Sqrt(lengthSquared);
            if (!double.IsFinite(length) || length < CollinearToleranceM)
                continue;

            var points = nodes
                .Select((node, index) => new PointOnMember(node, Parameter(node, firstSourceNode, direction, lengthSquared), index))
                .Where(point => IsOnSegment(point.Node, firstSourceNode, direction, lengthSquared, length))
                .GroupBy(point => point.Node.NodeTag, StringComparer.Ordinal)
                .Select(group => group.OrderBy(point => point.Index).First())
                .OrderBy(point => point.Parameter)
                .ThenBy(point => point.Index)
                .ToArray();
            points = RemoveShortSpans(points, length, firstSourceNode.NodeTag, secondSourceNode.NodeTag);

            var orderedMeshNodes = points
                .Select(point => GetOrCreateSourceNode(
                    point.Node,
                    member.ElemTag,
                    schemaId,
                    meshNodes,
                    meshNodeBySourceTag,
                    usedNodeTags,
                    reservedNodeTags,
                    ref nextNodeTag))
                .ToArray();

            var targetLength = SelectTargetLength(member.TargetMeshLengthM, defaultTargetLengthM);
            for (var pointIndex = 0; pointIndex < points.Length - 1; pointIndex++)
            {
                var start = points[pointIndex];
                var end = points[pointIndex + 1];
                var segmentLength = (end.Parameter - start.Parameter) * length;
                if (!double.IsFinite(segmentLength) || segmentLength < CollinearToleranceM)
                    continue;

                var subdivisionCount = 1;
                if (targetLength is { } target)
                {
                    var requestedSubdivisionCount = Math.Max(1, (int)Math.Min(Math.Ceiling(segmentLength / target), int.MaxValue));
                    var maximumSubdivisionCount = Math.Max(1, (int)Math.Min(Math.Floor(segmentLength / CollinearToleranceM), int.MaxValue));
                    subdivisionCount = Math.Min(requestedSubdivisionCount, maximumSubdivisionCount);
                }
                var startMeshNode = orderedMeshNodes[pointIndex];
                var endMeshNode = orderedMeshNodes[pointIndex + 1];
                var previousNodeTag = int.Parse(startMeshNode.NodeTag);

                for (var subdivisionIndex = 1; subdivisionIndex <= subdivisionCount; subdivisionIndex++)
                {
                    var currentNode = subdivisionIndex == subdivisionCount
                        ? endMeshNode
                        : CreateSyntheticNode(
                            start.Node,
                            end.Node,
                            subdivisionIndex / (double)subdivisionCount,
                            schemaId,
                            member.ElemTag,
                            meshNodes,
                            ref nextSyntheticNodeTag);

                    AddElement(
                        schemaId,
                        member,
                        previousNodeTag,
                        int.Parse(currentNode.NodeTag),
                        meshElements,
                        ref nextElementTag);
                    previousNodeTag = int.Parse(currentNode.NodeTag);
                }
            }
        }

        return (meshNodes, meshElements);
    }

    /// <summary>
    /// Дискретизация схемы с импортированной сеткой: строки <c>Origin="imported"</c> из
    /// <paramref name="existingNodes"/>/<paramref name="existingElements"/> сохраняются без изменений
    /// (Id, теги, связи), сетка строится заново только для элементов с незаблокированной сеткой.
    /// Новый узел, совпадающий с импортированным (по тегу исходного узла или по координатам в пределах
    /// <see cref="CollinearToleranceM"/>), заменяется им; остальные новые теги — выше импортированных.
    /// </summary>
    public static (List<FemMeshNode> Nodes, List<FemElement> Elements) DiscretizeKeepingImported(
        int schemaId,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemMember> members,
        double? defaultTargetLengthM,
        IReadOnlyList<FemMeshNode> existingNodes,
        IReadOnlyList<FemElement> existingElements)
    {
        var importedNodes = existingNodes.Where(n => n.Origin == FemMember.MeshSourceImported).ToList();
        var importedElements = existingElements.Where(e => e.Origin == FemMember.MeshSourceImported).ToList();
        var (generatedNodes, generatedElements) = Discretize(
            schemaId, nodes, members.Where(m => !m.IsMeshLocked).ToList(), defaultTargetLengthM);
        if (importedNodes.Count == 0 && importedElements.Count == 0)
            return (generatedNodes, generatedElements);

        var importedByTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var node in importedNodes) importedByTag.TryAdd(node.NodeTag, node);
        var grid = new Dictionary<(long, long, long), List<FemMeshNode>>();
        foreach (var node in importedNodes)
        {
            var cell = Cell(node);
            if (!grid.TryGetValue(cell, out var list)) grid[cell] = list = [];
            list.Add(node);
        }

        FemMeshNode? Coincident(FemMeshNode node)
        {
            var (cx, cy, cz) = Cell(node);
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    for (long dz = -1; dz <= 1; dz++)
                        if (grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var list))
                            foreach (var candidate in list)
                                if (Distance(candidate, node) <= CollinearToleranceM)
                                    return candidate;
            return null;
        }

        int nextNodeTag = importedNodes.Select(n => int.TryParse(n.NodeTag, out var t) ? t : 0).DefaultIfEmpty(0).Max() + 1;
        int nextElementTag = importedElements.Select(e => int.TryParse(e.ElemTag, out var t) ? t : 0).DefaultIfEmpty(0).Max() + 1;
        var tagMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var resultNodes = new List<FemMeshNode>(importedNodes);
        foreach (var node in generatedNodes)
        {
            var match = node.SourceNodeTag is { } source && importedByTag.TryGetValue(source, out var byTag)
                        && Distance(byTag, node) <= CollinearToleranceM
                ? byTag
                : Coincident(node);
            if (match is not null)
            {
                tagMap[node.NodeTag] = match.NodeTag;
                continue;
            }
            // Синтетические узлы (отрицательные теги) с номерами ЛИРЫ не пересекаются.
            if (int.TryParse(node.NodeTag, out var tag) && tag > 0)
            {
                var renumbered = (nextNodeTag++).ToString();
                tagMap[node.NodeTag] = renumbered;
                node.NodeTag = renumbered;
            }
            else tagMap[node.NodeTag] = node.NodeTag;
            resultNodes.Add(node);
        }

        var resultElements = new List<FemElement>(importedElements);
        foreach (var element in generatedElements)
        {
            var ids = JsonSerializer.Deserialize<int[]>(element.NodeIdsJson) ?? [];
            element.NodeIdsJson = JsonSerializer.Serialize(ids.Select(id => int.Parse(tagMap[id.ToString()])).ToArray());
            element.ElemTag = (nextElementTag++).ToString();
            resultElements.Add(element);
        }
        return (resultNodes, resultElements);
    }

    static (long, long, long) Cell(FemMeshNode n) => (
        (long)Math.Floor(n.X / CollinearToleranceM),
        (long)Math.Floor(n.Y / CollinearToleranceM),
        (long)Math.Floor(n.Z / CollinearToleranceM));

    static double Distance(FemMeshNode a, FemMeshNode b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    static PointOnMember[] RemoveShortSpans(
        PointOnMember[] points, double memberLength, string firstEndpointTag, string secondEndpointTag)
    {
        var chain = points.ToList();
        var index = 1;
        while (index < chain.Count)
        {
            var spanLength = (chain[index].Parameter - chain[index - 1].Parameter) * memberLength;
            if (spanLength >= CollinearToleranceM)
            {
                index++;
                continue;
            }

            // При схлопывании двух совпадающих по положению узлов всегда сохраняем истинный
            // объявленный конец ИМЕННО этого стержня, а не случайно совпавший с ним посторонний
            // узел схемы — иначе дискретизация соседнего стержня, разделяющего эту точку,
            // «отвязывается» от неё (см. StrayNodeCoincidentWithSharedEndpoint_DoesNotReplaceTrueEndpoint).
            var prevIsEndpoint = IsEndpointTag(chain[index - 1].Node.NodeTag, firstEndpointTag, secondEndpointTag);
            var currIsEndpoint = IsEndpointTag(chain[index].Node.NodeTag, firstEndpointTag, secondEndpointTag);

            if (currIsEndpoint && !prevIsEndpoint)
                chain.RemoveAt(index - 1);
            else if (prevIsEndpoint && !currIsEndpoint)
                chain.RemoveAt(index);
            // Ни один, ни оба сразу не являются объявленным концом стержня — прежняя эвристика:
            // сохраняем конечный узел стержня, удаляя близкий к нему внутренний узел.
            else if (index == chain.Count - 1)
                chain.RemoveAt(index - 1);
            else
                chain.RemoveAt(index);
            index = Math.Max(1, index - 1);
        }

        return chain.ToArray();
    }

    static bool IsEndpointTag(string nodeTag, string firstEndpointTag, string secondEndpointTag) =>
        nodeTag == firstEndpointTag || nodeTag == secondEndpointTag;

    static bool TryReadEndpointTags(string? json, out int[] tags)
    {
        tags = [];
        try
        {
            var parsed = JsonSerializer.Deserialize<int[]>(json ?? "[]");
            if (parsed is null || parsed.Length != 2 || parsed.Any(tag => tag <= 0))
                return false;

            tags = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    static double? SelectTargetLength(double? memberTarget, double? defaultTarget)
    {
        if (memberTarget is not null)
            return IsPositiveFinite(memberTarget) ? memberTarget : null;
        return IsPositiveFinite(defaultTarget) ? defaultTarget : null;
    }

    static bool IsPositiveFinite(double? value) =>
        value is { } actual && actual > 0 && double.IsFinite(actual);

    static double Parameter(FemNode node, FemNode first, Vector direction, double lengthSquared)
    {
        var offset = new Vector(node.X - first.X, node.Y - first.Y, node.Z - first.Z);
        return offset.Dot(direction) / lengthSquared;
    }

    static bool IsOnSegment(
        FemNode node,
        FemNode first,
        Vector direction,
        double lengthSquared,
        double length)
    {
        if (!double.IsFinite(node.X) || !double.IsFinite(node.Y) || !double.IsFinite(node.Z))
            return false;

        var parameter = Parameter(node, first, direction, lengthSquared);
        if (parameter < 0 || parameter > 1)
            return false;

        var projection = new Vector(
            first.X + parameter * direction.X,
            first.Y + parameter * direction.Y,
            first.Z + parameter * direction.Z);
        var distance = new Vector(node.X - projection.X, node.Y - projection.Y, node.Z - projection.Z).Length;
        return distance <= CollinearToleranceM;
    }

    static FemMeshNode GetOrCreateSourceNode(
        FemNode source,
        string sourceMemberTag,
        int schemaId,
        ICollection<FemMeshNode> meshNodes,
        IDictionary<string, FemMeshNode> meshNodeBySourceTag,
        ISet<int> usedNodeTags,
        ISet<int> reservedNodeTags,
        ref int nextNodeTag)
    {
        var sourceTag = source.NodeTag ?? "";
        if (meshNodeBySourceTag.TryGetValue(sourceTag, out var existing))
            return existing;

        var nodeTag = TryGetPositiveTag(sourceTag, usedNodeTags)
            ?? NextPositiveNodeTag(usedNodeTags, reservedNodeTags, ref nextNodeTag);
        usedNodeTags.Add(nodeTag);
        var meshNode = new FemMeshNode
        {
            SchemaId = schemaId,
            NodeTag = nodeTag.ToString(),
            X = source.X,
            Y = source.Y,
            Z = source.Z,
            SourceNodeTag = source.NodeTag,
            SourceMemberTag = sourceMemberTag
        };
        meshNodeBySourceTag.Add(sourceTag, meshNode);
        meshNodes.Add(meshNode);
        return meshNode;
    }

    static int? TryGetPositiveTag(string sourceTag, ISet<int> usedNodeTags)
    {
        if (int.TryParse(sourceTag, out var tag) && tag > 0 && !usedNodeTags.Contains(tag))
            return tag;
        return null;
    }

    static int NextPositiveNodeTag(
        ISet<int> usedNodeTags,
        ISet<int> reservedNodeTags,
        ref int nextNodeTag)
    {
        while (usedNodeTags.Contains(nextNodeTag) || reservedNodeTags.Contains(nextNodeTag))
            nextNodeTag++;
        return nextNodeTag++;
    }

    static FemMeshNode CreateSyntheticNode(
        FemNode first,
        FemNode second,
        double fraction,
        int schemaId,
        string sourceMemberTag,
        ICollection<FemMeshNode> meshNodes,
        ref int nextSyntheticNodeTag)
    {
        var node = new FemMeshNode
        {
            SchemaId = schemaId,
            NodeTag = nextSyntheticNodeTag--.ToString(),
            X = first.X + fraction * (second.X - first.X),
            Y = first.Y + fraction * (second.Y - first.Y),
            Z = first.Z + fraction * (second.Z - first.Z),
            SourceMemberTag = sourceMemberTag
        };
        meshNodes.Add(node);
        return node;
    }

    static void AddElement(
        int schemaId,
        FemMember member,
        int firstNodeTag,
        int secondNodeTag,
        ICollection<FemElement> meshElements,
        ref int nextElementTag)
    {
        meshElements.Add(new FemElement
        {
            SchemaId = schemaId,
            ElemTag = nextElementTag++.ToString(),
            NodeIdsJson = JsonSerializer.Serialize(new[] { firstNodeTag, secondNodeTag }),
            SourceMemberTag = member.ElemTag,
            CrossSectionId = member.CrossSectionId,
            GjStrategy = member.GjStrategy,
            GjManualValue = member.GjManualValue,
            GjTorsionTaskId = member.GjTorsionTaskId
        });
    }

    readonly record struct PointOnMember(FemNode Node, double Parameter, int Index);

    readonly record struct Vector(double X, double Y, double Z)
    {
        public double Dot(Vector other) => X * other.X + Y * other.Y + Z * other.Z;

        public double Length => Math.Sqrt(Dot(this));
    }
}
