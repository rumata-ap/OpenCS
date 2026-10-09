using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Fem.Combinations;
using CScore.Fem.Loads;
using CScore.Planar;
using CSfea.CScoreBridge;
using CSfea.CScoreBridge.Structural;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Настройки сборки входа CSfea по схеме FEM: вид расчёта, источник армирования пластин, стадии.</summary>
public sealed record FemCsfeaSetup
{
    /// <summary>Вид расчёта для диаграмм материалов сечений.</summary>
    public CalcType Calc { get; init; } = CalcType.N;

    /// <summary>Источник армирования пластинчатых КЭ (<see cref="FemCheckRebarSource"/>), как в проверке по КЭ.</summary>
    public string PlateRebarSource { get; init; } = FemCheckRebarSource.Section;

    /// <summary>Учитываются ли сдвиговые деформации стержней по хомутам.</summary>
    public bool BeamShear { get; init; } = true;

    /// <summary>Стадии нагружения: выражение загружений, шаг и максимум коэффициента нагрузки.</summary>
    public IReadOnlyList<FemAnalysisStage> Stages { get; init; } = [];

    /// <summary>
    /// Настройки по постановке: вид расчёта, стадии (<see cref="FemAnalysisParams.ResolveStages"/>), источник армирования
    /// пластин и сдвиг стержней — из <see cref="FemAnalysisParams.Csfea"/> (нет — по умолчанию).
    /// </summary>
    public static FemCsfeaSetup FromAnalysis(FemAnalysis analysis)
    {
        var p = FemAnalysisParams.Parse(analysis.ParamsJson);
        var c = p.Csfea ?? new FemCsfeaParams();
        return new FemCsfeaSetup
        {
            Calc = p.CalcType ?? CalcType.N, Stages = p.ResolveStages(analysis),
            PlateRebarSource = c.PlateRebarSource, BeamShear = c.BeamShear,
        };
    }

    /// <summary>
    /// Параметры ядра по параметрам постановки; <paramref name="maxDegreeOfParallelism"/> — из общих настроек расчёта
    /// (−1 — по числу ядер), <paramref name="log"/> — журнал итераций.
    /// </summary>
    public static RcSecantOptions SecantOptions(FemCsfeaParams? p, int maxDegreeOfParallelism = -1, Action<string>? log = null)
    {
        p ??= new FemCsfeaParams();
        return new RcSecantOptions
        {
            TensionConcrete = p.TensionConcrete, Psi = p.Psi, PlateCrackRule = p.PlateCrackRule, BeamShear = p.BeamShear,
            PoissonUncracked = p.PoissonUncracked,
            Solver = new CSfea.Core.SecantPicardOptions
            {
                MaxIterations = p.MaxIterations, TolDisplacement = p.TolDisplacement, TolStiffness = p.TolStiffness,
                MaxBisections = p.MaxBisections, Omega0 = p.Omega0, Geometric = p.GeomNonlinear,
                MaxDegreeOfParallelism = maxDegreeOfParallelism, Log = log,
            },
        };
    }
}

/// <summary>
/// Загрузка схемы FEM из БД во вход адаптера CSfea (<see cref="FemRcModelInput"/>): сетка, конструктивный уровень, ГУ,
/// загружения и нагрузки, свойства КЭ (<see cref="FemSelfWeightSourceFactory"/>). Сечение пластинчатого КЭ — источник
/// армирования проверки по КЭ на шаблон: сечение пластины КонЭ → сечение группы с КЭ (первой по порядку) → нет (упругий
/// по жёсткости); материалы — диаграммы по <see cref="FemCsfeaSetup.Calc"/>. Стержни — сечение CScore КЭ или КонЭ,
/// подготовленное один раз на сечение. Всё читается и готовится здесь — вызывать в UI-потоке (соединение SQLite одно);
/// адаптер (<see cref="FemRcModelAdapter.Adapt"/>) и расчёт — в фоне.
/// </summary>
public static class FemCsfeaInputBuilder
{
    public static FemRcModelInput Build(DatabaseService db, int schemaId, FemCsfeaSetup setup)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(setup);
        var diag = new List<FemValidationDiagnostic>();
        var data = FemCheckSchemaData.Load(db, schemaId);
        foreach (var error in data.Errors) diag.Add(new("rebar_file", error, false));

        var groups = db.FemSchemas.FirstOrDefault(s => s.Id == schemaId)?.MemberGroups.ToList() ?? [];
        var loadCases = db.GetFemLoadCases(schemaId);
        var properties = FemSelfWeightSourceFactory.Create(db, schemaId, out var notes);
        foreach (var note in notes) diag.Add(new("properties_default", note, false));

        var materials = new Dictionary<int, Material>();
        foreach (var m in db.Materials) materials.TryAdd(m.Id, m);
        var plates = PlateSections(db, data, groups, materials, setup, diag);
        var beams = BeamSections(db, data, materials, diag);

        return new FemRcModelInput
        {
            MeshNodes = data.MeshNodes,
            MeshElements = data.Mesh,
            Nodes = db.GetFemNodes(schemaId),
            Members = data.Members,
            Groups = groups,
            Supports = db.GetFemMeshNodeSupports(schemaId),
            Springs = db.GetFemSprings(schemaId),
            RigidBodies = db.GetFemRigidBodies(schemaId),
            LoadCases = loadCases,
            NodeLoads = db.GetFemNodeLoads(schemaId),
            MemberLoads = db.GetFemMemberLoads(schemaId),
            ElementLoads = db.GetFemElementLoads(schemaId),
            MeshNodeLoads = db.GetFemMeshNodeLoads(schemaId),
            KinematicLoads = db.GetFemKinematicLoads(schemaId),
            Properties = properties,
            PlateSection = plates.Count == 0 ? null : e => plates.GetValueOrDefault(e.ElemTag),
            BeamSection = beams.Count == 0 ? null : e => beams.GetValueOrDefault(e.ElemTag),
            Calc = setup.Calc,
            RegionAxisX = RegionAxes(data),
            BeamShear = setup.BeamShear,
            Stages = Stages(setup.Stages, loadCases, diag),
            Diagnostics = diag,
        };
    }

    /// <summary>
    /// Стадии постановки → стадии адаптера: загружения выражения с коэффициентами, умноженными на максимум коэффициента
    /// нагрузки; шагов — round(максимум / шаг), не меньше одного. Нерешаемое выражение — ошибка.
    /// </summary>
    public static List<FemRcStage> Stages(IReadOnlyList<FemAnalysisStage> stages, IReadOnlyList<FemLoadCase> loadCases,
        List<FemValidationDiagnostic> diag)
    {
        var result = new List<FemRcStage>(stages.Count);
        foreach (var stage in stages)
        {
            double max = stage.MaxLoadFactor ?? 1.0, step = stage.LoadFactorStep ?? max;
            IReadOnlyList<(FemLoadCase LoadCase, double Factor)> terms;
            try { terms = FemLoadExpressionResolver.Terms(FemLoadExpression.Parse(stage.LoadExpressionJson), loadCases); }
            catch (Exception ex) when (ex is NotSupportedException or System.Text.Json.JsonException)
            {
                diag.Add(new("stage_expression", $"Стадия «{stage.Tag}»: {ex.Message}", true));
                terms = [];
            }
            int steps = step > 0 && max > 0 ? Math.Max(1, (int)Math.Round(max / step)) : 1;
            result.Add(new FemRcStage(stage.Tag, terms.Select(t => (t.LoadCase.Id, t.Factor * max)).ToList(), steps));
        }
        return result;
    }

    // ---------------------------------------------------------------- пластины

    /// <summary>
    /// Сечения пластинчатых КЭ по тегу; КЭ без шаблона в словарь не входят, без армирования источника — входят без
    /// материалов (упругие, причина — в отчёте адаптера), с ошибкой материалов шаблона — не входят (ошибка в отчёте).
    /// </summary>
    static Dictionary<string, FemRcPlateSection> PlateSections(DatabaseService db, FemCheckSchemaData data,
        IReadOnlyList<FemMemberGroup> groups, IReadOnlyDictionary<int, Material> materials, FemCsfeaSetup setup,
        List<FemValidationDiagnostic> diag)
    {
        var result = new Dictionary<string, FemRcPlateSection>(StringComparer.Ordinal);
        var shells = data.Mesh.Where(e => e.ElemType == "shell").ToList();
        if (shells.Count == 0) return result;

        var plateById = new Dictionary<int, PlateSection>();
        foreach (var p in db.PlateSections) plateById.TryAdd(p.Id, p);
        var memberByTag = new Dictionary<string, FemMember>(StringComparer.Ordinal);
        foreach (var m in data.Members) memberByTag.TryAdd(m.ElemTag, m);
        var withSection = groups.Where(g => g.PlateSectionId != null)
            .Select(g => (Group: g, Tags: FemCheckScope.GroupTags(g))).ToList();

        var sources = new Dictionary<int, IPlateElementSectionSource?>();
        var prepared = new Dictionary<PlateSection, PlateSectionMaterials?>(ReferenceEqualityComparer.Instance);
        var noTemplate = new List<string>();
        var conflicts = new List<string>();
        var badTemplates = new HashSet<int>();
        foreach (var e in shells)
        {
            var member = e.SourceMemberTag is { } tag ? memberByTag.GetValueOrDefault(tag) : null;
            int? templateId = member?.PlateSectionId;
            if (templateId == null)
            {
                var ids = withSection
                    .Where(x => x.Group.IsMeshGroup ? x.Tags.Contains(e.ElemTag) : e.SourceMemberTag != null && x.Tags.Contains(e.SourceMemberTag))
                    .Select(x => x.Group.PlateSectionId!.Value).Distinct().ToList();
                if (ids.Count > 1) conflicts.Add(e.ElemTag);
                templateId = ids.Count > 0 ? ids[0] : null;
            }
            if (templateId is not int id || !plateById.TryGetValue(id, out var template)) { noTemplate.Add(e.ElemTag); continue; }

            if (!sources.TryGetValue(id, out var source))
                sources[id] = source = FemCheckContext.PlateSource(setup.PlateRebarSource, template, data, db.PlateSections);
            if (source == null) continue;
            var section = source.Resolve(new FemCheckScopeElement(
                int.TryParse(e.ElemTag, NumberStyles.None, CultureInfo.InvariantCulture, out int num) ? num : null, e, member));
            if (section.Section is not { } ps)
            {
                // Без армирования — упругий КЭ; причина попадёт в отчёт адаптера.
                result[e.ElemTag] = new FemRcPlateSection(section, null, "");
                continue;
            }
            if (!prepared.TryGetValue(ps, out var mats))
            {
                try
                {
                    var (c, r, layers, concreteE_MPa) = PlateMaterialResolver.Resolve(ps, materials.Values, setup.Calc);
                    mats = new PlateSectionMaterials { ConcreteDiagram = c, RebarDiagram = r, LayerDiagrams = layers, ConcreteE_MPa = concreteE_MPa };
                }
                catch (InvalidOperationException ex)
                {
                    if (badTemplates.Add(id)) diag.Add(new("plate_materials", $"Сечение пластины «{template.Tag}»: {ex.Message}", true));
                    mats = null;
                }
                prepared[ps] = mats;
            }
            if (mats != null) result[e.ElemTag] = new FemRcPlateSection(section, mats, Key(template, setup.Calc));
        }
        if (noTemplate.Count > 0)
            diag.Add(new("shell_no_template", $"Пластин без сечения пластины (КонЭ и группы): {noTemplate.Count} — упругие по жёсткости.",
                false, noTemplate));
        if (conflicts.Count > 0)
            diag.Add(new("shell_template_conflict",
                $"Пластины в нескольких группах с разными сечениями — взято сечение первой группы: {FemLoadTargets.Sample(conflicts)}.",
                false, conflicts));
        return result;
    }

    /// <summary>Ключ материалов сечения: шаблон (бетон, арматура, модель) и вид расчёта.</summary>
    static string Key(PlateSection template, CalcType calc) =>
        string.Create(CultureInfo.InvariantCulture, $"{template.Id}|{template.ConcreteMaterialId}|{template.RebarMaterialId}|{template.ConcreteDiagramType}|{calc}");

    /// <summary>Ось x области (<see cref="Frame3D.LocalX"/>) по тегу планарного конструктивного элемента.</summary>
    static Dictionary<string, PlanarVector3>? RegionAxes(FemCheckSchemaData data)
    {
        if (data.Regions.Count == 0) return null;
        var regions = new Dictionary<int, PlanarRegion>();
        foreach (var r in data.Regions) regions.TryAdd(r.Id, r);
        var result = new Dictionary<string, PlanarVector3>(StringComparer.Ordinal);
        foreach (var m in data.Members)
            if (m.PlanarRegionId is int id && regions.TryGetValue(id, out var region))
                result.TryAdd(m.ElemTag, region.Frame.LocalX);
        return result;
    }

    // ---------------------------------------------------------------- стержни

    /// <summary>
    /// Сечения CScore стержневых КЭ по тегу: сечение КЭ, иначе КонЭ; клон с материалами и диаграммами — один на
    /// сечение. GJ — по <see cref="ProjectElementStiffnessSource"/> (ручное значение, иначе Ix + Iy).
    /// </summary>
    static Dictionary<string, FemRcBeamCross> BeamSections(DatabaseService db, FemCheckSchemaData data,
        IReadOnlyDictionary<int, Material> materials, List<FemValidationDiagnostic> diag)
    {
        var result = new Dictionary<string, FemRcBeamCross>(StringComparer.Ordinal);
        var memberByTag = new Dictionary<string, FemMember>(StringComparer.Ordinal);
        foreach (var m in data.Members) memberByTag.TryAdd(m.ElemTag, m);
        var sections = new Dictionary<int, CrossSection>();
        foreach (var s in db.CrossSections) sections.TryAdd(s.Id, s);
        var gj = new ProjectElementStiffnessSource(data.Members, db.CrossSections, [], materials.Values);

        var prepared = new Dictionary<int, CrossSection?>();
        var missing = new List<string>();
        var saintVenant = new List<string>();
        foreach (var e in data.Mesh.Where(e => e.ElemType == "beam"))
        {
            var member = e.SourceMemberTag is { } tag ? memberByTag.GetValueOrDefault(tag) : null;
            if ((e.CrossSectionId ?? member?.CrossSectionId) is not int id) continue;
            if (!prepared.TryGetValue(id, out var cs))
            {
                cs = null;
                if (sections.TryGetValue(id, out var source))
                    try
                    {
                        cs = source.CloneForCalc();
                        foreach (var area in cs.Areas)
                            if (area.Material == null && materials.TryGetValue(area.MaterialId, out var mat)) area.Material = mat;
                        cs.ResolveAndBuildDiagramms();
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                    {
                        diag.Add(new("beam_section_prepare", $"Сечение стержня «{source.Tag}»: {ex.Message}", true));
                        cs = null;
                    }
                prepared[id] = cs;
            }
            if (cs == null) { missing.Add(e.ElemTag); continue; }
            string strategy = e.CrossSectionId != null || member == null ? e.GjStrategy : member.GjStrategy;
            if (strategy == "saint_venant") saintVenant.Add(e.ElemTag);
            double torsion = gj.Bar(e) is { } b ? b.G * b.J : 0;
            result[e.ElemTag] = new FemRcBeamCross(cs, string.Create(CultureInfo.InvariantCulture, $"cs{id}|{torsion:R}"), torsion);
        }
        if (missing.Count > 0)
            diag.Add(new("beam_section_missing",
                $"Стержни с сечением, которое не найдено или не подготовлено, — по жёсткости: {FemLoadTargets.Sample(missing)}.", false, missing));
        if (saintVenant.Count > 0)
            diag.Add(new("beam_gj_saint_venant",
                $"GJ по задаче кручения пока не берётся — принят Ix + Iy сечения: {FemLoadTargets.Sample(saintVenant)}.", false, saintVenant));
        return result;
    }
}
