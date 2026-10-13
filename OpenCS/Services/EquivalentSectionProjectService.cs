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
        EquivalentSection? existing = null,
        StripBeamFormulation formulation = StripBeamFormulation.EulerBernoulli,
        IStripRebarSource? rebarSource = null,
        string rebarSourceKey = "")
    {
        ArgumentNullException.ThrowIfNull(analogy);

        var resolved = ResolveAndBuild(
            analogy, sourceSchemaId, sourceRegionId, calc, policy,
            widthIntegrationPoints, spanStationFraction, formulation, rebarSource, rebarSourceKey);
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
    public bool RefreshStale(EquivalentSection equivalent, CalcType calc, IStripRebarSource? rebarSource = null)
    {
        ArgumentNullException.ThrowIfNull(equivalent);

        bool wasStale = equivalent.IsStale;
        bool wasCalculable = equivalent.IsCalculable;
        var oldDiagnostics = equivalent.Diagnostics;

        var resolved = ResolveAndBuild(
            equivalent.Strip, equivalent.SourceSchemaId, equivalent.SourceRegionId, calc,
            equivalent.ReductionPolicy, equivalent.WidthIntegrationPoints, equivalent.SpanStationFraction,
            equivalent.BeamFormulation, rebarSource, equivalent.RebarSourceKey);

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
        ReductionPolicy policy, int widthIntegrationPoints, double spanStationFraction,
        StripBeamFormulation formulation = StripBeamFormulation.EulerBernoulli,
        IStripRebarSource? rebarSource = null,
        string rebarSourceKey = "")
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
        StripMaterialProfile.EnsureFromRegion(analogy.Geometry, region);

        var (gaussV, _) = EquivalentSectionCalculator.WidthGaussPoints(analogy.ExplicitWidthM, widthIntegrationPoints);
        const int CenterlineElementId = -1;
        var centroids = new List<(int ElementId, double U, double V)>();
        void AddCentroid(int elementId, double v)
        {
            // Точка в отверстии: армирование берётся у ближайшей кромки материала.
            var uv = PlateStripWidthSampler.Point(
                analogy, spanStationFraction, StripMaterialProfile.SnapToMaterial(analogy, spanStationFraction, v));
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

        // Источник армирования: заданный вызывающей стороной, по ключу (КЭ импортированной схемы) либо раскладка региона.
        if (rebarSource == null && StripRebarSourceFactory.IsMeshKey(rebarSourceKey))
        {
            var made = StripRebarSourceFactory.Create(
                _database, _materials, sourceSchemaId, region, rebarSourceKey, background);
            diagnostics.AddRange(made.Diagnostics);
            if (made.Diagnostics.Any(d => d.IsError)) return new(false, null, diagnostics);
            rebarSource = made.Source;
        }
        rebarSource ??= new FieldStripRebarSource(field);
        var resolvedLayouts = new List<(int ElementId, IReadOnlyList<PlateRebarLayer> Layers, string Fingerprint)>();
        var guardSeen = new HashSet<string>();
        foreach (var (elementId, u, v) in centroids)
        {
            var sample = rebarSource.Resolve(u, v);
            diagnostics.AddRange(sample.Diagnostics);
            foreach (var d in StripRebarAxisGuard.Check(analogy, sample))
                if (guardSeen.Add(d.Code + d.Message)) diagnostics.Add(d);
            resolvedLayouts.Add((elementId, sample.Layers, PlateRebarLayoutFingerprint.Compute(sample.Layers)));
        }
        if (diagnostics.Any(d => d.IsError && d.Code.StartsWith("plate_strip_", StringComparison.Ordinal)))
            return new(false, null, diagnostics);

        var snapshotByFingerprint = new Dictionary<string, IPlateSectionResponse>();
        foreach (var resolved in resolvedLayouts)
        {
            if (snapshotByFingerprint.ContainsKey(resolved.Fingerprint)) continue;
            var variant = background.CloneForCalc();
            variant.RebarLayers = resolved.Layers.Select(l => l.Clone()).ToList();
            var sourceResult = BuildSource(variant, calc);
            diagnostics.AddRange(sourceResult.Diagnostics);
            if (!sourceResult.IsCalculable || sourceResult.Source == null)
                return new(false, null, diagnostics);
            snapshotByFingerprint[resolved.Fingerprint] = sourceResult.Source;
        }

        var byId = resolvedLayouts.ToDictionary(r => r.ElementId);
        var centerlineSource = snapshotByFingerprint[byId[CenterlineElementId].Fingerprint];
        var widthSources = new IPlateSectionResponse[gaussV.Length];
        for (int i = 0; i < gaussV.Length; i++)
            widthSources[i] = snapshotByFingerprint[byId[i].Fingerprint];

        // Отверстия региона: доля материала в ячейках ширины на станции сечения; прямая редукция — ширина материала.
        var cellFractions = StripMaterialProfile.CellFractions(analogy, spanStationFraction, widthIntegrationPoints);
        for (int i = 0; i < widthSources.Length; i++)
            widthSources[i] = ScaledPlateSectionResponse.Apply(widthSources[i], cellFractions[i]);
        double materialWidth = StripMaterialProfile.MaterialWidth(analogy, spanStationFraction);

        var build = EquivalentSectionCalculator.Build(
            analogy, centerlineSource, widthSources, policy, widthIntegrationPoints, formulation, materialWidth);
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
            centerlineSource, widthSources, policy, widthIntegrationPoints, formulation, rebarSourceKey);
        equivalent.RebarSourceKey = rebarSourceKey ?? "";

        return new(equivalent.IsCalculable, equivalent, diagnostics);
    }

    /// <summary>Диаграммы плитного сечения для расчёта: бетон, арматура и диаграммы слоёв с собственным
    /// материалом. Единственный источник правды для замороженного и «живого» источников.</summary>
    sealed record SectionDiagrams(
        Diagramm Concrete, Diagramm Rebar, Diagramm?[]? Layers, List<FemValidationDiagnostic> Diagnostics);

    SectionDiagrams? ResolveDiagrams(PlateSection section, CalcType calc, out string? failure)
    {
        failure = null;
        var diagnostics = new List<FemValidationDiagnostic>();
        if (!_materials.TryGetValue(section.ConcreteMaterialId, out var concrete))
        {
            failure = $"Материал бетона Id={section.ConcreteMaterialId} не найден.";
            return null;
        }
        if (!_materials.TryGetValue(section.RebarMaterialId, out var rebar))
        {
            failure = $"Материал арматуры Id={section.RebarMaterialId} не найден.";
            return null;
        }

        var concreteDiagram = concrete.GetDiagramms(section.ConcreteDiagramType)?[calc]
            ?? concrete.GetDiagramms(DiagrammType.L3)?[calc];
        if (concreteDiagram == null)
        {
            failure = $"Диаграмма бетона для CalcType={calc} не построена.";
            return null;
        }

        var rebarDiagram = rebar.GetDiagramms(
            DiagrammCompatibility.Coerce(rebar.Type, DiagrammType.L2))?[calc];
        if (rebarDiagram == null)
        {
            failure = $"Диаграмма арматуры для CalcType={calc} не построена.";
            return null;
        }

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
                    layerDiagrams[i] = layerMaterial.GetDiagramms(
                        DiagrammCompatibility.Coerce(layerMaterial.Type, DiagrammType.L2))?[calc];
            }
        }

        return new(concreteDiagram, rebarDiagram, layerDiagrams, diagnostics);
    }

    PlateSectionTangentSnapshotBuildResult BuildSource(PlateSection section, CalcType calc)
    {
        var diagrams = ResolveDiagrams(section, calc, out var failure);
        if (diagrams == null)
            return MissingSource(failure!);
        var diagnostics = diagrams.Diagnostics;
        if (diagnostics.Any(d => d.IsError))
            return new(false, null, diagnostics);

        var source = PlateSectionTangentSnapshot.Create(
            section, diagrams.Concrete, diagrams.Rebar, diagrams.Layers);
        return diagnostics.Count == 0
            ? source
            : new(source.IsCalculable, source.Source,
                  diagnostics.Concat(source.Diagnostics).ToList());
    }

    /// <summary>Результат построения «живых» источников полосы для нелинейного прогона.</summary>
    public sealed record StripLiveSources(
        bool IsCalculable,
        PlateSection? Background,
        PlateRebarField? Field,
        StripSectionSourceGrid? Grid,
        IReadOnlyList<FemValidationDiagnostic> Diagnostics)
    {
        /// <summary>Источник армирования вдоль полосы, по которому построена сетка источников.</summary>
        public IStripRebarSource? RebarSource { get; init; }
    }

    /// <summary>
    /// Срез 8d: «живые» источники <see cref="PlateSectionLiveResponse"/> по элементам полосы и точкам
    /// квадратуры по ширине. Армирование разрешается из <see cref="PlateRebarField"/> в серединах
    /// элементов (тот же резолвер, что у фибровой сверки), источники с одинаковой раскладкой общие.
    /// </summary>
    public StripLiveSources ResolveLiveSources(
        EquivalentSection equivalent, CalcType calc, IReadOnlyList<double> stationFractions,
        IStripRebarSource? rebarSource = null)
    {
        ArgumentNullException.ThrowIfNull(equivalent);
        ArgumentNullException.ThrowIfNull(stationFractions);

        StripLiveSources Failure(string code, string message) =>
            new(false, null, null, null, [new FemValidationDiagnostic(code, message)]);

        var region = _database.GetPlanarRegions(equivalent.SourceSchemaId)
            .FirstOrDefault(r => r.Id == equivalent.SourceRegionId);
        if (region == null)
            return Failure("strip_run_region_not_found",
                $"Регион Id={equivalent.SourceRegionId} не найден в схеме Id={equivalent.SourceSchemaId}.");

        var member = _database.GetFemMembers(equivalent.SourceSchemaId)
            .Where(m => m.PlanarRegionId == equivalent.SourceRegionId && m.PlateSectionId.HasValue)
            .OrderBy(m => m.Id)
            .FirstOrDefault();
        if (member == null)
            return Failure("strip_run_background_section_not_found",
                $"У региона Id={equivalent.SourceRegionId} нет FemMember с фоновым PlateSectionId.");

        var background = _database.PlateSections.FirstOrDefault(s => s.Id == member.PlateSectionId!.Value);
        if (background == null)
            return Failure("strip_run_background_section_not_found",
                $"Фоновое PlateSection Id={member.PlateSectionId} не найдено.");

        var field = PlateRebarField.From(background, region);
        StripMaterialProfile.EnsureFromRegion(equivalent.Strip.Geometry, region);
        var diagnostics = new List<FemValidationDiagnostic>();
        if (rebarSource == null && StripRebarSourceFactory.IsMeshKey(equivalent.RebarSourceKey))
        {
            var made = StripRebarSourceFactory.Create(
                _database, _materials, equivalent.SourceSchemaId, region, equivalent.RebarSourceKey, background);
            diagnostics.AddRange(made.Diagnostics);
            if (made.Diagnostics.Any(d => d.IsError)) return new(false, background, field, null, diagnostics);
            rebarSource = made.Source;
        }
        rebarSource ??= new FieldStripRebarSource(field);
        var layouts = PlateStripFiberLayouts.Resolve(
            rebarSource, equivalent.Strip, stationFractions, equivalent.WidthIntegrationPoints);
        diagnostics.AddRange(layouts.Diagnostics);
        if (!layouts.IsCalculable)
            return new(false, background, field, null, diagnostics) { RebarSource = rebarSource };

        var byFingerprint = new Dictionary<string, IPlateSectionResponse>();
        var rows = new List<IReadOnlyList<IPlateSectionResponse>>(layouts.PerElement.Count);
        for (int e = 0; e < layouts.PerElement.Count; e++)
        {
            var element = layouts.PerElement[e];
            var row = new List<IPlateSectionResponse>(element.Count);
            // Отверстия региона: доля материала в ячейках ширины, усреднённая по длине элемента.
            var fractions = StripMaterialProfile.ElementFractions(
                equivalent.Strip, stationFractions[e], stationFractions[e + 1], element.Count);
            int point = 0;
            foreach (var layers in element)
            {
                string key = PlateRebarLayoutFingerprint.Compute(layers);
                if (!byFingerprint.TryGetValue(key, out var source))
                {
                    var variant = background.CloneForCalc();
                    variant.RebarLayers = layers.Select(l => l.Clone()).ToList();
                    var diagrams = ResolveDiagrams(variant, calc, out var failure);
                    if (diagrams == null)
                        return new(false, background, field, null,
                            [.. diagnostics, new FemValidationDiagnostic("strip_run_diagram_missing", failure!)])
                            { RebarSource = rebarSource };
                    diagnostics.AddRange(diagrams.Diagnostics);
                    if (diagrams.Diagnostics.Any(d => d.IsError))
                        return new(false, background, field, null, diagnostics) { RebarSource = rebarSource };
                    source = new PlateSectionLiveResponse(
                        variant, diagrams.Concrete, diagrams.Rebar, diagrams.Layers);
                    byFingerprint[key] = source;
                }
                row.Add(ScaledPlateSectionResponse.Apply(source, fractions[point++]));
            }
            rows.Add(row);
        }

        return new(true, background, field, new StripSectionSourceGrid(rows), diagnostics)
        {
            RebarSource = rebarSource
        };
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
