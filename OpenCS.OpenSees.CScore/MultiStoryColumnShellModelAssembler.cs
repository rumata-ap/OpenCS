using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.Planar.Fragments;
using CScore.PlateRebar;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Составная shell + нелинейная балочная модель многоэтажной колонны: N независимых
/// Gmsh-снапшотов перекрытий (уровней), сшитых через embedded_point anchor-узлы, плюс балочные
/// FemNonlinearElement между anchor-узлами соседних уровней — в одной ShellOpenSeesModel, без
/// отдельной Gmsh-сетки у самой колонны.</summary>
public sealed record MultiStoryColumnShellAssemblyResult(
    ShellOpenSeesModel Model,
    IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> NodeIndexToTagByLevel,
    IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> ElementIndexToTagByLevel,
    IReadOnlyDictionary<string, int> AnchorNodeTagByLevel,
    IReadOnlyDictionary<int, string> SectionProvenance,
    IReadOnlyDictionary<int, string> MaterialProvenance,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics)
{
    public bool IsCalculable => !Diagnostics.Any(diagnostic => diagnostic.IsError);
}

public static class MultiStoryColumnShellModelAssembler
{
    public static MultiStoryColumnShellAssemblyResult Assemble(
        IReadOnlyList<(ColumnFloorLevel Level, PlanarMeshSnapshot Snapshot)> levels,
        IReadOnlyList<ColumnSegment> segments,
        ColumnBaseFixity baseSupport,
        string geomTransfKind,
        string elementFormulation,
        IPlateSectionShellMaterialResolver resolver,
        CalcType calcType = default,
        Func<int, Material?>? lookupMaterial = null,
        IReadOnlyList<Diagramm>? customDiagramPool = null)
    {
        var diagnostics = new List<FemValidationDiagnostic>();

        foreach (var (level, snapshot) in levels)
            if (!snapshot.IsCalculable)
                diagnostics.Add(new("multistory_column_snapshot_not_calculable",
                    $"Уровень '{level.Id}': snapshot не является расчётным."));
        if (diagnostics.Count > 0) return Empty(diagnostics);

        // Шаг 1: независимая сборка shell-модели каждого уровня + последовательный remap
        // node/element tags (та же схема офсетов, что у FloorJunctionShellModelAssembler,
        // обобщённая на произвольное число источников).
        var perLevel = new List<(ColumnFloorLevel Level, PlanarMeshShellModelResult Built)>();
        foreach (var (level, snapshot) in levels)
        {
            var field = PlateRebarField.From(level.PlateSection, level.PlateRegion);
            var built = PlanarMeshSnapshotShellModelAdapter.Build(
                snapshot, level.PlateRegion.Frame, level.PlateSection, field, resolver, firstSectionTag: 1);
            perLevel.Add((level, built));
        }

        var nodeIndexToTagByLevel = new Dictionary<string, IReadOnlyDictionary<int, int>>();
        var elementIndexToTagByLevel = new Dictionary<string, IReadOnlyDictionary<int, int>>();
        var allNodes = new List<NormalizedShellNode>();
        var allElements = new List<NormalizedShellElement>();
        int nodeTagOffset = 0, elementTagOffset = 0;
        foreach (var (level, built) in perLevel)
        {
            var nodeMap = new Dictionary<int, int>();
            foreach (var pair in built.NodeIndexToTag)
                nodeMap[pair.Key] = pair.Value + nodeTagOffset;
            nodeIndexToTagByLevel[level.Id] = nodeMap;
            foreach (var node in built.Model.Nodes)
                allNodes.Add(node with { Tag = node.Tag + nodeTagOffset });

            var elementMap = new Dictionary<int, int>();
            foreach (var pair in built.ElementIndexToTag)
                elementMap[pair.Key] = pair.Value + elementTagOffset;
            elementIndexToTagByLevel[level.Id] = elementMap;
            foreach (var element in built.Model.Elements)
                allElements.Add(element with
                {
                    Tag = element.Tag + elementTagOffset,
                    NodeTags = element.NodeTags.Select(tag => tag + nodeTagOffset).ToArray()
                });

            nodeTagOffset += built.Model.Nodes.Count;
            elementTagOffset += built.Model.Elements.Count;
        }

        var model = new ShellOpenSeesModel
        {
            Nodes = allNodes,
            Elements = allElements,
            NonlinearBeamGeomTransfKind = geomTransfKind,
            NonlinearBeamElementFormulation = elementFormulation
        };

        return new MultiStoryColumnShellAssemblyResult(
            model, nodeIndexToTagByLevel, elementIndexToTagByLevel,
            new Dictionary<string, int>(), new Dictionary<int, string>(), new Dictionary<int, string>(),
            diagnostics);
    }

    static MultiStoryColumnShellAssemblyResult Empty(IReadOnlyList<FemValidationDiagnostic> diagnostics) =>
        new(new ShellOpenSeesModel(), new Dictionary<string, IReadOnlyDictionary<int, int>>(),
            new Dictionary<string, IReadOnlyDictionary<int, int>>(), new Dictionary<string, int>(),
            new Dictionary<int, string>(), new Dictionary<int, string>(), diagnostics);
}
