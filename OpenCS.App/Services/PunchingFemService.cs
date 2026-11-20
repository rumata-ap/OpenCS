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
    /// <summary>Линии стен у плит (для плана перекрытия).</summary>
    public IReadOnlyList<PunchingFemWallLine> Walls { get; init; } = [];
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
    /// Нагрузки (и реакции опор у CSfea) внутри границ по заданным ключам, Н и Н·м относительно точки; ключ без данных
    /// пропускается. null — нагрузки схемы неизвестны.
    /// </summary>
    public Func<FragmentBounds, PlanarVector3, IReadOnlyList<FragmentMaskKey>, IReadOnlyDictionary<FragmentMaskKey, (PlanarVector3 F, PlanarVector3 M)>>? BaseLoads { get; init; }

    /// <summary>
    /// Реакция опоры узла сетки на схему по ключу, Н и Н·м (момент — относительно узла): у CSfea — реакция связи или
    /// пружины, у импорта — отпор пружины −K·u. null — реакции нет (жёсткая связь импорта, узел без опоры). Для свай-точек.
    /// </summary>
    public Func<string, FragmentMaskKey, (PlanarVector3 F, PlanarVector3 M)?>? NodeReaction { get; init; }

    /// <summary>
    /// Снимок импорта: номер загружения в программе → Id загружения схемы. У таких источников результат —
    /// суперпозиция загружений, и годится любой ключ-сочетание (<see cref="WithKeys"/>). null — не снимок импорта.
    /// </summary>
    public IReadOnlyDictionary<int, int>? ProgramLoadCases { get; init; }

    /// <summary>
    /// Тот же снимок импорта с другими ключами — например, сочетаниями ЛИРЫ, разложенными на загружения
    /// (<see cref="CScore.Import.LiraCombinationFit"/>). Только для снимков импорта (<see cref="ProgramLoadCases"/>).
    /// </summary>
    public PunchingFemSource WithKeys(IReadOnlyList<(FragmentMaskKey Key, string Label)> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (ProgramLoadCases == null || Result is not PunchingFemCombinedResult combined)
            throw new InvalidOperationException("Ключи-сочетания задаются только у снимка результатов импорта.");
        return new PunchingFemSource
        {
            Kind = Kind, Label = Label, Keys = keys, Result = combined.WithKeys(keys.Select(k => k.Key).ToList()),
            Nodal = Nodal, NodalUnavailable = NodalUnavailable, ShellCut = ShellCut, BaseLoads = BaseLoads,
            NodeReaction = NodeReaction, ProgramLoadCases = ProgramLoadCases,
        };
    }
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

    // ------------------------------------------------------------------ продавливание ЛИРЫ (*.asp)

    /// <summary>
    /// Подбор ЛИРЫ (*.asp), приложенный к схеме (у фрагмента — копия родителя): имя файла, разобранный файл и
    /// текст ошибки чтения. Null — файл не приложен.
    /// </summary>
    public static (string FileName, CScore.Import.LiraAspFile? File, string? Error)? LoadLiraAsp(DatabaseService db, int schemaId)
    {
        ArgumentNullException.ThrowIfNull(db);
        var (isFragment, _) = db.GetFemSchemaFragmentSource(schemaId);
        (string FileName, byte[] Data)? src = isFragment
            ? db.GetFragmentRebarSources(schemaId).FirstOrDefault(c => c.Kind == DatabaseService.FragmentRebarSourceLiraAsp) is { Data: not null } c
                ? (c.FileName, c.Data) : null
            : db.GetFemSchemaSelectedReinforcementFile(schemaId);
        if (src is not { } f) return null;
        try { return (f.FileName, CScore.Import.LiraAspReader.Read(f.Data), null); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        { return (f.FileName, null, ex.Message); }
    }

    /// <summary>Сопоставление узлов продавливания ЛИРЫ с найденными узлами схемы.</summary>
    public static PunchingFemLiraMatch MatchLira(CScore.Import.LiraAspFile? asp, IEnumerable<PunchingFemJoint> joints) =>
        asp is { Punching.Count: > 0 }
            ? PunchingFemLiraMatch.Build(asp.Punching, joints.Select(j => (j.NodeTag, j.Origin)).ToList())
            : PunchingFemLiraMatch.Empty;

    // ------------------------------------------------------------------ сетка

    /// <summary>Сетка схемы и найденные узлы продавливания.</summary>
    public static PunchingFemSchemaData Load(DatabaseService db, FemSchema schema, PunchingFemOptions options)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(schema);
        return Load(schema, BuildModel(db, schema.Id), options);
    }

    /// <summary>Найденные узлы продавливания по готовой модели сетки (<see cref="BuildModel"/>) — без чтения базы.</summary>
    public static PunchingFemSchemaData Load(FemSchema schema, PunchingFemModel model, PunchingFemOptions options)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(model);
        var joints = PunchingFemJointFinder.Find(model, options, out var slabs, out var walls);
        return new PunchingFemSchemaData
        {
            Schema = schema, Model = model, Joints = joints, Slabs = slabs, Walls = walls,
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

        const int dofZ = 1 << 2;
        var supported = new HashSet<string>(StringComparer.Ordinal);
        // Сваи (п. 2, С2): связь или пружина по Z — кандидат в сваю-точку; пружина — низ сваи-стержня.
        var vertical = new HashSet<string>(StringComparer.Ordinal);
        var springNodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in db.GetFemMeshNodeSupports(schemaId))
        {
            if (s.Mask != 0) supported.Add(s.NodeTag);
            if ((s.Mask & dofZ) != 0) vertical.Add(s.NodeTag);
        }
        var meshBySource = nodes.Values.Where(n => n.SourceNodeTag != null).ToLookup(n => n.SourceNodeTag!, n => n.NodeTag, StringComparer.Ordinal);
        foreach (var s in FemFieldService.EffectiveSprings(db, schemaId))
        {
            IEnumerable<string> tags = s.TargetKind == FemSpringTargetKinds.MeshNode ? [s.NodeTag] : meshBySource[s.NodeTag];
            foreach (var t in tags)
            {
                if (s.TargetKind == FemSpringTargetKinds.MeshNode) supported.Add(t);
                if (s.Kz > 0) vertical.Add(t);
                if (s.Kx > 0 || s.Ky > 0 || s.Kz > 0) springNodes.Add(t);
            }
        }
        foreach (var fn in db.GetFemNodes(schemaId).Where(n => n.DofMask != 0))
            foreach (var t in meshBySource[fn.NodeTag])
            {
                supported.Add(t);
                if ((fn.DofMask & dofZ) != 0) vertical.Add(t);
            }

        return new PunchingFemModel
        {
            Nodes = nodes, Elements = elements, RigidBodies = db.GetFemRigidBodies(schemaId),
            ShellProps = Shell, BarSection = Bar, SupportedNodes = supported, VerticalSupports = vertical, SpringNodes = springNodes,
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
        // Реакции по узлу — индекс узлов реакций на шаг (для свай-точек).
        var reactionIndex = new Dictionary<FragmentMaskKey, Dictionary<string, int>>();
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
            BaseLoads = !loadsKnown ? null : (bounds, origin, wanted) =>
            {
                var perCase = preparedLoads.Value.Compute(bounds, origin);
                var byKey = new Dictionary<FragmentMaskKey, (PlanarVector3 F, PlanarVector3 M)>();
                foreach (var key in wanted)
                {
                    // Реакции опор есть только у шагов расчёта.
                    if (!fieldsByKey.TryGetValue(key, out var fields)) continue;
                    var f = PlanarVector3.Zero;
                    var m = PlanarVector3.Zero;
                    foreach (var (lc, k) in key.Terms())
                        if (perCase.TryGetValue(lc, out var l)) { f += l.F * k; m += l.M * k; }
                    var (rf, rm) = Reactions(fields, nodes, bounds, origin);
                    byKey[key] = (f + rf, m + rm);
                }
                return byKey;
            },
            NodeReaction = (tag, key) =>
            {
                if (!fieldsByKey.TryGetValue(key, out var fields)) return null;
                if (!reactionIndex.TryGetValue(key, out var index))
                {
                    index = new Dictionary<string, int>(StringComparer.Ordinal);
                    for (int i = 0; i < fields.ReactionNodeIds.Length; i++)
                        index.TryAdd(fields.ReactionNodeIds[i].ToString(CultureInfo.InvariantCulture), i);
                    reactionIndex[key] = index;
                }
                if (!index.TryGetValue(tag, out int k)) return null;
                var r = fields.Reactions.AsSpan(RcSecantStepFields.NodeComponents * k, 6);
                return (new PlanarVector3(r[0], r[1], r[2]), new PlanarVector3(r[3], r[4], r[5]));
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

        Func<FragmentBounds, PlanarVector3, IReadOnlyList<FragmentMaskKey>, IReadOnlyDictionary<FragmentMaskKey, (PlanarVector3 F, PlanarVector3 M)>>? baseLoads = null;
        Func<string, FragmentMaskKey, (PlanarVector3 F, PlanarVector3 M)?>? nodeReaction = null;
        try
        {
            var model = StoreyFragmentMaskService.ParentModelOf(db, schema.Id, new FemCsfeaSetup(), sections: false);
            var prepared = new Lazy<FragmentBandLoads.Prepared>(() => new FragmentBandLoads.Prepared(model.Input, model.Loads));
            // Отпор упругого основания C1 — по перемещениям снимка, как реакция основания у CSfea (−K·u).
            var foundation = FoundationSprings(model.Adapted.Model);
            var displacements = new PunchingFemCombinedResult(inner, []);
            // Сваи-точки: отпор пружины узла по перемещениям снимка; жёсткая связь реакции не даёт.
            var springByTag = foundation.GroupBy(s => s.Tag, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            if (springByTag.Count > 0)
                nodeReaction = (tag, key) =>
                {
                    if (!springByTag.TryGetValue(tag, out var s) || !displacements.TryNodeDisplacement(key, tag, out var d)) return null;
                    double[] u = [d.X, d.Y, d.Z];
                    var r = new double[3];
                    for (int a = 0; a < 3; a++)
                        for (int b = 0; b < 3; b++) r[a] -= s.K[a, b] * u[b];
                    return (new PlanarVector3(r[0], r[1], r[2]), PlanarVector3.Zero);
                };
            if (foundation.Count > 0)
            {
                var firstKey = FragmentMaskKey.OfLoadCase(known.Min());
                int missing = foundation.Count(fs => !inner.TryNodeDisplacement(firstKey, fs.Tag, out _));
                if (missing > 0)
                    diag.Add(new("punching_fem_foundation_no_displacements",
                        $"У {missing} из {foundation.Count} узлов с отпором (упругое основание C1, пружины свай) нет перемещений в снимке {program} — " +
                        "отпор этих узлов не вычитается (в запас).", false));
            }
            if (model.Adapted.Model.LoadCases.Count > 0 || foundation.Count > 0)
                baseLoads = (bounds, origin, wanted) =>
                {
                    var perCase = model.Adapted.Model.LoadCases.Count > 0
                        ? prepared.Value.Compute(bounds, origin)
                        : new Dictionary<int, (PlanarVector3 F, PlanarVector3 M)>();
                    var byKey = new Dictionary<FragmentMaskKey, (PlanarVector3 F, PlanarVector3 M)>();
                    foreach (var key in wanted)
                    {
                        var f = PlanarVector3.Zero;
                        var m = PlanarVector3.Zero;
                        foreach (var (lc, k) in key.Terms())
                            if (perCase.TryGetValue(lc, out var l)) { f += l.F * k; m += l.M * k; }
                        var (rf, rm) = FoundationReactions(foundation, displacements, key, bounds, origin);
                        byKey[key] = (f + rf, m + rm);
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
            NodeReaction = nodeReaction,
            ProgramLoadCases = byNum,
        };
    }

    /// <summary>Узел плиты на упругом основании: тег, координаты и матрица 3 × 3 Σ C1·wᵢ·n·nᵀ по смежным КЭ (Н/м).</summary>
    internal sealed record FoundationSpring(string Tag, PlanarVector3 Position, double[,] K);

    /// <summary>
    /// Узловые пружины основания C1 модели — как у CSfea (<see cref="RcStructuralMeshBuilder"/>): узлу КЭ добавляется
    /// C1·wᵢ·n·nᵀ, wᵢ — доля площади узла, n — нормаль КЭ; плюс поступательные пружины узлов модели (сваи-точки).
    /// </summary>
    internal static List<FoundationSpring> FoundationSprings(RcStructuralModel model)
    {
        var xyz = model.Nodes.ToDictionary(n => n.Id, n => new[] { n.X, n.Y, n.Z });
        var k = new Dictionary<int, double[,]>();
        foreach (var shell in model.Shells)
        {
            if (shell.FoundationC1 is not { } c1 || !(c1 > 0) || double.IsInfinity(c1)) continue;
            if (shell.NodeIds.Any(id => !xyz.ContainsKey(id))) continue;
            var p = shell.NodeIds.Select(id => xyz[id]).ToArray();
            var frame = CSfea.Core.ShellGeometry.LocalFrame(p);
            double[] n = [frame[2, 0], frame[2, 1], frame[2, 2]];
            var w = RcStructuralMeshBuilder.AreaWeights(p);
            for (int i = 0; i < p.Length; i++)
            {
                if (!k.TryGetValue(shell.NodeIds[i], out var m)) k[shell.NodeIds[i]] = m = new double[3, 3];
                for (int a = 0; a < 3; a++)
                    for (int b = 0; b < 3; b++) m[a, b] += c1 * w[i] * n[a] * n[b];
            }
        }
        // Пружины узлов (сваи-точки КЭ 56/57 ЛИРЫ, КЭ 51 SCAD) — поступательные жёсткости в общих осях (п. 2, С1).
        foreach (var sp in model.Springs)
        {
            if (sp.Dof > 2 || !xyz.ContainsKey(sp.NodeId) || !(sp.Stiffness > 0) || double.IsInfinity(sp.Stiffness)) continue;
            if (!k.TryGetValue(sp.NodeId, out var m)) k[sp.NodeId] = m = new double[3, 3];
            m[sp.Dof, sp.Dof] += sp.Stiffness;
        }
        return k.Select(kv => new FoundationSpring(kv.Key.ToString(CultureInfo.InvariantCulture),
            new PlanarVector3(xyz[kv.Key][0], xyz[kv.Key][1], xyz[kv.Key][2]), kv.Value)).ToList();
    }

    /// <summary>
    /// Отпор основания внутри границ по перемещениям результата: сила −K·u на схему и её момент относительно
    /// <paramref name="origin"/>. Узлы без перемещений пропускаются.
    /// </summary>
    internal static (PlanarVector3 F, PlanarVector3 M) FoundationReactions(IReadOnlyList<FoundationSpring> springs,
        IFragmentParentResult result, FragmentMaskKey key, FragmentBounds bounds, PlanarVector3 origin)
    {
        var f = PlanarVector3.Zero;
        var m = PlanarVector3.Zero;
        foreach (var s in springs)
        {
            if (!bounds.InBox(s.Position) || !result.TryNodeDisplacement(key, s.Tag, out var d)) continue;
            double[] u = [d.X, d.Y, d.Z];
            var r = new double[3];
            for (int a = 0; a < 3; a++)
                for (int b = 0; b < 3; b++) r[a] -= s.K[a, b] * u[b];
            var rf = new PlanarVector3(r[0], r[1], r[2]);
            f += rf;
            m += (s.Position - origin).Cross(rf);
        }
        return (f, m);
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
    /// Капитель (<paramref name="capital"/>, у колонны): основание пирамиды у колонны — от грани колонны на h0 + hc
    /// (если контур у колонны в капители, как в ядре), и своя сила за капителью — основание от грани капители на h0 плиты.
    /// </summary>
    public static PunchingFemNodeForces Evaluate(PunchingFemSchemaData data, PunchingFemSource source, PunchingFemJoint joint,
        PunchingFemForceMethod? main = null, PunchingCapital? capital = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(joint);
        var keys = source.Keys.Select(k => k.Key).ToList();

        PunchingFemJoint? capJoint = null;
        if (!joint.IsWall && capital != null && CapitalBases(joint, capital) is var (colBase, capBase))
        {
            joint = joint with { PyramidBase = colBase };
            capJoint = joint with { PyramidBase = capBase };
        }

        // Нагрузки основания: тонкая полоса у плиты (стержни колонн в неё почти не попадают).
        IReadOnlyDictionary<FragmentMaskKey, (PlanarVector3 F, PlanarVector3 M)>? baseLoads =
            source.BaseLoads?.Invoke(PunchingFemColumnAction.BaseBounds(joint, BaseLoadHalfBand), joint.Origin, keys);

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
            // Самопроверка ΔM у торца (CSfea): тело — П-контур h0/2, замкнутый поперёк стены; моменты на его гранях по
            // узловым силам против тех же моментов по пластинам в центрах КЭ.
            PunchingFemCutResult? dmCut = null;
            IReadOnlyList<PlanPoint> dmPoly = [];
            if (joint.Wall is { Kind: PunchingWallKind.End } endWall && source.Nodal != null)
            {
                dmPoly = PunchingWallGeometry.BasePolygon(endWall, joint.H0 / 2, joint.Edges);
                if (dmPoly.Count >= 3)
                {
                    dmCut = PunchingFemCut.Compute(joint with { PyramidBase = dmPoly }, data.Model.Nodes, data.CutIndex,
                        data.SlabShells[joint.SlabIndex], keys, source.Nodal);
                    if (dmCut.Failure != null) dmCut = null;
                }
            }
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
                    WallDeltaMCheck = dmCut != null && dmCut.ByKey.TryGetValue(key, out var dk)
                        && PunchingFemShellCut.WallMoment(joint, dmPoly, data.Model.Nodes, data.ElementsByTag, source.Result, key) is var (pl, sc)
                        ? (pl, -dk.Terms.Sum(t => t.M.Dot(joint.AxisX)) / 1e3, sc) : null,
                };
                if (dm == null) missing ??= "нет усилий пластин на ветвях П-контура (ΔM)";
            }
            return new PunchingFemNodeForces(byKey, cutFailure, missing);
        }
        // За капителью: нагрузки и разрез своего основания.
        IReadOnlyDictionary<FragmentMaskKey, (PlanarVector3 F, PlanarVector3 M)>? capBaseLoads = null;
        PunchingFemCutResult? capCut = null;
        if (capJoint != null)
        {
            capBaseLoads = source.BaseLoads?.Invoke(PunchingFemColumnAction.BaseBounds(capJoint, BaseLoadHalfBand), capJoint.Origin, keys);
            if (source.Nodal != null)
                capCut = PunchingFemCut.Compute(capJoint, data.Model.Nodes, data.CutIndex, data.SlabShells[capJoint.SlabIndex], keys, source.Nodal);
        }

        foreach (var key in keys)
        {
            bool shells = joint.Kind == PunchingFemJointKind.PylonShells;
            bool pilePoint = joint.IsPilePoint;
            var action = pilePoint ? PileReaction(joint, data, source, key)
                : shells
                ? source.Nodal is { } nodal ? PunchingFemColumnAction.ComputeShells(joint, data.Model.Nodes, nodal, key) : null
                : PunchingFemColumnAction.Compute(joint, data.Model.Nodes, data.ElementsByTag, source.Result, key);
            if (action is not var (af, am))
            {
                missing ??= pilePoint
                    ? "нет реакции опоры сваи-точки в результате (у импорта реакцию даёт только пружина узла, жёсткая связь — нет)"
                    : shells
                    ? source.Nodal == null
                        ? "пилон из пластин проверяется только по расчёту CSfea: силу стены на плиту дают узловые силы КЭ, а у источника их нет"
                        : "нет узловых сил пластин пилона"
                    : "нет сил концов стержней колонн в результате";
                continue;
            }
            double colAction = af.Dot(joint.Normal) / 1e3;
            var (baseLoad, contour) = Part(joint, baseLoads, cut);
            var method = main ?? (source.Nodal != null ? PunchingFemForceMethod.Contour : PunchingFemForceMethod.Column);
            if (method == PunchingFemForceMethod.Contour && contour == null) method = PunchingFemForceMethod.Column;
            double signed = method == PunchingFemForceMethod.Contour ? contour!.Value : colAction + (baseLoad ?? 0);
            // За капителью — тем же способом (нет разреза — по стержням), с той же стороны, что сила у колонны.
            double? fCap = null, capBaseLoad = null, capContour = null;
            if (capJoint != null)
            {
                (capBaseLoad, capContour) = Part(capJoint, capBaseLoads, capCut);
                double capSigned = method == PunchingFemForceMethod.Contour && capContour is double cc ? cc : colAction + (capBaseLoad ?? 0);
                fCap = Math.Max(0, signed >= 0 ? capSigned : -capSigned);
            }
            byKey[key] = new PunchingFemForces(Math.Abs(signed), signed >= 0 ? PunchingLoadSide.FromBelow : PunchingLoadSide.FromAbove,
                am.Dot(joint.AxisX) / 1e3, am.Dot(joint.AxisY) / 1e3, method, colAction, baseLoad, contour)
            {
                PylonParts = cut?.Failure == null && cut?.ByKey.TryGetValue(key, out var pc) == true
                    ? PunchingFemColumnAction.PylonParts(joint, pc.Terms) : null,
                FCapital = fCap, BaseLoadCapital = capBaseLoad, ContourForceCapital = capContour,
            };

            // Нагрузка основания и сила по разрезу для основания узла j.
            (double? BaseLoad, double? Contour) Part(PunchingFemJoint j,
                IReadOnlyDictionary<FragmentMaskKey, (PlanarVector3 F, PlanarVector3 M)>? loads, PunchingFemCutResult? c)
            {
                double? bload = loads != null && loads.TryGetValue(key, out var bl) ? bl.F.Dot(j.Normal) / 1e3 : null;
                // Свая-точка: её реакция — «действие сваи»; из нагрузки основания (там все реакции в основании) она исключается.
                if (pilePoint && bload is double b1) bload = b1 - colAction;
                // Стержни с другой стороны плиты в основании (сваи, колонна над сваей) — их действие на плиту входит в
                // нагрузку основания (п. 2, С1); нет сил концов — нагрузка основания неизвестна (сила без вычета, в запас).
                if (j.BaseBars.Count > 0 && bload is double b0)
                    bload = PunchingFemColumnAction.Compute(j with { Bars = j.BaseBars }, data.Model.Nodes, data.ElementsByTag, source.Result, key)
                        is var (pf, _) ? b0 + pf.Dot(j.Normal) / 1e3 : null;
                double? cforce = c?.Failure == null && c?.ByKey.TryGetValue(key, out var cf) == true ? cf.Force
                    : c == null && source.ShellCut ? PunchingFemShellCut.Compute(j, data.Model.Nodes, data.ElementsByTag, source.Result, key)
                    : null;
                return (bload, cforce);
            }
        }
        return new PunchingFemNodeForces(byKey, cutFailure, missing);
    }

    /// <summary>
    /// Основания пирамид узла с капителью в осях узла: у колонны — от грани колонны на h0 + hc (если контур у колонны в
    /// капители, иначе на h0), за капителью — от грани капители на h0 плиты; края плиты отсекают. null — капитель не
    /// годится (не содержит колонну и т. п.) или основание вне плиты: тогда сила считается как без капители.
    /// </summary>
    public static (List<PlanPoint> Column, List<PlanPoint> Capital)? CapitalBases(PunchingFemJoint joint, PunchingCapital capital)
    {
        ArgumentNullException.ThrowIfNull(joint);
        ArgumentNullException.ThrowIfNull(capital);
        if (joint.Area is not { } area || joint.IsWall || !(joint.H0 > 0)) return null;
        var probe = new PunchingInput(area, joint.H0, 1, new PunchingForces(0, 0, 0), Edges: joint.Edges, H: joint.H, Capital: capital);
        try { PunchingCapitalCheck.Validate(probe); }
        catch (ArgumentException) { return null; }
        var col = PunchingContourBuilder.PyramidBasePolygon(area, PunchingCapitalCheck.ColumnH0(probe), joint.Edges);
        var cap = PunchingContourBuilder.PyramidBasePolygon(PunchingCapitalCheck.CapitalArea(probe), joint.H0, joint.Edges);
        return col.Count >= 3 && cap.Count >= 3 ? (col, cap) : null;
    }

    /// <summary>Капитель узла для расчёта: переопределение пользователя или распознанная по схеме; null — нет или узел стены.</summary>
    public static PunchingCapital? EffectiveCapital(PunchingFemJoint joint, PunchingFemJointOverride? o)
        => joint.IsWall || joint.Area == null ? null : CapitalParams(joint, o).ToCore(AreaParams(joint.Area).Shape);

    /// <summary>Реакция опоры сваи-точки на плиту в общих осях, Н и Н·м относительно начала осей узла; null — нет.</summary>
    static (PlanarVector3 F, PlanarVector3 M)? PileReaction(PunchingFemJoint joint, PunchingFemSchemaData data, PunchingFemSource source, FragmentMaskKey key)
    {
        if (source.NodeReaction?.Invoke(joint.NodeTag, key) is not var (f, m)) return null;
        var p = data.Model.Nodes.TryGetValue(joint.NodeTag, out var n) ? new PlanarVector3(n.X, n.Y, n.Z) : joint.Origin;
        return (f, m + (p - joint.Origin).Cross(f));
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

    /// <summary>Капитель узла: переопределение пользователя или распознанная по схеме.</summary>
    public static PunchingCapitalParams CapitalParams(PunchingFemJoint joint, PunchingFemJointOverride? o) => o?.Capital switch
    {
        "none" => new PunchingCapitalParams(),
        "manual" when o.CapitalManual is { } m => m with { Enabled = true },
        _ => FromCore(joint.Capital),
    };

    /// <summary>Капитель ядра → параметры задачи; null — выключена.</summary>
    public static PunchingCapitalParams FromCore(PunchingCapital? c) => c switch
    {
        { Area: PunchingLoadArea.Rectangle r } => new PunchingCapitalParams { Enabled = true, Bx = r.Bx, By = r.By, Hc = c.Hc, Dx = c.Offset.X, Dy = c.Offset.Y },
        { Area: PunchingLoadArea.Circle k } => new PunchingCapitalParams { Enabled = true, D = k.D, Hc = c.Hc },
        _ => new PunchingCapitalParams(),
    };

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
            Capital = wall.IsWall ? new PunchingCapitalParams() : CapitalParams(joint, o),
            Holes = joint.Holes.Select(h => new PunchingHoleParams { Shape = "polygon", Points = h.Polygon.Select(p => new[] { p.X, p.Y }).ToList() }).ToList(),
            Reinforcement = o?.Reinforcement ?? check.Reinforcement,
            Forces = new PunchingForceParams
            {
                // До 1 Н и 1 Н·м: в окне задачи по строке — читаемые числа; проверка считает по тем же значениям.
                F = Math.Round(forces.F, 3), Mx = Math.Round(forces.Mx, 3), My = Math.Round(forces.My, 3),
                FCapital = forces.FCapital is double fc && !wall.IsWall && CapitalParams(joint, o).Enabled ? Math.Round(fc, 3) : null,
                MomentsAreMloc = check.MomentsAreMloc,
                LoadSide = forces.Side == PunchingLoadSide.FromAbove ? "from_above" : "from_below",
            },
            Options = check.Options,
            Source = new PunchingSourceParams
            {
                Schema = schemaTag, Node = joint.NodeTag, Key = keyLabel,
                Method = forces.Method == PunchingFemForceMethod.Contour ? "contour" : "column",
                ColumnAction = forces.ColumnAction, BaseLoad = forces.BaseLoad, ContourForce = forces.ContourForce,
                BaseLoadCapital = forces.BaseLoadCapital, ContourForceCapital = forces.ContourForceCapital,
            },
        };
    }
}
