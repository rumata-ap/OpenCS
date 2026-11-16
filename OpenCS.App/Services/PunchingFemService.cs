using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.Sp63Punching;
using CScore.Sp63Punching.Fem;
using CSfea.CScoreBridge.Structural;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Сетка схемы для продавливания: модель поиска, найденные узлы, плиты и КЭ по тегу.</summary>
public sealed class PunchingFemSchemaData
{
    public required FemSchema Schema { get; init; }
    public required PunchingFemModel Model { get; init; }
    public required IReadOnlyList<PunchingFemJoint> Joints { get; init; }
    public required IReadOnlyList<PunchingFemSlab> Slabs { get; init; }
    public required IReadOnlyDictionary<string, FemElement> ElementsByTag { get; init; }
    /// <summary>Пластины каждой плиты по её индексу.</summary>
    public required IReadOnlyList<IReadOnlySet<string>> SlabShells { get; init; }
    /// <summary>Геометрия КЭ и КЭ у узлов для разреза основания — одна на схему (строится при первом разрезе).</summary>
    public PunchingFemCut.MeshIndex CutIndex => _cutIndex ??= new PunchingFemCut.MeshIndex(Model.Nodes, Model.Elements);
    PunchingFemCut.MeshIndex? _cutIndex;
}

/// <summary>
/// Источник усилий проверки: ключи (загружения, сочетания) с подписями, результат (силы концов стержней), узловые силы
/// КЭ (у CSfea — для разреза) и нагрузки основания пирамиды по ключам.
/// </summary>
public sealed class PunchingFemSource
{
    public required string Kind { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<(FragmentMaskKey Key, string Label)> Keys { get; init; }
    public required IFragmentParentResult Result { get; init; }
    /// <summary>Узловые силы КЭ (способ C); null — разрез по узловым силам недоступен.</summary>
    public IFragmentParentNodalForces? Nodal { get; init; }
    /// <summary>Почему недоступны узловые силы (для журнала); null — доступны.</summary>
    public string? NodalUnavailable { get; init; }
    /// <summary>Без узловых сил — справочный разрез по усилиям пластин (<see cref="PunchingFemShellCut"/>).</summary>
    public bool ShellCut { get; init; }
    /// <summary>
    /// Нагрузки (и реакции опор у CSfea) внутри границ по ключам, Н и Н·м относительно точки; null — нагрузки схемы
    /// неизвестны.
    /// </summary>
    public Func<FragmentBounds, PlanarVector3, IReadOnlyDictionary<FragmentMaskKey, (PlanarVector3 F, PlanarVector3 M)>>? BaseLoads { get; init; }
}

/// <summary>Усилия узла по ключам и сведения о разрезе.</summary>
/// <param name="ByKey">Усилия по ключу; ключа нет — нет сил стержней (причина — <see cref="Missing"/>).</param>
/// <param name="CutFailure">Почему не считался разрез основания (null — считался или не нужен).</param>
public sealed record PunchingFemNodeForces(
    IReadOnlyDictionary<FragmentMaskKey, PunchingFemForces> ByKey, string? CutFailure, string? Missing);

/// <summary>
/// Продавливание по МКЭ-схеме (спека 2026-11-15, срез 3): сетка схемы → узлы (<see cref="PunchingFemJointFinder"/>) →
/// усилия по ключам источника: по стержням колонн минус нагрузка основания пирамиды (S) и по разрезу основания из
/// узловых сил КЭ (C, только CSfea) → параметры одиночной задачи <see cref="PunchingTaskParams"/> узла и ключа.
/// Всё читается из БД здесь — вызывать в потоке соединения.
/// </summary>
public static class PunchingFemService
{
    static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Допуск полосы нагрузок основания по высоте около плиты, м: стержни колонн почти не попадают.</summary>
    const double BaseLoadHalfBand = 2e-3;

    // ------------------------------------------------------------------ сетка

    /// <summary>Сетка схемы и найденные узлы продавливания.</summary>
    public static PunchingFemSchemaData Load(DatabaseService db, FemSchema schema, PunchingFemOptions options)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(schema);
        var model = BuildModel(db, schema.Id);
        var joints = PunchingFemJointFinder.Find(model, options, out var slabs);
        return new PunchingFemSchemaData
        {
            Schema = schema, Model = model, Joints = joints, Slabs = slabs,
            ElementsByTag = model.Elements.GroupBy(e => e.ElemTag, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
            SlabShells = slabs.Select(s => (IReadOnlySet<string>)s.Shells.Select(x => x.Element.ElemTag).ToHashSet(StringComparer.Ordinal)).ToList(),
        };
    }

    /// <summary>
    /// Модель поиска узлов по сетке схемы: пластины — сечение конструктивного элемента (CSfea) или толщина и материал
    /// КЭ (импорт); стержни — сечение КЭ или конструктивного элемента (контур бетонных областей).
    /// </summary>
    public static PunchingFemModel BuildModel(DatabaseService db, int schemaId)
    {
        var members = db.GetFemMembers(schemaId);
        var memberByTag = new Dictionary<string, FemMember>(StringComparer.Ordinal);
        foreach (var m in members) memberByTag.TryAdd(m.ElemTag, m);
        var nodes = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in db.GetFemMeshNodes(schemaId)) nodes.TryAdd(n.NodeTag, n);
        var elements = CsfeaFieldsParentResult.WithBeamRotation(db.GetFemMeshElements(schemaId), members);
        var plates = db.PlateSections.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        var sections = db.CrossSections.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());

        var shellCache = new Dictionary<string, PunchingFemShellProps?>(StringComparer.Ordinal);
        PunchingFemShellProps? Shell(FemElement e)
        {
            string key = e.SourceMemberTag != null && memberByTag.TryGetValue(e.SourceMemberTag, out var mm) && mm.PlateSectionId is int pid
                ? "ps:" + pid.ToString(CultureInfo.InvariantCulture)
                : "el:" + (e.ThicknessM?.ToString("R", CultureInfo.InvariantCulture) ?? "") + "|" + (e.MaterialTag ?? "");
            if (shellCache.TryGetValue(key, out var cached)) return cached;
            PunchingFemShellProps? props = null;
            if (key.StartsWith("ps:", StringComparison.Ordinal) && plates.TryGetValue(int.Parse(key[3..], CultureInfo.InvariantCulture), out var ps) && ps.H > 0)
                props = new PunchingFemShellProps(key, ps.H, ps.ConcreteMaterialId, Cover(ps, top: true), Cover(ps, top: false),
                    string.IsNullOrEmpty(ps.Tag) ? $"#{ps.Id}" : ps.Tag);
            else if (e.ThicknessM is double h && h > 0)
                props = new PunchingFemShellProps(key, h, 0, null, null,
                    string.Create(Ru, $"h = {h * 1000:0} мм{(string.IsNullOrEmpty(e.MaterialTag) ? "" : ", " + e.MaterialTag)}"));
            shellCache[key] = props;
            return props;
        }

        var barCache = new Dictionary<int, PunchingFemBarSection?>();
        PunchingFemBarSection? Bar(FemElement e)
        {
            int? id = e.CrossSectionId ?? (e.SourceMemberTag != null && memberByTag.TryGetValue(e.SourceMemberTag, out var m) ? m.CrossSectionId : null);
            if (id is not int sid) return null;
            if (barCache.TryGetValue(sid, out var cached)) return cached;
            PunchingFemBarSection? s = sections.TryGetValue(sid, out var cs) && RigidJunctionSectionOutline.From(cs) is { Count: >= 3 } outline
                ? new PunchingFemBarSection(outline, cs.Tag)
                : null;
            barCache[sid] = s;
            return s;
        }

        var supported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in db.GetFemMeshNodeSupports(schemaId)) if (s.Mask != 0) supported.Add(s.NodeTag);
        foreach (var s in db.GetFemSprings(schemaId)) if (s.TargetKind == FemSpringTargetKinds.MeshNode) supported.Add(s.NodeTag);
        var fixedSchemaNodes = db.GetFemNodes(schemaId).Where(n => n.DofMask != 0).Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal);
        foreach (var n in nodes.Values) if (n.SourceNodeTag is { } t && fixedSchemaNodes.Contains(t)) supported.Add(n.NodeTag);

        return new PunchingFemModel
        {
            Nodes = nodes, Elements = elements, RigidBodies = db.GetFemRigidBodies(schemaId),
            ShellProps = Shell, BarSection = Bar, SupportedNodes = supported,
        };
    }

    /// <summary>Привязки центров крайнего слоя арматуры у грани (верхней — +n, нижней — −n), м; null — слоя нет.</summary>
    static PunchingFemCover? Cover(PlateSection ps, bool top)
    {
        var layers = ps.RebarLayers.Where(l => top ? l.Face == RebarFace.PlusN || l.Face != RebarFace.MinusN && l.Zsx > 0
                                                 : l.Face == RebarFace.MinusN || l.Face != RebarFace.PlusN && l.Zsx < 0).ToList();
        if (layers.Count == 0) return null;
        var outer = top ? layers.MaxBy(l => Math.Max(l.Zsx, l.Zsy))! : layers.MinBy(l => Math.Min(l.Zsx, l.Zsy))!;
        double half = ps.H / 2;
        return top ? new PunchingFemCover(half - outer.Zsx, half - outer.Zsy) : new PunchingFemCover(half + outer.Zsx, half + outer.Zsy);
    }

    // ------------------------------------------------------------------ источник CSfea

    /// <summary>
    /// Источник по постановке CSfea схемы: ключи — загружения и стадии сохранённого результата, силы концов стержней и
    /// поля пластин (<see cref="CsfeaFieldsParentResult"/>), узловые силы КЭ (<see cref="CsfeaNodalForcesSource"/>) и
    /// нагрузки модели. null — постановки или результата нет (причина — ошибкой в <paramref name="diag"/>).
    /// </summary>
    public static PunchingFemSource? OpenCsfea(DatabaseService db, FemSchema schema, int analysisId, List<FemValidationDiagnostic> diag)
    {
        ArgumentNullException.ThrowIfNull(db);
        PunchingFemSource? Fail(string code, string message)
        {
            diag.Add(new(code, message, true));
            return null;
        }
        if (db.GetFemAnalysis(analysisId) is not { } analysis || analysis.SchemaId != schema.Id)
            return Fail("punching_fem_no_analysis", "Постановка расчёта CSfea не найдена у схемы.");
        bool linear = analysis.Kind == FemCsfeaRunner.LinearAnalysisKind;
        if (!linear && analysis.Kind != FemCsfeaRunner.AnalysisKind)
            return Fail("punching_fem_analysis_kind", $"Постановка «{analysis.Tag}» — не расчёт CSfea.");
        if (analysis.ResultId is not int resultId || db.GetCalcResultById(resultId) is not { } result
            || FemCsfeaResultSummary.Parse(result.DataJson) is not { } summary)
            return Fail("punching_fem_no_result", $"У постановки «{analysis.Tag}» нет сохранённого результата — выполните расчёт.");

        var loadCases = db.GetFemLoadCases(schema.Id);
        var warnings = new List<string>();
        var identity = loadCases.ToDictionary(c => c.Id, c => c.Id);
        var stages = FemAnalysisParams.Parse(analysis.ParamsJson).ResolveStages(analysis);
        var steps = new List<(FragmentMaskKey Key, RcSecantStepFields Fields, string Label)>();
        foreach (var step in FragmentMaskSourceKeys.Select(summary, stages, loadCases, identity, warnings))
        {
            RcSecantStepFields? fields = null;
            if (db.GetFemResultStep(resultId, step.StepN) is { } data)
                try { fields = FemCsfeaStepFieldsCodec.Unpack(data); }
                catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { }
            if (fields == null) warnings.Add($"Стадия «{step.Label}»: поля шага не читаются — пропущена.");
            else if (steps.All(s => s.Key != step.Key)) steps.Add((step.Key, fields, step.Label));
        }
        foreach (var w in warnings) diag.Add(new("punching_fem_source", w, false));
        if (steps.Count == 0)
            return Fail("punching_fem_no_keys", $"В результате «{analysis.Tag}» нет загружений или стадий.");

        var nodes = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in db.GetFemMeshNodes(schema.Id)) nodes.TryAdd(n.NodeTag, n);
        var members = db.GetFemMembers(schema.Id);
        var elements = db.GetFemMeshElements(schema.Id);
        var regionAxes = FemCsfeaInputBuilder.RegionAxes(members, db.GetPlanarRegions(schema.Id));
        var parentResult = new CsfeaFieldsParentResult(steps.Select(s => (s.Key, s.Fields)).ToList(), nodes, elements, members, regionAxes);
        var fieldsByKey = steps.ToDictionary(s => s.Key, s => s.Fields);
        var setup = FemCsfeaSetup.FromAnalysis(analysis, loadCases);
        var parent = StoreyFragmentMaskService.ParentModelOf(db, schema.Id, setup, sections: true);
        var nodalDiag = new List<FemValidationDiagnostic>();
        var nodal = StoreyFragmentMaskService.NodalSource(db, schema.Id, analysis, summary, linear, setup, parent, parentResult,
            fieldsByKey, id => id, nodalDiag);
        string? unavailable = nodal == null
            ? nodalDiag.FirstOrDefault()?.Message.Replace("Силы кромок пластин — МНК по центрам КЭ: ", "", StringComparison.Ordinal) ?? "нет данных"
            : null;
        var loadsModel = parent.Adapted.HasErrors ? StoreyFragmentMaskService.ParentModelOf(db, schema.Id, setup, sections: false) : parent;
        bool loadsKnown = loadsModel.Adapted.Model.LoadCases.Count > 0;
        // Разбор нагрузок готовится один раз на источник: на узел — только отсечение по основанию.
        var preparedLoads = new Lazy<FragmentBandLoads.Prepared>(() => new FragmentBandLoads.Prepared(loadsModel.Input, loadsModel.Loads));

        return new PunchingFemSource
        {
            Kind = linear ? FragmentMaskSet.SourceCsfeaLinear : FragmentMaskSet.SourceCsfeaSecant,
            Label = analysis.Tag,
            Keys = steps.Select(s => (s.Key, s.Label)).ToList(),
            Result = parentResult,
            Nodal = nodal,
            NodalUnavailable = unavailable,
            BaseLoads = !loadsKnown ? null : (bounds, origin) =>
            {
                var perCase = preparedLoads.Value.Compute(bounds, origin);
                var byKey = new Dictionary<FragmentMaskKey, (PlanarVector3 F, PlanarVector3 M)>();
                foreach (var (key, fields, _) in steps)
                {
                    var f = PlanarVector3.Zero;
                    var m = PlanarVector3.Zero;
                    foreach (var (lc, k) in key.Terms())
                        if (perCase.TryGetValue(lc, out var l)) { f += l.F * k; m += l.M * k; }
                    var (rf, rm) = Reactions(fields, nodes, bounds, origin);
                    byKey[key] = (f + rf, m + rm);
                }
                return byKey;
            },
        };
    }

    /// <summary>
    /// Источник по снимку результатов программы во вложении схемы (ЛИРА-САПР, SCAD; меню «… → Результаты для
    /// фрагментов…»): загружения снимка, сопоставленные загружениям схемы, и сочетания схемы (определения загружений) —
    /// линейной суперпозицией. Узловых сил КЭ нет: главный способ — по стержням колонн, разрез — справочно по усилиям
    /// пластин. null — снимка нет или он не читается (причина — ошибкой в <paramref name="diag"/>).
    /// </summary>
    public static PunchingFemSource? OpenImported(DatabaseService db, FemSchema schema, string sourceKind, List<FemValidationDiagnostic> diag)
    {
        ArgumentNullException.ThrowIfNull(db);
        PunchingFemSource? Fail(string code, string message)
        {
            diag.Add(new(code, message, true));
            return null;
        }
        string program = sourceKind == FragmentMaskSet.SourceLira ? "ЛИРА-САПР" : "SCAD";
        string menu = sourceKind == FragmentMaskSet.SourceLira ? "ЛИРА" : "SCAD";
        if (db.GetFemSchemaSourceFile(schema.Id, StoreyFragmentMaskService.SnapshotFileKind(sourceKind)) is not { } file)
            return Fail("punching_fem_no_snapshot", $"У схемы нет снимка результатов {program}: меню схемы «{menu} → Результаты для фрагментов…».");
        ImportedResultSnapshot snapshot;
        try { snapshot = ImportedResultSnapshotCodec.Decode(file.Data); }
        catch (InvalidDataException ex) { return Fail("punching_fem_snapshot_broken", $"Снимок результатов {program} не читается: {ex.Message}"); }

        var loadCases = db.GetFemLoadCases(schema.Id);
        var warnings = new List<string>();
        var identity = loadCases.ToDictionary(c => c.Id, c => c.Id);
        var byNum = FragmentMaskSourceKeys.ImportedLoadCaseMap(snapshot.Loads, loadCases, FemLoadOrigin.Import(sourceKind), identity, warnings);
        foreach (var w in warnings) diag.Add(new("punching_fem_source", w, false));
        if (byNum.Count == 0)
            return Fail("punching_fem_no_keys", $"Ни одно загружение снимка {program} не сопоставлено загружениям схемы.");
        var nodes = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in db.GetFemMeshNodes(schema.Id)) nodes.TryAdd(n.NodeTag, n);
        var elements = db.GetFemMeshElements(schema.Id);
        var inner = new ImportedParentResult(snapshot, byNum, nodes, elements,
            sourceKind == FragmentMaskSet.SourceLira ? ImportedForceConvention.Lira : ImportedForceConvention.Scad);
        var known = byNum.Values.ToHashSet();
        var caseTag = loadCases.ToDictionary(c => c.Id, c => c.Tag);

        // Ключи: сочетания схемы (по загружениям снимка), без них — загружения.
        var keys = new List<(FragmentMaskKey Key, string Label)>();
        foreach (var d in db.GetFemLoadDefinitions(schema.Id))
        {
            var e = d.GetExpression();
            IReadOnlyList<(int, double)> terms = e.Mode switch
            {
                FemLoadExpressionMode.Single => e.LoadCaseIds.Select(id => (id, 1.0)).ToList(),
                FemLoadExpressionMode.All => loadCases.Select(c => (c.Id, 1.0)).ToList(),
                _ => e.Terms.Select(t => (t.LoadCaseId, t.Coefficient)).ToList(),
            };
            if (terms.Count == 0) continue;
            if (terms.Any(t => !known.Contains(t.Item1)))
            {
                diag.Add(new("punching_fem_combination_skipped",
                    $"Сочетание «{d.Tag}»: есть загружения без результатов в снимке {program} — пропущено.", false));
                continue;
            }
            var key = FragmentMaskKey.OfStageTerms(terms);
            if (keys.All(k => k.Key != key)) keys.Add((key, d.Tag));
        }
        if (keys.Count == 0)
        {
            diag.Add(new("punching_fem_no_combinations", $"В схеме нет сочетаний по загружениям снимка {program} — проверка по " +
                "отдельным загружениям (задайте сочетания: продавливание проверяется по расчётным сочетаниям).", false));
            foreach (int id in known.Order())
                keys.Add((FragmentMaskKey.OfStageTerms([(id, 1.0)]), caseTag.GetValueOrDefault(id, $"#{id}")));
        }

        Func<FragmentBounds, PlanarVector3, IReadOnlyDictionary<FragmentMaskKey, (PlanarVector3 F, PlanarVector3 M)>>? baseLoads = null;
        try
        {
            var model = StoreyFragmentMaskService.ParentModelOf(db, schema.Id, new FemCsfeaSetup(), sections: false);
            var prepared = new Lazy<FragmentBandLoads.Prepared>(() => new FragmentBandLoads.Prepared(model.Input, model.Loads));
            if (model.Adapted.Model.LoadCases.Count > 0)
                baseLoads = (bounds, origin) =>
                {
                    var perCase = prepared.Value.Compute(bounds, origin);
                    var byKey = new Dictionary<FragmentMaskKey, (PlanarVector3 F, PlanarVector3 M)>();
                    foreach (var (key, _) in keys)
                    {
                        var f = PlanarVector3.Zero;
                        var m = PlanarVector3.Zero;
                        foreach (var (lc, k) in key.Terms())
                            if (perCase.TryGetValue(lc, out var l)) { f += l.F * k; m += l.M * k; }
                        byKey[key] = (f, m);
                    }
                    return byKey;
                };
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            diag.Add(new("punching_fem_loads_unknown", $"Нагрузки схемы не собраны ({ex.Message}) — сила без вычета нагрузки основания (в запас).", false));
        }

        return new PunchingFemSource
        {
            Kind = sourceKind,
            Label = $"{program} — снимок {snapshot.TakenAt.Replace('T', ' ')}",
            Keys = keys,
            Result = new PunchingFemCombinedResult(inner, keys.Select(k => k.Key).ToList()),
            Nodal = null,
            NodalUnavailable = $"импорт {program} — узловых сил КЭ программы нет",
            ShellCut = true,
            BaseLoads = baseLoads,
        };
    }

    /// <summary>Реакции опор внутри границ из полей шага CSfea: сила и момент относительно <paramref name="origin"/>.</summary>
    static (PlanarVector3 F, PlanarVector3 M) Reactions(RcSecantStepFields fields, IReadOnlyDictionary<string, FemMeshNode> nodes,
        FragmentBounds bounds, PlanarVector3 origin)
    {
        var f = PlanarVector3.Zero;
        var m = PlanarVector3.Zero;
        for (int i = 0; i < fields.ReactionNodeIds.Length; i++)
        {
            if (!nodes.TryGetValue(fields.ReactionNodeIds[i].ToString(CultureInfo.InvariantCulture), out var n)
                || !bounds.InBox(new PlanarVector3(n.X, n.Y, n.Z))) continue;
            var r = fields.Reactions.AsSpan(RcSecantStepFields.NodeComponents * i, 6);
            var rf = new PlanarVector3(r[0], r[1], r[2]);
            f += rf;
            m += new PlanarVector3(r[3], r[4], r[5]) + (new PlanarVector3(n.X, n.Y, n.Z) - origin).Cross(rf);
        }
        return (f, m);
    }

    // ------------------------------------------------------------------ усилия узла

    /// <summary>
    /// Усилия узла по всем ключам источника: S — действие стержней плюс нагрузка основания; C — разрез основания по
    /// узловым силам (если источник их даёт). Главный способ: <paramref name="main"/> или, при null, C у источника с
    /// узловыми силами и S у прочих. Разрез не удался — главный S.
    /// </summary>
    public static PunchingFemNodeForces Evaluate(PunchingFemSchemaData data, PunchingFemSource source, PunchingFemJoint joint,
        PunchingFemForceMethod? main = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(joint);
        var keys = source.Keys.Select(k => k.Key).ToList();

        // Нагрузки основания: тонкая полоса у плиты (стержни колонн в неё почти не попадают).
        IReadOnlyDictionary<FragmentMaskKey, (PlanarVector3 F, PlanarVector3 M)>? baseLoads =
            source.BaseLoads?.Invoke(PunchingFemColumnAction.BaseBounds(joint, BaseLoadHalfBand), joint.Origin);

        PunchingFemCutResult? cut = null;
        string? cutFailure = null;
        if (source.Nodal != null)
        {
            cut = PunchingFemCut.Compute(joint, data.Model.Nodes, data.CutIndex, data.SlabShells[joint.SlabIndex], keys, source.Nodal);
            cutFailure = cut.Failure;
        }
        else if (main == PunchingFemForceMethod.Contour && !source.ShellCut)
            cutFailure = source.NodalUnavailable ?? "у источника нет узловых сил КЭ";

        var byKey = new Dictionary<FragmentMaskKey, PunchingFemForces>();
        string? missing = null;
        if (joint.IsWall)
        {
            // Торец или угол стены (СП 430 п. 6.3.8): сила — только по разрезу плиты по длине контура (стена продолжается,
            // её сила на торец и середину не делится); ΔM у торца — по моментам плиты на ветвях П-контура.
            bool shellsWall = joint.Bars.Count == 0;
            foreach (var key in keys)
            {
                double? contour = cut?.Failure == null && cut?.ByKey.TryGetValue(key, out var cf) == true ? cf.Force
                    : cut == null && source.ShellCut ? PunchingFemShellCut.Compute(joint, data.Model.Nodes, data.ElementsByTag, source.Result, key)
                    : null;
                if (contour is not double c)
                {
                    missing ??= source.Nodal == null && !source.ShellCut
                        ? "у торцов и углов стен сила определяется только разрезом плиты, а у источника нет узловых сил КЭ"
                        : "разрез плиты у стены не удался";
                    continue;
                }
                double? dm = joint.Wall!.Kind == PunchingWallKind.End
                    ? PunchingFemShellCut.WallDeltaM(joint, data.Model.Nodes, data.ElementsByTag, source.Result, key)
                    : 0;
                // Действие стены в основании (CSfea, стена из пластин) — для самопроверки равновесия разреза.
                double? action = shellsWall && source.Nodal is { } nd
                    && PunchingFemColumnAction.ComputeShells(joint, data.Model.Nodes, nd, key) is var (af, _) ? af.Dot(joint.Normal) / 1e3 : null;
                double? baseLoad = baseLoads != null && baseLoads.TryGetValue(key, out var bl) ? bl.F.Dot(joint.Normal) / 1e3 : null;
                byKey[key] = new PunchingFemForces(Math.Abs(c), c >= 0 ? PunchingLoadSide.FromBelow : PunchingLoadSide.FromAbove,
                    dm ?? 0, 0, PunchingFemForceMethod.Contour, action, baseLoad, c)
                {
                    WallExcluded = cut?.Failure == null && cut?.ByKey.TryGetValue(key, out var ce) == true ? ce.WallExcluded : null,
                };
                if (dm == null) missing ??= "нет усилий пластин на ветвях П-контура (ΔM)";
            }
            return new PunchingFemNodeForces(byKey, cutFailure, missing);
        }
        foreach (var key in keys)
        {
            bool shells = joint.Kind == PunchingFemJointKind.PylonShells;
            var action = shells
                ? source.Nodal is { } nodal ? PunchingFemColumnAction.ComputeShells(joint, data.Model.Nodes, nodal, key) : null
                : PunchingFemColumnAction.Compute(joint, data.Model.Nodes, data.ElementsByTag, source.Result, key);
            if (action is not var (af, am))
            {
                missing ??= shells
                    ? source.Nodal == null
                        ? "пилон из пластин проверяется только по расчёту CSfea: силу стены на плиту дают узловые силы КЭ, а у источника их нет"
                        : "нет узловых сил пластин пилона"
                    : "нет сил концов стержней колонн в результате";
                continue;
            }
            double colAction = af.Dot(joint.Normal) / 1e3;
            double? baseLoad = baseLoads != null && baseLoads.TryGetValue(key, out var bl) ? bl.F.Dot(joint.Normal) / 1e3 : null;
            double? contour = cut?.Failure == null && cut?.ByKey.TryGetValue(key, out var cf) == true ? cf.Force
                : cut == null && source.ShellCut ? PunchingFemShellCut.Compute(joint, data.Model.Nodes, data.ElementsByTag, source.Result, key)
                : null;
            var method = main ?? (source.Nodal != null ? PunchingFemForceMethod.Contour : PunchingFemForceMethod.Column);
            if (method == PunchingFemForceMethod.Contour && contour == null) method = PunchingFemForceMethod.Column;
            double signed = method == PunchingFemForceMethod.Contour ? contour!.Value : colAction + (baseLoad ?? 0);
            byKey[key] = new PunchingFemForces(Math.Abs(signed), signed >= 0 ? PunchingLoadSide.FromBelow : PunchingLoadSide.FromAbove,
                am.Dot(joint.AxisX) / 1e3, am.Dot(joint.AxisY) / 1e3, method, colAction, baseLoad, contour)
            {
                PylonParts = cut?.Failure == null && cut?.ByKey.TryGetValue(key, out var pc) == true
                    ? PunchingFemColumnAction.PylonParts(joint, pc.Terms) : null,
            };
        }
        return new PunchingFemNodeForces(byKey, cutFailure, missing);
    }

    // ------------------------------------------------------------------ параметры задачи

    /// <summary>Края ядра в осях узла → края параметров одиночной задачи (сторона, c, угол скоса).</summary>
    public static List<PunchingEdgeParams> EdgeParams(PunchingLoadArea area, IReadOnlyList<PunchingSlabEdge> edges)
    {
        var list = new List<PunchingEdgeParams>();
        foreach (var e in edges)
        {
            var n = e.InwardNormal.Normalized();
            var s = PunchingFemJointFinder.SideOf(n);
            var o = new PlanPoint(-n.X, -n.Y);
            double angle = Math.Atan2(s.Cross(o), s.Dot(o)) * 180 / Math.PI;
            double hmin = area switch
            {
                PunchingLoadArea.Circle c => -c.D / 2,
                PunchingLoadArea.Ring r => -r.D / 2,
                _ => PunchingContourBuilder.AreaPolygon(area).Min(p => p.Dot(n)),
            };
            double c0 = hmin - n.Dot(e.PointOnEdge);
            string side = s.X > 0.5 ? "+x" : s.X < -0.5 ? "-x" : s.Y > 0.5 ? "+y" : "-y";
            list.Add(new PunchingEdgeParams { Side = side, C = Math.Max(0, c0), AngleDeg = Math.Round(angle, 9) });
        }
        return list;
    }

    /// <summary>Грузовая площадь ядра → параметры площади.</summary>
    public static PunchingAreaParams AreaParams(PunchingLoadArea area) => area switch
    {
        PunchingLoadArea.Rectangle r => new PunchingAreaParams { Shape = "rectangle", Bx = r.Bx, By = r.By },
        PunchingLoadArea.Circle c => new PunchingAreaParams { Shape = "circle", D = c.D },
        PunchingLoadArea.Ring r => new PunchingAreaParams { Shape = "ring", D = r.D, T = r.T },
        PunchingLoadArea.Polygon p => new PunchingAreaParams { Shape = "polygon", Points = p.Points.Select(q => new[] { q.X, q.Y }).ToList() },
        _ => new PunchingAreaParams(),
    };

    /// <summary>
    /// Параметры одиночной задачи узла и ключа: геометрия узла, плита (бетон узла или проверки), арматура и режимы
    /// проверки, усилия ключа (Mloc — по настройке), источник.
    /// </summary>
    public static PunchingTaskParams TaskParams(PunchingFemJoint joint, PunchingFemForces forces, PunchingFemCheckParams check,
        string schemaTag, string keyLabel)
    {
        ArgumentNullException.ThrowIfNull(joint);
        ArgumentNullException.ThrowIfNull(forces);
        ArgumentNullException.ThrowIfNull(check);
        var o = check.Overrides.FirstOrDefault(x => x.Node == joint.NodeTag);
        var area = joint.Area ?? new PunchingLoadArea.Rectangle(0.4, 0.4);
        var wall = joint.Wall is { } w
            ? new PunchingWallParams { Kind = w.Kind == PunchingWallKind.End ? "end" : "corner", A1 = w.A1, A2 = w.T2 }
            : new PunchingWallParams();
        return new PunchingTaskParams
        {
            Wall = wall,
            Area = wall.IsWall ? new PunchingAreaParams() : AreaParams(area),
            Slab = new PunchingSlabParams
            {
                H = joint.H, Ax = o?.Ax ?? joint.Ax, Ay = o?.Ay ?? joint.Ay,
                ConcreteMaterialId = joint.ConcreteMaterialId > 0 && check.ConcreteFromSchema ? joint.ConcreteMaterialId : check.ConcreteMaterialId,
                RbtOverride = check.RbtOverride,
            },
            Edges = EdgeParams(area, joint.Edges),
            Holes = joint.Holes.Select(h => new PunchingHoleParams { Shape = "polygon", Points = h.Polygon.Select(p => new[] { p.X, p.Y }).ToList() }).ToList(),
            Reinforcement = o?.Reinforcement ?? check.Reinforcement,
            Forces = new PunchingForceParams
            {
                // До 1 Н и 1 Н·м: в окне задачи по строке — читаемые числа; проверка считает по тем же значениям.
                F = Math.Round(forces.F, 3), Mx = Math.Round(forces.Mx, 3), My = Math.Round(forces.My, 3),
                MomentsAreMloc = check.MomentsAreMloc,
                LoadSide = forces.Side == PunchingLoadSide.FromAbove ? "from_above" : "from_below",
            },
            Options = check.Options,
            Source = new PunchingSourceParams
            {
                Schema = schemaTag, Node = joint.NodeTag, Key = keyLabel,
                Method = forces.Method == PunchingFemForceMethod.Contour ? "contour" : "column",
                ColumnAction = forces.ColumnAction, BaseLoad = forces.BaseLoad, ContourForce = forces.ContourForce,
            },
        };
    }
}
