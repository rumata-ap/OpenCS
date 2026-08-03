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

        // Шаг 2: дедупликация shell material/section по fingerprint — строго внутри
        // shell-домена: все уровни производят один и тот же тип NativeShellMaterialDefinition/
        // RCShellLayeredSection, поэтому обобщение — прямая экстраполяция существующего
        // двухстороннего цикла FloorJunctionShellModelAssembler на N источников.
        var materialTagMap = new Dictionary<(string Level, int SourceTag), int>();
        var materialByFingerprint = new Dictionary<string, NativeShellMaterialDefinition>(StringComparer.Ordinal);
        var finalMaterials = new List<NativeShellMaterialDefinition>();
        var materialProvenance = new Dictionary<int, string>();
        int nextMaterialTag = 1;

        void RegisterMaterial(string levelId, NativeShellMaterialDefinition definition)
        {
            string fingerprint = definition.Fingerprint;
            if (materialByFingerprint.TryGetValue(fingerprint, out var existing))
            {
                materialTagMap[(levelId, definition.Tag)] = existing.Tag;
                return;
            }
            var registered = definition with { Tag = nextMaterialTag++ };
            registered.Validate();
            materialByFingerprint[fingerprint] = registered;
            finalMaterials.Add(registered);
            materialTagMap[(levelId, definition.Tag)] = registered.Tag;
            materialProvenance[registered.Tag] = $"{levelId}|source:{definition.SourceId}|fp:{fingerprint}";
        }

        var allShellMaterials = perLevel
            .SelectMany(item => item.Built.Model.Materials.Select(material => (levelId: item.Level.Id, material)))
            .ToList();

        foreach (var (levelId, material) in allShellMaterials
                     .Where(item => item.material.Spec.DependsOnMaterialTag is null)
                     .OrderBy(item => item.material.Fingerprint, StringComparer.Ordinal)
                     .ThenBy(item => item.material.SourceId, StringComparer.Ordinal))
            RegisterMaterial(levelId, material);

        var pendingDependent = allShellMaterials
            .Where(item => item.material.Spec.DependsOnMaterialTag is int)
            .OrderBy(item => item.material.SourceId, StringComparer.Ordinal)
            .ToList();
        while (pendingDependent.Count > 0)
        {
            var deferred = new List<(string levelId, NativeShellMaterialDefinition material)>();
            bool advanced = false;
            foreach (var (levelId, material) in pendingDependent)
            {
                int dependency = material.Spec.DependsOnMaterialTag!.Value;
                if (materialTagMap.TryGetValue((levelId, dependency), out int finalDependency))
                {
                    RegisterMaterial(levelId, material with { Spec = material.Spec.WithDependencyTag(finalDependency) });
                    advanced = true;
                }
                else
                {
                    deferred.Add((levelId, material));
                }
            }
            if (!advanced)
            {
                foreach (var (levelId, material) in deferred)
                    diagnostics.Add(new("multistory_column_material_dependency_missing",
                        $"Материал {material.SourceId} (уровень {levelId}) ссылается на не " +
                        $"зарегистрированную зависимость (material tag {material.Spec.DependsOnMaterialTag})."));
                return Empty(diagnostics);
            }
            pendingDependent = deferred;
        }

        var sectionByFingerprint = new Dictionary<string, RCShellLayeredSection>(StringComparer.Ordinal);
        var finalSections = new List<RCShellLayeredSection>();
        var sectionProvenance = new Dictionary<int, string>();
        int nextSectionTag = 1;
        var elementSectionTagRewrite = new Dictionary<(string Level, int OldTag), int>();

        foreach (var (level, built) in perLevel)
        {
            foreach (var section in built.Model.Sections)
            {
                var remappedLayers = section.Layers
                    .Select(layer => layer with { MaterialTag = materialTagMap[(level.Id, layer.MaterialTag)] })
                    .ToList();
                var remapped = section with { Layers = remappedLayers };
                string fingerprint = PlateSectionOpenSeesMapper.RecalcFingerprint(remapped, finalMaterials);
                if (sectionByFingerprint.TryGetValue(fingerprint, out var existingSection))
                {
                    elementSectionTagRewrite[(level.Id, section.Tag)] = existingSection.Tag;
                    continue;
                }
                var registeredSection = remapped with { Tag = nextSectionTag++ };
                sectionByFingerprint[fingerprint] = registeredSection;
                finalSections.Add(registeredSection);
                sectionProvenance[registeredSection.Tag] =
                    $"{level.Id}|source:{section.Tag}|fp:{fingerprint}";
                elementSectionTagRewrite[(level.Id, section.Tag)] = registeredSection.Tag;
            }
        }

        // Перепривязка элементов ко финальным section tags (level-scoped: тег элемента уже
        // содержит nodeTagOffset/elementTagOffset из шага 1, поэтому итерируем перестроенный
        // allElements по уровням в том же порядке).
        int elementCursor = 0;
        var rewrittenElements = new List<NormalizedShellElement>();
        foreach (var (level, built) in perLevel)
        {
            foreach (var originalElement in built.Model.Elements)
            {
                var element = allElements[elementCursor++];
                int finalSectionTag = elementSectionTagRewrite[(level.Id, originalElement.SectionTag)];
                var finalSection = finalSections.Single(s => s.Tag == finalSectionTag);
                rewrittenElements.Add(element with
                {
                    SectionTag = finalSectionTag,
                    SectionFingerprint = finalSection.Fingerprint
                });
            }
        }
        allElements = rewrittenElements;

        // Шаг 3: секции/материалы балочных сегментов колонны через CrossSectionToOpenSeesAdapter —
        // без дедупа с shell и между сегментами (OpenSeesMaterialDefinition структурно другой тип,
        // общего дедуп-пула с NativeShellMaterialDefinition нет и быть не может). Теги продолжают
        // сквозную нумерацию после материалов/секций всех уровней.
        var nonlinearBeamSections = new Dictionary<int, OpenSeesSectionModel>();
        var beamSectionTagBySegmentId = new Dictionary<string, int>();
        int nextBeamMaterialTag = nextMaterialTag;
        int nextBeamSectionTag = nextSectionTag;
        foreach (var segment in segments)
        {
            var materialIds = segment.Section.Areas.Select(area => area.MaterialId).Distinct();
            var segmentMaterials = new Dictionary<int, Material>();
            bool segmentHasMissingMaterial = false;
            foreach (int materialId in materialIds)
            {
                Material? material = lookupMaterial?.Invoke(materialId);
                if (material is null)
                {
                    diagnostics.Add(new("multistory_column_segment_material_missing",
                        $"Сегмент '{segment.Id}': материал {materialId} не найден."));
                    segmentHasMissingMaterial = true;
                    continue;
                }
                segmentMaterials[materialId] = material;
            }
            // Немедленный выход, а не «continue» по общему diagnostics.Count: локальный флаг
            // не даёт CrossSectionToOpenSeesAdapter.Build получить неполный materials dictionary
            // ни для этого, ни (по ошибке) для последующих сегментов — тот же паттерн раннего
            // выхода, что и у material dependency missing в Task 6.
            if (segmentHasMissingMaterial) return Empty(diagnostics);

            var options = new CrossSectionToOpenSeesAdapter.Options
            {
                GJ = segment.GJ,
                FirstMaterialTag = nextBeamMaterialTag
            };
            OpenSeesSectionModel built = CrossSectionToOpenSeesAdapter.Build(
                segment.Section, calcType, segmentMaterials, customDiagramPool, options);
            nextBeamMaterialTag += built.Materials.Count;

            int sectionTag = nextBeamSectionTag++;
            nonlinearBeamSections[sectionTag] = built;
            beamSectionTagBySegmentId[segment.Id] = sectionTag;
            sectionProvenance[sectionTag] = $"column-segment:{segment.Id}|source:{segment.Id}";
        }
        if (diagnostics.Count > 0) return Empty(diagnostics);

        var model = new ShellOpenSeesModel
        {
            Nodes = allNodes,
            Elements = allElements,
            Materials = finalMaterials,
            Sections = finalSections,
            NonlinearBeamSections = nonlinearBeamSections,
            NonlinearBeamGeomTransfKind = geomTransfKind,
            NonlinearBeamElementFormulation = elementFormulation
        };

        return new MultiStoryColumnShellAssemblyResult(
            model, nodeIndexToTagByLevel, elementIndexToTagByLevel,
            new Dictionary<string, int>(), sectionProvenance, materialProvenance, diagnostics);
    }

    static MultiStoryColumnShellAssemblyResult Empty(IReadOnlyList<FemValidationDiagnostic> diagnostics) =>
        new(new ShellOpenSeesModel(), new Dictionary<string, IReadOnlyDictionary<int, int>>(),
            new Dictionary<string, IReadOnlyDictionary<int, int>>(), new Dictionary<string, int>(),
            new Dictionary<int, string>(), new Dictionary<int, string>(), diagnostics);
}
