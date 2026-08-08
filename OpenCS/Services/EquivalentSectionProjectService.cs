using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.PlateStrip;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Собирает эквивалентное сечение из сохранённых исходных данных проекта: резолвит
/// PlanarRegion/фоновое PlateSection через DatabaseService и пространственное варьирование
/// армирования по ширине полосы через PlateRebarField/PlateRebarFieldResolver.</summary>
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

    /// <summary>Построить и сохранить эквивалентное сечение по региону-источнику.</summary>
    public EquivalentSectionBuildResult BuildAndSave(
        PlateStripBeamAnalogy analogy,
        int sourceSchemaId,
        int sourceRegionId,
        CalcType calc,
        ReductionPolicy policy,
        int widthIntegrationPoints = 2,
        double spanStationFraction = 0.5,
        EquivalentSection? existing = null)
    {
        ArgumentNullException.ThrowIfNull(analogy);

        var resolved = ResolveAndBuild(
            analogy, sourceSchemaId, sourceRegionId, calc, policy,
            widthIntegrationPoints, spanStationFraction);
        if (!resolved.IsCalculable || resolved.Section == null)
            return resolved;

        var equivalent = resolved.Section;
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

    /// <summary>Пересчитать провенанс/жёсткость и обновить IsStale/IsCalculable/Diagnostics.
    /// Возвращает true, если IsStale, IsCalculable или состав Diagnostics изменились.</summary>
    public bool RefreshStale(EquivalentSection equivalent, CalcType calc)
    {
        ArgumentNullException.ThrowIfNull(equivalent);

        bool wasStale = equivalent.IsStale;
        bool wasCalculable = equivalent.IsCalculable;
        var oldDiagnostics = equivalent.Diagnostics;

        var resolved = ResolveAndBuild(
            equivalent.Strip, equivalent.SourceSchemaId, equivalent.SourceRegionId, calc,
            equivalent.ReductionPolicy, equivalent.WidthIntegrationPoints, equivalent.SpanStationFraction);

        bool calculable = resolved.IsCalculable && resolved.Section != null;
        bool stale = !calculable || !string.Equals(
            resolved.Section?.InputFingerprint, equivalent.InputFingerprint, StringComparison.Ordinal);

        var diagnostics = resolved.Diagnostics.ToList();
        if (stale && calculable)
            diagnostics.Add(new(
                "equivalent_section_stale",
                "Входные данные эквивалентного сечения изменились; требуется пересчёт.", false));

        equivalent.IsStale = stale;
        equivalent.IsCalculable = calculable;
        equivalent.Diagnostics = diagnostics;

        return wasStale != stale || wasCalculable != calculable || !DiagnosticsEqual(oldDiagnostics, diagnostics);
    }

    /// <summary>Единый внутренний путь резолва+сборки — общий для BuildAndSave и RefreshStale.
    /// Не сохраняет в БД и не трогает Id/Num/Tag/Description.</summary>
    EquivalentSectionBuildResult ResolveAndBuild(
        PlateStripBeamAnalogy analogy, int sourceSchemaId, int sourceRegionId, CalcType calc,
        ReductionPolicy policy, int widthIntegrationPoints, double spanStationFraction)
    {
        if (!double.IsFinite(spanStationFraction) || spanStationFraction < 0.0 || spanStationFraction > 1.0)
            return Fail("equivalent_section_invalid_station_fraction",
                "Станция вдоль пролёта должна быть конечным числом в диапазоне [0,1].");

        var region = _database.GetPlanarRegions(sourceSchemaId).FirstOrDefault(r => r.Id == sourceRegionId);
        if (region == null)
            return Fail("equivalent_section_region_not_found",
                $"Регион Id={sourceRegionId} не найден в схеме Id={sourceSchemaId}.");

        var members = _database.GetFemMembers(sourceSchemaId)
            .Where(m => m.PlanarRegionId == sourceRegionId && m.PlateSectionId.HasValue)
            .OrderBy(m => m.Id)
            .ToList();
        if (members.Count == 0)
            return Fail("equivalent_section_background_section_not_found",
                $"У региона Id={sourceRegionId} нет FemMember с фоновым PlateSectionId.");

        var diagnostics = new List<FemValidationDiagnostic>();
        if (members.Count > 1)
            diagnostics.Add(new(
                "equivalent_section_duplicate_background_member",
                $"У региона Id={sourceRegionId} несколько FemMember с этим PlanarRegionId; выбран Id={members[0].Id}.",
                false));

        var background = _database.PlateSections.FirstOrDefault(s => s.Id == members[0].PlateSectionId!.Value);
        if (background == null)
            return Fail("equivalent_section_background_section_not_found",
                $"Фоновое PlateSection Id={members[0].PlateSectionId} не найдено.");

        var field = PlateRebarField.From(background, region);

        var (gaussV, _) = EquivalentSectionCalculator.WidthGaussPoints(analogy.ExplicitWidthM, widthIntegrationPoints);
        const int CenterlineElementId = -1;
        var centroids = new List<(int ElementId, double U, double V)>();
        void AddCentroid(int elementId, double v)
        {
            var uv = PlateStripWidthSampler.Point(analogy, spanStationFraction, v);
            if (!IsInsideMaterial(region, uv))
                diagnostics.Add(new(
                    "equivalent_section_width_sample_outside_material",
                    $"Точка резолва (станция={spanStationFraction:G4}, v={v:G4}) вне Hull/внутри отверстия региона.",
                    false));
            centroids.Add((elementId, uv.U, uv.V));
        }
        // PlateStripWidthSampler.Point — чистый API, бросающий ArgumentException/
        // ArgumentOutOfRangeException на структурно повреждённой геометрии (например,
        // LeftBoundary/RightBoundary < 2 точек после десериализации повреждённого strip_json).
        // ResolveAndBuild вызывается из RefreshStale на старте приложения — исключение здесь
        // не должно долетать до вызывающей стороны: превращаем в блокирующую диагностику.
        try
        {
            AddCentroid(CenterlineElementId, 0.0);
            for (int i = 0; i < gaussV.Length; i++)
                AddCentroid(i, gaussV[i]);
        }
        catch (ArgumentException ex)
        {
            return Fail("equivalent_section_invalid_strip",
                $"Геометрия сохранённой полосы повреждена или несовместима с шириной интегрирования: {ex.Message}");
        }

        var resolvedLayouts = PlateRebarFieldResolver.ResolveMesh(field, centroids);
        foreach (var resolved in resolvedLayouts)
        foreach (var d in resolved.Layout.Diagnostics)
            diagnostics.Add(d.Code == "plate_rebar_zone_priority_conflict" ? new(d.Code, d.Message, false) : d);

        var snapshotByFingerprint = new Dictionary<string, IPlateSectionResponse>();
        foreach (var resolved in resolvedLayouts)
        {
            if (snapshotByFingerprint.ContainsKey(resolved.LayoutFingerprint)) continue;
            var variant = background.CloneForCalc();
            variant.RebarLayers = resolved.Layout.Layers.Select(l => l.Clone()).ToList();
            var sourceResult = BuildSource(variant, calc);
            diagnostics.AddRange(sourceResult.Diagnostics);
            if (!sourceResult.IsCalculable || sourceResult.Source == null)
                return new(false, null, diagnostics);
            snapshotByFingerprint[resolved.LayoutFingerprint] = sourceResult.Source;
        }

        var byId = resolvedLayouts.ToDictionary(r => r.ElementId);
        var centerlineSource = snapshotByFingerprint[byId[CenterlineElementId].LayoutFingerprint];
        var widthSources = new IPlateSectionResponse[gaussV.Length];
        for (int i = 0; i < gaussV.Length; i++)
            widthSources[i] = snapshotByFingerprint[byId[i].LayoutFingerprint];

        var build = EquivalentSectionCalculator.Build(
            analogy, centerlineSource, widthSources, policy, widthIntegrationPoints);
        diagnostics.AddRange(build.Diagnostics);
        if (!build.IsCalculable || build.Section == null)
            return new(false, null, diagnostics);

        var equivalent = build.Section;
        equivalent.SourceSchemaId = sourceSchemaId;
        equivalent.SourceRegionId = sourceRegionId;
        equivalent.SourcePlateSectionId = background.Id;
        equivalent.SourceRegionFingerprint = region.GeometryFingerprint;
        equivalent.SpanStationFraction = spanStationFraction;
        equivalent.Diagnostics = diagnostics;
        equivalent.IsCalculable = !diagnostics.Any(d => d.IsError);
        equivalent.InputFingerprint = EquivalentSectionFingerprint.Compute(
            analogy, sourceSchemaId, region.GeometryFingerprint, spanStationFraction,
            centerlineSource, widthSources, policy, widthIntegrationPoints);

        return new(equivalent.IsCalculable, equivalent, diagnostics);
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

    static bool IsInsideMaterial(PlanarRegion region, PlanarPoint2D point)
    {
        if (region.Hull == null) return true;
        if (!PointInPolygon(region.Hull, point)) return false;
        foreach (var hole in region.Holes)
            if (PointInPolygon(hole, point)) return false;
        return true;
    }

    static bool PointInPolygon(Contour contour, PlanarPoint2D point)
    {
        var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
        var poly = new double[x.Length][];
        for (int i = 0; i < x.Length; i++) poly[i] = [x[i], y[i]];
        return CSTriangulation.GeometryUtils.PointInPolygon(point.U, point.V, poly);
    }

    static bool DiagnosticsEqual(IReadOnlyList<FemValidationDiagnostic> a, IReadOnlyList<FemValidationDiagnostic> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i].Code != b[i].Code || a[i].Message != b[i].Message || a[i].IsError != b[i].IsError)
                return false;
        return true;
    }

    static PlateSectionTangentSnapshotBuildResult MissingSource(string message) =>
        new(false, null, [new("equivalent_section_missing_source", message)]);

    static EquivalentSectionBuildResult Fail(string code, string message) =>
        new(false, null, [new FemValidationDiagnostic(code, message)]);
}
