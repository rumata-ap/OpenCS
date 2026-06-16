using CScore;

namespace CSfea.CScoreBridge;

/// <summary>Фабрика адаптеров CScore → CSfea.</summary>
public static class SectionBridgeFactory
{
    /// <summary>
    /// Балка: сечение уже подготовлено (<see cref="CrossSection.ResolveAndBuildDiagramms"/>).
    /// </summary>
    public static CrossSectionBeamResponse BeamFromPrepared(
        CrossSection section, CalcType calc, double gjLinear = 0.0,
        bool ten = true, bool ca = true)
        => new(section, calc, gjLinear, ten, ca);

    /// <summary>
    /// Балка: привязать материалы по Id, построить диаграммы, вернуть адаптер.
    /// </summary>
    public static CrossSectionBeamResponse BeamFromDatabase(
        CrossSection section, IReadOnlyDictionary<int, Material> materials,
        CalcType calc, double gjLinear = 0.0, bool ten = true, bool ca = true)
    {
        AttachMaterials(section, materials);
        section.ResolveAndBuildDiagramms();
        return BeamFromPrepared(section, calc, gjLinear, ten, ca);
    }

    /// <summary>Плита: диаграммы уже переданы в <paramref name="materials"/>.</summary>
    public static PlateSectionShellResponse ShellFromPrepared(
        PlateSection section, PlateSectionMaterials materials)
        => new(section, materials);

    /// <summary>
    /// Плита: резолв материалов бетона/арматуры по Id из словаря.
    /// </summary>
    public static PlateSectionShellResponse ShellFromDatabase(
        PlateSection section, IReadOnlyDictionary<int, Material> materials,
        CalcType calc, DiagrammType concreteDiagramType = DiagrammType.SP63,
        DiagrammType rebarDiagramType = DiagrammType.L2)
    {
        if (!materials.TryGetValue(section.ConcreteMaterialId, out var concrete))
            throw new InvalidOperationException($"Материал бетона Id={section.ConcreteMaterialId} не найден.");
        if (!materials.TryGetValue(section.RebarMaterialId, out var rebar))
            throw new InvalidOperationException($"Материал арматуры Id={section.RebarMaterialId} не найден.");

        var cDiag = concrete.GetDiagramms(concreteDiagramType)?[calc]
                    ?? throw new InvalidOperationException("Диаграмма бетона не построена.");
        var rDiag = rebar.GetDiagramms(rebarDiagramType)?[calc]
                    ?? throw new InvalidOperationException("Диаграмма арматуры не построена.");

        Diagramm?[]? layerDiags = null;
        if (section.RebarLayers.Count > 0)
        {
            layerDiags = new Diagramm?[section.RebarLayers.Count];
            for (int i = 0; i < section.RebarLayers.Count; i++)
            {
                var layer = section.RebarLayers[i];
                if (layer.MaterialId > 0 && materials.TryGetValue(layer.MaterialId, out var lm))
                    layerDiags[i] = lm.GetDiagramms(rebarDiagramType)?[calc];
            }
        }

        var mats = new PlateSectionMaterials
        {
            ConcreteDiagram = cDiag,
            RebarDiagram = rDiag,
            LayerDiagrams = layerDiags,
            ConcreteE_MPa = concrete.E,
        };
        return ShellFromPrepared(section, mats);
    }

    /// <summary>Оценка GJ для линейного кручения: G [МПа]·J [м⁴] → Н·м².</summary>
    public static double EstimateGJ(CrossSection section, double shearG_MPa)
    {
        var gp = new GeoProps(section);
        double j = gp.Ix + gp.Iy;
        return shearG_MPa * 1e6 * j;
    }

    private static void AttachMaterials(CrossSection section, IReadOnlyDictionary<int, Material> materials)
    {
        foreach (var area in section.Areas)
        {
            if (area.Material == null && materials.TryGetValue(area.MaterialId, out var m))
                area.Material = m;
        }
    }
}
