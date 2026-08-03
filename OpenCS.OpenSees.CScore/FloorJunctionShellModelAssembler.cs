using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore
{
    /// <summary>Результат сборки двух shell-моделей: модель без stages/boundary actions,
    /// component node/element maps, junction-пары, provenance материалов/секций, диагностики.</summary>
    public sealed record FloorJunctionShellAssemblyResult(
        ShellOpenSeesModel Model,
        IReadOnlyDictionary<int, int> PlateNodeIndexToTag,
        IReadOnlyDictionary<int, int> WallNodeIndexToTag,
        IReadOnlyDictionary<int, int> PlateElementIndexToTag,
        IReadOnlyDictionary<int, int> WallElementIndexToTag,
        IReadOnlyList<(int PlateNodeTag, int WallNodeTag)> JunctionPairs,
        IReadOnlyDictionary<int, string> SectionProvenance,
        IReadOnlyDictionary<int, string> MaterialProvenance,
        IReadOnlyList<FemValidationDiagnostic> Diagnostics)
    {
        public bool IsCalculable => !Diagnostics.Any(diagnostic => diagnostic.IsError);
    }

    /// <summary>Собирает два независимых PlanarMeshSnapshot в одну ShellOpenSeesModel:
    /// детерминированный remap node/element/section/material tags, пересчёт fingerprints после
    /// финальных тегов и equalDOF на exact pairs (plate master -> wall slave). Не меняет исходные
    /// snapshots; Gmsh/domain IDs не используются как OpenSees tags. На текущем шаге материалы и
    /// секции двух сторон НЕ дедуплицируются — каждой стороне выделяется свой непересекающийся
    /// диапазон тегов (merge добавляется в Task 8).</summary>
    public static class FloorJunctionShellModelAssembler
    {
        public static FloorJunctionShellAssemblyResult Assemble(
            PlanarMeshSnapshot plateSnapshot,
            PlanarRegion plateRegion,
            PlateSection plateSection,
            PlanarMeshSnapshot wallSnapshot,
            PlanarRegion wallRegion,
            PlateSection wallSection,
            IPlateSectionShellMaterialResolver resolver,
            PlanarConnectionMeshMapping mapping)
        {
            ArgumentNullException.ThrowIfNull(plateSnapshot);
            ArgumentNullException.ThrowIfNull(plateRegion);
            ArgumentNullException.ThrowIfNull(plateSection);
            ArgumentNullException.ThrowIfNull(wallSnapshot);
            ArgumentNullException.ThrowIfNull(wallRegion);
            ArgumentNullException.ThrowIfNull(wallSection);
            ArgumentNullException.ThrowIfNull(resolver);
            ArgumentNullException.ThrowIfNull(mapping);

            var diagnostics = new List<FemValidationDiagnostic>();
            if (!plateSnapshot.IsCalculable)
                diagnostics.Add(new("floor_junction_snapshot_not_calculable", "Plate snapshot нерасчётен."));
            if (!wallSnapshot.IsCalculable)
                diagnostics.Add(new("floor_junction_snapshot_not_calculable", "Wall snapshot нерасчётен."));
            if (mapping.MeshMode != PlanarConnectionMeshMode.ConformingPartition)
                diagnostics.Add(new("floor_junction_mesh_mode_unsupported",
                    $"MeshMode {mapping.MeshMode} не поддерживается; требуется ConformingPartition."));
            if (diagnostics.Any(d => d.IsError))
                return Empty(diagnostics);

            var plateField = PlateRebarField.From(plateSection, plateRegion);
            var wallField = PlateRebarField.From(wallSection, wallRegion);
            PlanarMeshShellModelResult plate = PlanarMeshSnapshotShellModelAdapter.Build(
                plateSnapshot, plateRegion.Frame, plateSection, plateField, resolver, firstSectionTag: 1);
            PlanarMeshShellModelResult wall = PlanarMeshSnapshotShellModelAdapter.Build(
                wallSnapshot, wallRegion.Frame, wallSection, wallField, resolver, firstSectionTag: 1);

            diagnostics.AddRange(plate.RebarDiagnostics.Select(item => item.Diagnostic));
            diagnostics.AddRange(wall.RebarDiagnostics.Select(item => item.Diagnostic));
            diagnostics.AddRange(plate.MappingDiagnostics.Select(message =>
                new FemValidationDiagnostic("floor_junction_tag_collision", $"Plate mapping: {message}")));
            diagnostics.AddRange(wall.MappingDiagnostics.Select(message =>
                new FemValidationDiagnostic("floor_junction_tag_collision", $"Wall mapping: {message}")));
            if (diagnostics.Any(diagnostic => diagnostic.IsError))
                return Empty(diagnostics);

            int plateNodeCount = plate.Model.Nodes.Count;
            int plateElementCount = plate.Model.Elements.Count;

            // 1. Node remap: plate 1..Np, wall Np+1..Np+Nw.
            var plateNodeIndexToTag = plate.NodeIndexToTag;
            var wallNodeIndexToTag = wall.NodeIndexToTag.ToDictionary(
                pair => pair.Key, pair => pair.Value + plateNodeCount);
            var wallNodeTagOffset = plateNodeCount;
            NormalizedShellNode[] remappedWallNodes = wall.Model.Nodes
                .Select(node => node with { Tag = node.Tag + wallNodeTagOffset })
                .ToArray();
            var nodes = plate.Model.Nodes.Concat(remappedWallNodes).ToArray();

            // 2. Element remap: plate 1..Mp, wall Mp+1..Mp+Mw; wall node tags + offset.
            var plateElementIndexToTag = plate.ElementIndexToTag;
            var wallElementIndexToTag = wall.ElementIndexToTag.ToDictionary(
                pair => pair.Key, pair => pair.Value + plateElementCount);
            NormalizedShellElement[] remappedWallElements = wall.Model.Elements
                .Select(element => element with
                {
                    Tag = element.Tag + plateElementCount,
                    NodeTags = element.NodeTags.Select(tag => tag + wallNodeTagOffset).ToList(),
                    SectionTag = element.SectionTag + plate.Model.Sections.Count,
                    SectionFingerprint = wall.Model.Sections
                        .First(section => section.Tag == element.SectionTag).Fingerprint
                })
                .ToArray();
            var elements = plate.Model.Elements.Concat(remappedWallElements).ToArray();

            // 3. Материалы/секции: на этом шаге — непересекающиеся диапазоны (merge в Task 8).
            int plateMaterialCount = plate.Model.Materials.Count;
            var materials = plate.Model.Materials.Concat(
                wall.Model.Materials.Select(material => material with
                {
                    Tag = material.Tag + plateMaterialCount,
                    Spec = material.Spec.DependsOnMaterialTag is int dependency
                        ? material.Spec.WithDependencyTag(dependency + plateMaterialCount)
                        : material.Spec
                })).ToArray();
            var wallSections = wall.Model.Sections.Select(section =>
            {
                var layers = section.Layers.Select(layer => layer with
                {
                    MaterialTag = layer.MaterialTag + plateMaterialCount
                }).ToList();
                var shifted = section with { Tag = section.Tag + plate.Model.Sections.Count, Layers = layers };
                return shifted with { Fingerprint = PlateSectionOpenSeesMapper.RecalcFingerprint(shifted, materials) };
            }).ToArray();
            var sections = plate.Model.Sections.Concat(wallSections).ToArray();

            // 4. Обновить SectionFingerprint wall-элементов на пересчитанный fingerprint секции.
            var sectionByTag = sections.ToDictionary(section => section.Tag);
            elements = elements
                .Select(element => sectionByTag.TryGetValue(element.SectionTag, out var target)
                    ? element with { SectionFingerprint = target.Fingerprint }
                    : element)
                .ToArray();

            // 5. Junction pairs + equalDOF (plate master -> wall slave).
            var junctionPairs = new List<(int PlateNodeTag, int WallNodeTag)>();
            var slaveDofCoverage = new Dictionary<(int Node, int Dof), string>();
            var equalDofs = new List<ShellEqualDofConstraint>();
            foreach (var pair in mapping.ExactNodePairs)
            {
                if (!plateNodeIndexToTag.TryGetValue(pair.SideANodeIndex, out int plateTag) ||
                    !wallNodeIndexToTag.TryGetValue(pair.SideBNodeIndex, out int wallTag))
                {
                    diagnostics.Add(new("floor_junction_connection_mapping_missing",
                        $"Exact pair {pair.SideANodeIndex}->{pair.SideBNodeIndex} ссылается на неизвестные узлы."));
                    continue;
                }
                junctionPairs.Add((plateTag, wallTag));
                foreach (int dof in new[] { 1, 2, 3, 4, 5, 6 })
                {
                    var key = (wallTag, dof);
                    if (slaveDofCoverage.TryGetValue(key, out string? owner))
                        diagnostics.Add(new("floor_junction_slave_dof_conflict",
                            $"Узел {wallTag}, DOF {dof} одновременно задан «{owner}» и equalDOF {plateTag}->{wallTag}."));
                    else
                        slaveDofCoverage[key] = $"equalDOF {plateTag}->{wallTag}";
                }
                equalDofs.Add(new(plateTag, wallTag, [1, 2, 3, 4, 5, 6]));
            }

            // 6. Проверка коллизий тегов в общем namespace.
            diagnostics.AddRange(CheckTagCollisions(nodes, elements, materials, sections));

            if (diagnostics.Any(d => d.IsError))
                return Empty(diagnostics);

            var model = new ShellOpenSeesModel
            {
                Nodes = nodes.OrderBy(node => node.Tag).ToArray(),
                Materials = materials.OrderBy(material => material.Tag).ToArray(),
                Sections = sections.OrderBy(section => section.Tag).ToArray(),
                Elements = elements.OrderBy(element => element.Tag).ToArray(),
                EqualDofConstraints = equalDofs
            };

            var materialProvenance = materials.ToDictionary(
                material => material.Tag, material => $"source:{material.SourceId}|fp:{material.Fingerprint}");
            var sectionProvenance = sections.ToDictionary(
                section => section.Tag, section => $"source:{section.SourcePlateSectionFingerprint}|fp:{section.Fingerprint}");

            return new FloorJunctionShellAssemblyResult(
                model,
                plateNodeIndexToTag,
                wallNodeIndexToTag,
                plateElementIndexToTag,
                wallElementIndexToTag,
                junctionPairs,
                sectionProvenance,
                materialProvenance,
                diagnostics);
        }

        static IReadOnlyList<FemValidationDiagnostic> CheckTagCollisions(
            IReadOnlyList<NormalizedShellNode> nodes,
            IReadOnlyList<NormalizedShellElement> elements,
            IReadOnlyList<NativeShellMaterialDefinition> materials,
            IReadOnlyList<RCShellLayeredSection> sections)
        {
            var diagnostics = new List<FemValidationDiagnostic>();
            var nodeTags = nodes.Select(node => node.Tag).ToHashSet();
            var elementTags = elements.Select(element => element.Tag).ToHashSet();
            var materialTags = materials.Select(material => material.Tag).ToHashSet();
            var sectionTags = sections.Select(section => section.Tag).ToHashSet();
            if (nodeTags.Count != nodes.Count)
                diagnostics.Add(new("floor_junction_tag_collision", "Дублирующийся node tag."));
            if (elementTags.Count != elements.Count)
                diagnostics.Add(new("floor_junction_tag_collision", "Дублирующийся element tag."));
            if (materialTags.Count != materials.Count)
                diagnostics.Add(new("floor_junction_tag_collision", "Дублирующийся material tag."));
            if (sectionTags.Count != sections.Count)
                diagnostics.Add(new("floor_junction_tag_collision", "Дублирующийся section tag."));
            // В OpenSees `nDMaterial`/`uniaxialMaterial` и `section` — раздельные пространства
            // команд, поэтому пересечение section/material тегов НЕ является коллизией (это
            // допускают и существующий адаптер, и ShellOpenSeesModel.Validate).
            foreach (var element in elements)
            {
                if (!sectionTags.Contains(element.SectionTag))
                    diagnostics.Add(new("floor_junction_tag_collision",
                        $"Элемент {element.Tag} ссылается на неизвестную секцию {element.SectionTag}."));
                foreach (int nodeTag in element.NodeTags)
                    if (!nodeTags.Contains(nodeTag))
                        diagnostics.Add(new("floor_junction_tag_collision",
                            $"Элемент {element.Tag} ссылается на неизвестный узел {nodeTag}."));
            }
            foreach (var section in sections)
                foreach (var layer in section.Layers)
                    if (!materialTags.Contains(layer.MaterialTag))
                        diagnostics.Add(new("floor_junction_tag_collision",
                            $"Секция {section.Tag}, слой {layer.Index} ссылается на неизвестный материал {layer.MaterialTag}."));
            return diagnostics;
        }

        static FloorJunctionShellAssemblyResult Empty(IReadOnlyList<FemValidationDiagnostic> diagnostics) =>
            new(
                new ShellOpenSeesModel(),
                new Dictionary<int, int>(),
                new Dictionary<int, int>(),
                new Dictionary<int, int>(),
                new Dictionary<int, int>(),
                [],
                new Dictionary<int, string>(),
                new Dictionary<int, string>(),
                diagnostics);
    }
}
