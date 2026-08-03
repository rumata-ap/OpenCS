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
    /// детерминированный remap node/element/section/material tags, merge-дедупликация
    /// идентичных материалов и секций по fingerprints (с пересчётом после финальной
    /// нумерации тегов) и equalDOF на exact pairs (plate master -> wall slave). Не меняет
    /// исходные snapshots; Gmsh/domain IDs не используются как OpenSees tags.</summary>
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
            // Диагностики самого connection mapping: сохраняем все (включая warning), но расчёт
            // блокируем только если среди них есть error (mapping.IsCalculable == false).
            diagnostics.AddRange(mapping.Diagnostics);
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
            // Mapping-сообщения (например про smeared rebar) — информационные, не блокируют расчёт.
            diagnostics.AddRange(plate.MappingDiagnostics.Select(message =>
                new FemValidationDiagnostic("floor_junction_mapping_warning", $"Plate mapping: {message}", IsError: false)));
            diagnostics.AddRange(wall.MappingDiagnostics.Select(message =>
                new FemValidationDiagnostic("floor_junction_mapping_warning", $"Wall mapping: {message}", IsError: false)));
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

            // 2. Element tag remap (только теги; section tags и fingerprints перезаписываются
            //    в шаге 5 после merge секций).
            var plateElementIndexToTag = plate.ElementIndexToTag;
            var wallElementIndexToTag = wall.ElementIndexToTag.ToDictionary(
                pair => pair.Key, pair => pair.Value + plateElementCount);

            // 3. Merge материалов: независимые определения — в детерминированном порядке
            //    fingerprint/исходного тега, зависимые — после финальных тегов баз с
            //    переписанным DependsOnMaterialTag (через WithDependencyTag) и пересчитанным
            //    Spec.Fingerprint. Идентичные финальные fingerprints дедуплицируются в один
            //    материал; ключ (side, sourceTag) сохраняет происхождение для remap секций.
            var materialTagMap = new Dictionary<(string Side, int SourceTag), int>();
            var materialByFingerprint = new Dictionary<string, NativeShellMaterialDefinition>(StringComparer.Ordinal);
            var finalMaterials = new List<NativeShellMaterialDefinition>();
            var materialProvenance = new Dictionary<int, string>();
            int nextMaterialTag = 1;

            void RegisterMaterial(string side, NativeShellMaterialDefinition definition)
            {
                string fingerprint = definition.Fingerprint;
                if (materialByFingerprint.TryGetValue(fingerprint, out NativeShellMaterialDefinition? existing))
                {
                    materialTagMap[(side, definition.Tag)] = existing.Tag;
                    return;
                }
                NativeShellMaterialDefinition registered = definition with { Tag = nextMaterialTag++ };
                registered.Validate();
                materialByFingerprint[fingerprint] = registered;
                finalMaterials.Add(registered);
                materialTagMap[(side, definition.Tag)] = registered.Tag;
                materialProvenance[registered.Tag] = $"{side}|source:{definition.SourceId}|fp:{fingerprint}";
            }

            var allMaterials = plate.Model.Materials
                .Select(material => (side: "plate", material: material))
                .Concat(wall.Model.Materials.Select(material => (side: "wall", material: material)))
                .ToList();

            foreach (var (side, material) in allMaterials
                         .Where(item => item.material.Spec.DependsOnMaterialTag is null)
                         .OrderBy(item => item.material.Fingerprint, StringComparer.Ordinal)
                         .ThenBy(item => item.material.SourceId, StringComparer.Ordinal))
                RegisterMaterial(side, material);

            // Зависимые материалы обрабатываются топологически итеративно: pending продвигается
            // только когда (side, originalDependencyTag) уже замаплен на финальный тег. При
            // продвижении dependency переписывается через WithDependencyTag, а fingerprint
            // берётся вычисляемым (Spec.Fingerprint) — поэтому два зависимых материала с одной
            // и той же финальной базой корректно дедуплицируются. Если pending не продвигается
            // (нет прогресса за проход) — блокирующая диагностика, broken material НЕ
            // регистрируется (никакой тихой fallback-регистрации с сырым dependency tag).
            var pendingDependent = allMaterials
                .Where(item => item.material.Spec.DependsOnMaterialTag is int)
                .OrderBy(item => item.material.SourceId, StringComparer.Ordinal)
                .ToList();
            while (pendingDependent.Count > 0)
            {
                var deferred = new List<(string Side, NativeShellMaterialDefinition Material)>();
                bool advanced = false;
                foreach (var (side, material) in pendingDependent)
                {
                    int dependency = material.Spec.DependsOnMaterialTag!.Value;
                    if (materialTagMap.TryGetValue((side, dependency), out int finalDependency))
                    {
                        RegisterMaterial(side, material with
                        {
                            Spec = material.Spec.WithDependencyTag(finalDependency)
                        });
                        advanced = true;
                    }
                    else
                    {
                        deferred.Add((side, material));
                    }
                }
                if (!advanced)
                {
                    foreach (var (side, material) in deferred)
                        diagnostics.Add(new("floor_junction_material_dependency_missing",
                            $"Материал {material.SourceId} (сторона {side}) ссылается на не зарегистрированную " +
                            $"зависимость (material tag {material.Spec.DependsOnMaterialTag})."));
                    return Empty(diagnostics);
                }
                pendingDependent = deferred;
            }

            // 4. Merge секций: material tags слоёв remap-ятся через side-специфичную карту,
            //    финальный fingerprint пересчитывается через RecalcFingerprint после финальных
            //    material tags, идентичные fingerprints дедуплицируются. Детерминированный
            //    порядок: по пересчитанному fingerprint, затем по fingerprint исходного
            //    PlateSection. Неразрешённые material tags добавляют диагностику (не
            //    проглатываются как успех).
            var sectionTagMap = new Dictionary<(string Side, int SourceTag), int>();
            var sectionByFingerprint = new Dictionary<string, RCShellLayeredSection>(StringComparer.Ordinal);
            var finalSections = new List<RCShellLayeredSection>();
            var sectionProvenance = new Dictionary<int, string>();
            int nextSectionTag = 1;

            // Fingerprint каждой remapped-секции вычисляется один раз (RecalcFingerprint зависит
            // только от финальных material tags, уже зафиксированных выше), после чего
            // используется и в сортировке, и в теле цикла.
            var remappedSections = new List<(string Side, RCShellLayeredSection Source, RCShellLayeredSection Remapped, string Fingerprint)>();
            foreach (var (side, section) in plate.Model.Sections
                         .Select(section => (side: "plate", section: section))
                         .Concat(wall.Model.Sections.Select(section => (side: "wall", section: section))))
            {
                bool resolved = true;
                var layers = new List<RCShellLayer>(section.Layers.Count);
                foreach (RCShellLayer layer in section.Layers)
                {
                    if (!materialTagMap.TryGetValue((side, layer.MaterialTag), out int finalMaterialTag))
                    {
                        diagnostics.Add(new("floor_junction_tag_collision",
                            $"Секция {section.Tag} (сторона {side}): слой {layer.Index} ссылается на неразрешённый material tag {layer.MaterialTag}."));
                        resolved = false;
                        break;
                    }
                    layers.Add(layer with { MaterialTag = finalMaterialTag });
                }
                if (!resolved)
                    continue;
                var remapped = section with { Layers = layers };
                remappedSections.Add((side, section, remapped,
                    PlateSectionOpenSeesMapper.RecalcFingerprint(remapped, finalMaterials)));
            }
            if (diagnostics.Any(diagnostic => diagnostic.IsError))
                return Empty(diagnostics);

            foreach (var (side, source, remapped, fingerprint) in remappedSections
                         .OrderBy(item => item.Fingerprint, StringComparer.Ordinal)
                         .ThenBy(item => item.Remapped.SourcePlateSectionFingerprint, StringComparer.Ordinal))
            {
                if (sectionByFingerprint.TryGetValue(fingerprint, out RCShellLayeredSection? existing))
                {
                    sectionTagMap[(side, source.Tag)] = existing.Tag;
                    continue;
                }
                RCShellLayeredSection registered = remapped with { Tag = nextSectionTag++, Fingerprint = fingerprint };
                registered.Validate();
                sectionByFingerprint[fingerprint] = registered;
                finalSections.Add(registered);
                sectionTagMap[(side, source.Tag)] = registered.Tag;
                sectionProvenance[registered.Tag] = $"{side}|source:{source.SourcePlateSectionFingerprint}|fp:{fingerprint}";
            }

            var materials = finalMaterials;
            var sections = finalSections;

            // 5. Remap элементов ОБЕИХ сторон на финальные section tags и fingerprints.
            var sectionByTag = sections.ToDictionary(section => section.Tag);
            NormalizedShellElement[] remappedWallElements = wall.Model.Elements
                .Select(element =>
                {
                    if (!sectionTagMap.TryGetValue(("wall", element.SectionTag), out int finalSectionTag))
                    {
                        diagnostics.Add(new("floor_junction_tag_collision",
                            $"Wall-элемент {element.Tag} ссылается на неразрешённую секцию {element.SectionTag}."));
                        return element;
                    }
                    return element with
                    {
                        Tag = element.Tag + plateElementCount,
                        NodeTags = element.NodeTags.Select(tag => tag + wallNodeTagOffset).ToList(),
                        SectionTag = finalSectionTag,
                        SectionFingerprint = sectionByTag[finalSectionTag].Fingerprint
                    };
                })
                .ToArray();
            NormalizedShellElement[] remappedPlateElements = plate.Model.Elements
                .Select(element =>
                {
                    if (!sectionTagMap.TryGetValue(("plate", element.SectionTag), out int finalSectionTag))
                    {
                        diagnostics.Add(new("floor_junction_tag_collision",
                            $"Plate-элемент {element.Tag} ссылается на неразрешённую секцию {element.SectionTag}."));
                        return element;
                    }
                    return element with
                    {
                        SectionTag = finalSectionTag,
                        SectionFingerprint = sectionByTag[finalSectionTag].Fingerprint
                    };
                })
                .ToArray();
            var elements = remappedPlateElements.Concat(remappedWallElements).ToArray();

            // 6. Junction pairs + equalDOF (plate master -> wall slave).
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

            // 7. Проверка коллизий тегов в общем namespace.
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
            // Замыкание зависимостей материалов: каждый финальный DependsOnMaterialTag должен
            // существовать в финальном наборе material tags — иначе в модель попадает битая
            // ссылка, которую OpenSees не сможет собрать. Защитный слой поверх итеративного
            // merge (резолвер обычно гарантирует closure, но это не инвариант навсегда).
            foreach (var material in materials)
                if (material.Spec.DependsOnMaterialTag is int dependency && !materialTags.Contains(dependency))
                    diagnostics.Add(new("floor_junction_material_dependency_missing",
                        $"Материал {material.Tag} ({material.SourceId}) ссылается на не существующий " +
                        $"dependency tag {dependency}."));
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
