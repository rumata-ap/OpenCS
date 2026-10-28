using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Fem.Combinations;
using CScore.Fem.Fragment.Masks;
using CScore.Fem.Loads;
using CScore.Planar;
using CScore.Submodel;
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
    /// Упругая жёсткость стержней с сечениями проекта — бетон брутто, без арматуры (линейный расчёт и секущий без
    /// физической нелинейности); в секущем расчёте с сечениями CScore упругая жёсткость таких стержней не используется.
    /// </summary>
    public bool GrossBars { get; init; }

    /// <summary>
    /// Понижающие коэффициенты жёсткости импорта из ЛИРЫ к упругой жёсткости бетона — там же, где бетон брутто
    /// (<see cref="GrossBars"/>: линейный расчёт и секущий без физической нелинейности).
    /// </summary>
    public bool ReducedStiffness { get; init; }

    /// <summary>
    /// Настройки по постановке: вид расчёта, стадии (<see cref="FemAnalysisParams.ResolveStages"/>), источник армирования
    /// пластин и сдвиг стержней — из <see cref="FemAnalysisParams.Csfea"/> (нет — по умолчанию). У пакета с
    /// <paramref name="loadCases"/> — и стадии длительных частей (<see cref="FemCsfeaBatchLongTerm.Stages"/>).
    /// </summary>
    public static FemCsfeaSetup FromAnalysis(FemAnalysis analysis, IReadOnlyList<FemLoadCase>? loadCases = null)
    {
        var p = FemAnalysisParams.Parse(analysis.ParamsJson);
        var c = p.Csfea ?? new FemCsfeaParams();
        bool elastic = analysis.Kind == FemCsfeaRunner.LinearAnalysisKind || !c.PhysicalNonlinearity;
        var stages = p.Batch is { } batch
            ? loadCases != null && batch.LongTermPairs.Count > 0 ? FemCsfeaBatchLongTerm.Stages(batch, loadCases) : batch.ToStages()
            : p.ResolveStages(analysis);
        return new FemCsfeaSetup
        {
            Calc = p.CalcType ?? CalcType.N, Stages = stages,
            PlateRebarSource = c.PlateRebarSource, BeamShear = c.BeamShear,
            GrossBars = elastic, ReducedStiffness = elastic && c.ImportedStiffnessFactors,
        };
    }

    /// <summary>
    /// Параметры ядра по параметрам постановки; <paramref name="maxDegreeOfParallelism"/> — из общих настроек расчёта
    /// (−1 — по числу ядер), <paramref name="log"/> — журнал итераций.
    /// </summary>
    public static RcSecantOptions SecantOptions(FemCsfeaParams? p, int maxDegreeOfParallelism = -1, Action<string>? log = null,
        IReadOnlyDictionary<int, double>? shellForceAngles = null,
        Action<CSfea.Core.SecantStepResult, Func<RcSecantStepFields>>? onStep = null)
    {
        p ??= new FemCsfeaParams();
        return new RcSecantOptions
        {
            TensionConcrete = p.TensionConcrete, Psi = p.Psi, PlateCrackRule = p.PlateCrackRule, BeamShear = p.BeamShear,
            PoissonUncracked = p.PoissonUncracked, ShellForceAngles = shellForceAngles, OnStep = onStep,
            RecordSectionStrains = p.RecordSectionStrains,
            PhysicallyLinear = !p.PhysicalNonlinearity,
            Solver = new CSfea.Core.SecantPicardOptions
            {
                MaxIterations = p.MaxIterations, TolDisplacement = p.TolDisplacement, TolStiffness = p.TolStiffness,
                MaxBisections = p.MaxBisections, Omega0 = p.Omega0, Geometric = p.GeomNonlinear,
                ShellInPlanePDelta = p.ShellInPlanePDelta, GeometricSecondOrder = !p.GeomLargeDisplacements,
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
    /// <param name="sections">false — без сечений CScore пластин и стержней (упругие по свойствам КЭ): вход для
    /// нагрузок и геометрии, без подготовки диаграмм.</param>
    public static FemRcModelInput Build(DatabaseService db, int schemaId, FemCsfeaSetup setup, bool sections = true) =>
        Build(db, schemaId, setup, out _, sections);

    /// <param name="fragmentBc">ГУ разрезов схемы-фрагмента (автозакрепления — для сверки реакций после расчёта);
    /// null — схема не фрагмент.</param>
    public static FemRcModelInput Build(DatabaseService db, int schemaId, FemCsfeaSetup setup,
        out FragmentCutBoundaryResult? fragmentBc, bool sections = true)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(setup);
        var diag = new List<FemValidationDiagnostic>();
        var data = FemCheckSchemaData.Load(db, schemaId);
        foreach (var error in data.Errors) diag.Add(new("rebar_file", error, false));

        var schema = db.FemSchemas.FirstOrDefault(s => s.Id == schemaId);
        var groups = schema?.MemberGroups.ToList() ?? [];
        var loadCases = db.GetFemLoadCases(schemaId);
        var properties = FemSelfWeightSourceFactory.Create(db, schemaId, out var notes, setup.GrossBars, setup.ReducedStiffness);
        foreach (var note in notes) diag.Add(new("properties_default", note, false));

        var materials = new Dictionary<int, Material>();
        foreach (var m in db.Materials) materials.TryAdd(m.Id, m);
        var plates = sections ? PlateSections(db, data, groups, materials, setup, diag) : [];
        var beams = sections ? BeamSections(db, data, materials, diag) : [];

        // Линейные связи граней пластин — узловыми закреплениями и пружинами сетки (дальше общий путь резолвера ГУ).
        var lines = db.ResolveFemLineSupports(schemaId, data.Members, data.MeshNodes, data.Mesh, data.Regions);
        diag.AddRange(lines.Diagnostics);
        var (supports, springs) = lines.AppendTo(db.GetFemMeshNodeSupports(schemaId), FemFieldService.Effective(db, schemaId).Springs.ToList());

        var nodes = db.GetFemNodes(schemaId);
        var kinematic = db.GetFemKinematicLoads(schemaId);
        var stages = Stages(setup.Stages, loadCases, diag);
        IReadOnlyList<FemLoadCase> cases = loadCases;
        IReadOnlyList<FemMeshNodeLoad> meshNodeLoads = db.GetFemMeshNodeLoads(schemaId);
        IReadOnlyList<FemMeshNodeKinematicLoad> meshKinematic = [];
        fragmentBc = null;
        if (schema is { IsFragment: true })
        {
            fragmentBc = FragmentBoundary(db, schema, data.MeshNodes, nodes, supports, springs, kinematic, stages, loadCases);
            diag.AddRange(fragmentBc.Diagnostics);
            if (!fragmentBc.HasErrors)
                (supports, meshNodeLoads, meshKinematic, cases, stages) =
                    ApplyFragmentBoundary(fragmentBc, supports, meshNodeLoads, loadCases, stages);
        }

        return new FemRcModelInput
        {
            MeshNodes = data.MeshNodes,
            MeshElements = data.Mesh,
            Nodes = nodes,
            Members = data.Members,
            Groups = groups,
            Supports = supports,
            Springs = springs,
            RigidBodies = db.GetFemRigidBodies(schemaId),
            LoadCases = cases,
            NodeLoads = db.GetFemNodeLoads(schemaId),
            MemberLoads = db.GetFemMemberLoads(schemaId),
            ElementLoads = FragmentLoadService.ElementLoads(db, schemaId, data.MeshNodes, data.Mesh, groups, diag),
            MeshNodeLoads = meshNodeLoads,
            KinematicLoads = kinematic,
            MeshNodeKinematicLoads = meshKinematic,
            Properties = properties,
            PlateSection = plates.Count == 0 ? null : e => plates.GetValueOrDefault(e.ElemTag),
            BeamSection = beams.Count == 0 ? null : e => beams.GetValueOrDefault(e.ElemTag),
            Calc = setup.Calc,
            RegionAxisX = RegionAxes(data),
            BeamShear = setup.BeamShear,
            Stages = stages,
            Diagnostics = diag,
        };
    }

    // ---------------------------------------------------------------- ГУ разрезов фрагмента

    /// <summary>
    /// ГУ разрезов фрагмента (спека масок 5.1–5.3): пресет схемы, правки разрезов, действующие маски; прочие связи схемы
    /// (опоры и пружины сетки, закрепления и кинематика узлов схемы) — для проверки жёстких смещений.
    /// </summary>
    static FragmentCutBoundaryResult FragmentBoundary(DatabaseService db, FemSchema schema, IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemNode> nodes, IReadOnlyList<FemMeshNodeSupport> supports, IReadOnlyList<FemSpring> springs,
        IReadOnlyList<FemKinematicLoad> kinematic, IReadOnlyList<FemRcStage> stages, IReadOnlyList<FemLoadCase> loadCases)
    {
        var withIds = db.GetFragmentCutsWithIds(schema.Id);
        var inputs = StoreyFragmentMaskService.CutInputs(db, schema.Id, withIds.Select(c => c.Cut).ToList());
        var meshByTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in meshNodes) meshByTag.TryAdd(n.NodeTag, n);
        var nodeByTag = new Dictionary<string, FemNode>(StringComparer.Ordinal);
        foreach (var n in nodes) nodeByTag.TryAdd(n.NodeTag, n);
        var nodeById = nodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());

        var other = new List<KinematicDofRef>();
        void Mask(PlanarVector3? p, int mask)
        {
            if (p is not { } x) return;
            for (int d = 0; d < 6; d++)
                if ((mask & 1 << d) != 0) other.Add(new KinematicDofRef(x, d));
        }
        PlanarVector3? MeshPoint(string tag) => meshByTag.TryGetValue(tag, out var n) ? new PlanarVector3(n.X, n.Y, n.Z) : null;
        PlanarVector3 NodePoint(FemNode n) => new(n.X, n.Y, n.Z);
        foreach (var s in supports) Mask(MeshPoint(s.NodeTag), s.Mask);
        foreach (var n in nodes) Mask(NodePoint(n), n.DofMask);
        foreach (var s in springs)
        {
            var p = s.TargetKind == FemSpringTargetKinds.Node
                ? nodeByTag.TryGetValue(s.NodeTag, out var n) ? NodePoint(n) : (PlanarVector3?)null
                : MeshPoint(s.NodeTag);
            var k = s.Stiffnesses;
            Mask(p, Enumerable.Range(0, 6).Where(d => k[d] != 0).Aggregate(0, (m, d) => m | 1 << d));
        }
        foreach (var l in kinematic)
            if (nodeById.TryGetValue(l.NodeId, out var n) && l.Dof is >= 1 and <= 6) Mask(NodePoint(n), 1 << (l.Dof - 1));

        return FragmentCutBoundary.Resolve(new FragmentCutBoundaryInput
        {
            Cuts = withIds.Select((c, i) => (c.Id, inputs[i])).ToList(),
            Preset = FragmentCutBc.ParsePreset(schema.FragmentBcPreset),
            Overrides = db.GetFragmentCutBc(schema.Id),
            Masks = db.GetFragmentMasks(schema.Id),
            MeshNodes = meshNodes,
            OtherConstraints = other,
            Stages = stages.Select(s => new FragmentBcStage(s.Tag, s.Terms)).ToList(),
            LoadCaseTags = loadCases.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Tag),
        });
    }

    /// <summary>
    /// ГУ разрезов во вход адаптера: закрепления (с автозакреплениями) — к опорам сетки, силы масок — узловыми нагрузками
    /// сетки, перемещения — заданными перемещениями узлов сетки; загружения-приращения масок стадий — к загружениям и в
    /// свои стадии с коэффициентом 1. В БД ничего не пишется.
    /// </summary>
    static (List<FemMeshNodeSupport>, IReadOnlyList<FemMeshNodeLoad>, IReadOnlyList<FemMeshNodeKinematicLoad>,
        IReadOnlyList<FemLoadCase>, List<FemRcStage>) ApplyFragmentBoundary(FragmentCutBoundaryResult bc,
        IReadOnlyList<FemMeshNodeSupport> supports, IReadOnlyList<FemMeshNodeLoad> meshNodeLoads,
        IReadOnlyList<FemLoadCase> loadCases, IReadOnlyList<FemRcStage> stages)
    {
        var merged = new List<FemMeshNodeSupport>(supports.Count + bc.Supports.Count);
        var pending = new Dictionary<string, int>(bc.Supports, StringComparer.Ordinal);
        foreach (var s in supports)
        {
            if (!pending.Remove(s.NodeTag, out int mask)) { merged.Add(s); continue; }
            merged.Add(new FemMeshNodeSupport { Id = s.Id, SchemaId = s.SchemaId, NodeTag = s.NodeTag, Mask = s.Mask | mask, Origin = s.Origin });
        }
        foreach (var (tag, mask) in pending.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            merged.Add(new FemMeshNodeSupport { NodeTag = tag, Mask = mask, Origin = FragmentCutOrigin });

        var loads = meshNodeLoads.Concat(bc.Loads.Select(l => new FemMeshNodeLoad
        {
            LoadCaseId = l.LoadCaseId, MeshNodeTag = l.MeshNodeTag, Origin = FragmentCutOrigin,
            Fx = l.Force.X, Fy = l.Force.Y, Fz = l.Force.Z, Mx = l.Force.Rx, My = l.Force.Ry, Mz = l.Force.Rz,
        })).ToList();
        var kinematic = bc.Prescribed.Select(p => new FemMeshNodeKinematicLoad(p.LoadCaseId, p.MeshNodeTag, p.Dof + 1, p.Value)).ToList();

        var cases = loadCases.Concat(bc.StageCases.Select(c => new FemLoadCase { Id = c.LoadCaseId, Tag = c.Tag, Origin = FragmentCutOrigin })).ToList();
        var result = stages.ToList();
        foreach (var c in bc.StageCases)
            result[c.StageIndex] = result[c.StageIndex] with { Terms = [.. result[c.StageIndex].Terms, (c.LoadCaseId, 1.0)] };
        return (merged, loads, kinematic, cases, result);
    }

    /// <summary>Происхождение ГУ и нагрузок, собранных из разрезов фрагмента (только во входе расчёта).</summary>
    public const string FragmentCutOrigin = "fragment:cut";

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
    static Dictionary<string, PlanarVector3>? RegionAxes(FemCheckSchemaData data) => RegionAxes(data.Members, data.Regions);

    /// <summary>Ось x выдачи усилий пластин по тегу КонЭ — ось X области (<see cref="FemRcModelInput.RegionAxisX"/>).</summary>
    public static Dictionary<string, PlanarVector3>? RegionAxes(IReadOnlyList<FemMember> members, IReadOnlyList<PlanarRegion> regionList)
    {
        if (regionList.Count == 0) return null;
        var regions = new Dictionary<int, PlanarRegion>();
        foreach (var r in regionList) regions.TryAdd(r.Id, r);
        var result = new Dictionary<string, PlanarVector3>(StringComparer.Ordinal);
        foreach (var m in members)
            if (m.PlanarRegionId is int id && regions.TryGetValue(id, out var region))
                result.TryAdd(m.ElemTag, region.Frame.LocalX);
        return result;
    }

    // ---------------------------------------------------------------- стержни

    /// <summary>
    /// Сечения CScore стержневых КЭ по тегу: сечение КЭ, иначе КонЭ; клон с материалами и диаграммами — один на
    /// сечение. GJ — по <see cref="ProjectElementStiffnessSource"/> (ручное значение, иначе Ix + Iy) над подготовленными
    /// клонами: характеристики считаются по волокнам, а у сечений из БД их нет — GJ был бы нулём (механизм кручения у
    /// цепочки стержней без плит).
    /// </summary>
    static Dictionary<string, FemRcBeamCross> BeamSections(DatabaseService db, FemCheckSchemaData data,
        IReadOnlyDictionary<int, Material> materials, List<FemValidationDiagnostic> diag)
    {
        var result = new Dictionary<string, FemRcBeamCross>(StringComparer.Ordinal);
        var memberByTag = new Dictionary<string, FemMember>(StringComparer.Ordinal);
        foreach (var m in data.Members) memberByTag.TryAdd(m.ElemTag, m);
        var sections = new Dictionary<int, CrossSection>();
        foreach (var s in db.CrossSections) sections.TryAdd(s.Id, s);

        var prepared = new Dictionary<int, CrossSection?>();
        var missing = new List<string>();
        var saintVenant = new List<string>();
        var bars = new List<(FemElement Element, CrossSection Section, int Id)>();
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
            bars.Add((e, cs, id));
        }
        var gj = new ProjectElementStiffnessSource(data.Members, prepared.Values.OfType<CrossSection>(), [], materials.Values);
        foreach (var (e, cs, id) in bars)
        {
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
