using CScore;
using CScore.Fem;
using CScore.PlateStrip;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Собирает эквивалентное сечение из сохранённых исходных данных проекта.</summary>
public sealed class EquivalentSectionProjectService
{
    readonly DatabaseService _database;
    readonly IReadOnlyDictionary<int, Material> _materials;

    public EquivalentSectionProjectService(
        DatabaseService database,
        IReadOnlyDictionary<int, Material> materials)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
    }

    /// <summary>Построить и сохранить эквивалентное сечение по плитному источнику.</summary>
    public EquivalentSectionBuildResult BuildAndSave(
        PlateStripBeamAnalogy analogy,
        int sourceSchemaId,
        PlateSection section,
        CalcType calc,
        ReductionPolicy policy,
        int widthIntegrationPoints = 2,
        EquivalentSection? existing = null)
    {
        ArgumentNullException.ThrowIfNull(analogy);
        ArgumentNullException.ThrowIfNull(section);

        var sourceResult = BuildSource(section, calc);
        if (!sourceResult.IsCalculable || sourceResult.Source == null)
            return new(false, null, sourceResult.Diagnostics);

        var build = EquivalentSectionCalculator.Build(
            analogy, sourceResult.Source, policy, widthIntegrationPoints);
        if (!build.IsCalculable || build.Section == null)
            return build;

        var equivalent = build.Section;
        equivalent.SourceSchemaId = sourceSchemaId;
        equivalent.SourceRegionId = analogy.SourceRegionId;
        equivalent.SourcePlateSectionId = section.Id;
        equivalent.Diagnostics = sourceResult.Diagnostics.Concat(build.Diagnostics).ToList();
        equivalent.IsCalculable = !equivalent.Diagnostics.Any(d => d.IsError);

        if (existing != null)
        {
            equivalent.Id = existing.Id;
            equivalent.Num = existing.Num;
            equivalent.Tag = existing.Tag;
            equivalent.Description = existing.Description;
        }

        _database.SaveEquivalentSection(equivalent);
        if (existing != null)
        {
            int index = _database.EquivalentSections.IndexOf(existing);
            if (index >= 0)
                _database.EquivalentSections[index] = equivalent;
            else if (!_database.EquivalentSections.Contains(equivalent))
                _database.EquivalentSections.Add(equivalent);
        }
        return new(equivalent.IsCalculable, equivalent, equivalent.Diagnostics);
    }

    /// <summary>Проверить, не изменились ли входы сохранённого результата.</summary>
    public bool RefreshStale(EquivalentSection equivalent, PlateSection? section, CalcType calc)
    {
        ArgumentNullException.ThrowIfNull(equivalent);

        bool wasStale = equivalent.IsStale;
        bool wasMarked = equivalent.Diagnostics.Any(d => d.Code == "equivalent_section_stale");
        var source = section == null
            ? MissingSource("Исходное плитное сечение не найдено.")
            : BuildSource(section, calc);
        bool stale = !source.IsCalculable || source.Source == null;
        if (!stale)
        {
            var build = EquivalentSectionCalculator.Build(
                equivalent.Strip, source.Source, equivalent.ReductionPolicy,
                equivalent.WidthIntegrationPoints);
            stale = !build.IsCalculable || build.Section == null ||
                    !string.Equals(build.Section.InputFingerprint, equivalent.InputFingerprint,
                        StringComparison.Ordinal);
        }

        equivalent.IsStale = stale;
        var diagnostics = equivalent.Diagnostics
            .Where(d => d.Code != "equivalent_section_stale")
            .ToList();
        if (stale)
            diagnostics.Add(new(
                "equivalent_section_stale",
                "Входные данные эквивалентного сечения изменились; требуется пересчёт.", false));
        equivalent.Diagnostics = diagnostics;
        return wasStale != stale || wasMarked != stale;
    }

    PlateSectionTangentSnapshotBuildResult BuildSource(PlateSection section, CalcType calc)
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        if (!_materials.TryGetValue(section.ConcreteMaterialId, out var concrete))
            return MissingSource($"Материал бетона Id={section.ConcreteMaterialId} не найден.");
        if (!_materials.TryGetValue(section.RebarMaterialId, out var rebar))
            return MissingSource($"Материал арматуры Id={section.RebarMaterialId} не найден.");

        var concreteDiagram = concrete.GetDiagramms(section.ConcreteDiagramType)?[calc]
            ?? concrete.GetDiagramms(DiagrammType.L3)?[calc];
        if (concreteDiagram == null)
            return MissingSource($"Диаграмма бетона для CalcType={calc} не построена.");

        var rebarDiagram = rebar.GetDiagramms(DiagrammType.L2)?[calc];
        if (rebarDiagram == null)
            return MissingSource($"Диаграмма арматуры для CalcType={calc} не построена.");

        Diagramm?[]? layerDiagrams = null;
        if (section.RebarLayers.Count > 0)
        {
            layerDiagrams = new Diagramm?[section.RebarLayers.Count];
            for (int i = 0; i < section.RebarLayers.Count; i++)
            {
                var layer = section.RebarLayers[i];
                if (layer.MaterialId <= 0) continue;
                if (!_materials.TryGetValue(layer.MaterialId, out var layerMaterial))
                    diagnostics.Add(new(
                        "equivalent_section_missing_layer_material",
                        $"Материал арматурного слоя Id={layer.MaterialId} не найден."));
                else
                    layerDiagrams[i] = layerMaterial.GetDiagramms(DiagrammType.L2)?[calc];
            }
        }

        if (diagnostics.Any(d => d.IsError))
            return new(false, null, diagnostics);

        var source = PlateSectionTangentSnapshot.Create(
            section, concreteDiagram, rebarDiagram, layerDiagrams);
        return diagnostics.Count == 0
            ? source
            : new(source.IsCalculable, source.Source,
                  diagnostics.Concat(source.Diagnostics).ToList());
    }

    static PlateSectionTangentSnapshotBuildResult MissingSource(string message) =>
        new(false, null, [new("equivalent_section_missing_source", message)]);
}
