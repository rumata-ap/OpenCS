using System.Text.Json;
using CScore.Fem;
using CScore.Submodel;

namespace CScore.Tests.Submodel;

/// <summary>Родительская схема в каноническом виде для тестов граничного сценария.</summary>
public sealed record ParentModel(
    List<FemNode> Nodes, List<FemMember> Members, List<FemMeshNode> MeshNodes, List<FemElement> MeshElements);

/// <summary>Тестовые родительские модели и фабрика извлечения без БД.</summary>
public static class BoundaryTestModels
{
    /// <summary>
    /// Балка-стержень "10" вдоль X длиной 8 м (конструктивные узлы "1" при x=0 и "2" при x=8),
    /// промежуточные конструктивные узлы "3" (x=2) и "4" (x=4) для узловых нагрузок.
    /// Сетка: узлы 101..105 через 2 м, элементы 201..204 (201 = 101→102 и т.д.).
    /// </summary>
    public static ParentModel Beam()
    {
        var nodes = new List<FemNode>
        {
            new() { Id = 1, NodeTag = "1", X = 0 },
            new() { Id = 2, NodeTag = "2", X = 8 },
            new() { Id = 3, NodeTag = "3", X = 2 },
            new() { Id = 4, NodeTag = "4", X = 4 },
        };
        var members = new List<FemMember> { new() { Id = 10, ElemTag = "10", NodeIdsJson = "[1,2]", ElemType = "beam" } };
        string?[] sourceNodes = ["1", "3", "4", null, "2"];
        var meshNodes = Enumerable.Range(0, 5).Select(i => new FemMeshNode
        {
            Id = 1000 + i, NodeTag = (101 + i).ToString(), X = 2 * i, SourceNodeTag = sourceNodes[i], SourceMemberTag = "10"
        }).ToList();
        var meshElements = Enumerable.Range(0, 4).Select(i => new FemElement
        {
            Id = 2000 + i, ElemTag = (201 + i).ToString(), ElemType = "beam",
            NodeIdsJson = JsonSerializer.Serialize(new[] { 101 + i, 102 + i }), SourceMemberTag = "10"
        }).ToList();
        return new ParentModel(nodes, members, meshNodes, meshElements);
    }

    /// <summary>Извлечение цепочки из заданных родительских элементов в заданном порядке (теги дочерних
    /// объектов совпадают с родительскими, как в Срезе 2).</summary>
    public static SubmodelExtraction Extraction(ParentModel parent, IReadOnlyList<(string ElemTag, bool IsReversed)> chain,
        string loadExpressionJson = "{}")
    {
        var nodeByTag = parent.MeshNodes.ToDictionary(n => n.NodeTag);
        var segments = new List<SubmodelExtractionSegment>();
        var nodes = new List<SubmodelExtractionNode>();
        double station = 0;
        foreach (var ((tag, reversed), ordinal) in chain.Select((c, i) => (c, i)))
        {
            var element = parent.MeshElements.Single(e => e.ElemTag == tag);
            var ends = FemMeshTopology.ReadNodeTags(element, 2)!;
            var a = nodeByTag[ends[0]];
            var b = nodeByTag[ends[1]];
            double length = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2) + Math.Pow(b.Z - a.Z, 2));
            segments.Add(new SubmodelExtractionSegment(ordinal + 1, ordinal, element.Id + 50000, tag, element.Id, tag,
                element.SourceMemberTag, reversed, station, station + length, length, 0, 0, BetaSource.Member));
            station += length;
            foreach (var node in new[] { a, b })
                if (nodes.All(n => n.ParentNodeTag != node.NodeTag))
                    nodes.Add(new SubmodelExtractionNode(nodes.Count + 1, node.Id + 50000, node.NodeTag, node.Id, node.NodeTag,
                        node.X, node.Y, node.Z, node.SourceNodeTag, node.SourceMemberTag));
        }
        return new SubmodelExtraction
        {
            Id = 77, ParentSchemaId = 1, SubmodelSchemaId = 2, ParentAnalysisId = 3, ParentResultId = 4,
            LoadExpressionJson = loadExpressionJson, ReferenceScale = 1.0,
            Tolerances = null!, Metrics = null!, Nodes = nodes, Segments = segments
        };
    }

    /// <summary>Цепочка 202→203 (x = 2..6), концы 102 и 104, внутренний узел 103.</summary>
    public static readonly (string, bool)[] MiddleChain = [("202", false), ("203", false)];

    /// <summary>Та же цепочка, пройденная в обратном направлении (203 затем 202, оба развёрнуты).</summary>
    public static readonly (string, bool)[] MiddleChainReversed = [("203", true), ("202", true)];
}
