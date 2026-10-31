using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using CSfea.CScoreBridge.Structural;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Состояние масок фрагмента относительно их источника.</summary>
public enum FragmentMaskStatus
{
    /// <summary>Маски не снимались.</summary>
    None,
    /// <summary>Результат источника и полоса родителя не менялись.</summary>
    Current,
    /// <summary>Источник пересчитан или полоса родителя изменена после снятия.</summary>
    Stale,
    /// <summary>Постановка, её результат или схема-родитель удалены.</summary>
    SourceDeleted,
}

/// <summary>Итог снятия масок: записанный набор (null — отказ, БД не менялась) и отчёт.</summary>
public sealed record FragmentMaskExtractResult(FragmentMaskSet? Set, IReadOnlyList<FemValidationDiagnostic> Diagnostics)
{
    public bool HasErrors => Set == null;
}

/// <summary>
/// Маски фрагмента этажа из результата родителя (спека 2026-10-28, разд. 4, 6): постановка CSfea родителя → ключи
/// (<see cref="FragmentMaskSourceKeys"/>) → поля шагов → <see cref="CsfeaFieldsParentResult"/> → снятие по разрезам
/// (<see cref="FragmentMaskExtractor"/>) → равновесие с нагрузками полосы (<see cref="FragmentBandLoads"/>,
/// <see cref="FragmentMaskBalance"/>) → запись набора (БД v94) с отчётом и отпечатком источника. Источники ЛИРЫ/SCAD —
/// снимок результатов программы во вложении родителя (<see cref="ExtractImported"/>, срез 7).
/// Всё читается из БД здесь — вызывать в потоке соединения.
/// </summary>
public static class StoreyFragmentMaskService
{
    /// <summary>
    /// Снять маски фрагмента <paramref name="fragment"/> из источника <paramref name="sourceKind"/>
    /// (<see cref="FragmentMaskSet.SourceCsfeaLinear"/> или <see cref="FragmentMaskSet.SourceCsfeaSecant"/>; ссылка —
    /// Id постановки родителя). Отказ (ошибки отчёта) БД не меняет; иначе прежний набор заменяется.
    /// </summary>
    public static FragmentMaskExtractResult Extract(DatabaseService db, FemSchema fragment, string sourceKind, string sourceRef)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(fragment);
        var diag = new List<FemValidationDiagnostic>();
        FragmentMaskExtractResult Fail(string code, string message)
        {
            diag.Add(new(code, message, true));
            return new(null, diag);
        }

        if (fragment.ParentSchemaId is not int parentId || fragment.FragmentZ1 is not double z1 || fragment.FragmentZ2 is not double z2)
            return Fail("fragment_mask_not_fragment", $"Схема «{fragment.Tag}» — не фрагмент этажа: маски снимаются только во фрагменте.");
        if (db.GetFemSchemaSourceType(parentId) is null)
            return Fail("fragment_mask_parent_deleted", "Схема-родитель фрагмента удалена — снять маски не из чего.");
        if (sourceKind is FragmentMaskSet.SourceScad or FragmentMaskSet.SourceLira)
            return ExtractImported(db, fragment, parentId, z1, z2, sourceKind, diag);
        bool linear = sourceKind == FragmentMaskSet.SourceCsfeaLinear;
        if (!linear && sourceKind != FragmentMaskSet.SourceCsfeaSecant)
            return Fail("fragment_mask_source_unsupported", $"Источник масок «{sourceKind}» не поддерживается.");
        if (!int.TryParse(sourceRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out int analysisId)
            || db.GetFemAnalysis(analysisId) is not { } analysis || analysis.SchemaId != parentId)
            return Fail("fragment_mask_source_missing", "Постановка-источник масок не найдена у схемы-родителя.");
        string expectedKind = linear ? FemCsfeaRunner.LinearAnalysisKind : FemCsfeaRunner.AnalysisKind;
        if (analysis.Kind != expectedKind)
            return Fail("fragment_mask_source_kind", $"Постановка «{analysis.Tag}» — не {(linear ? "линейный" : "секущий")} расчёт CSfea.");
        if (analysis.ResultId is not int resultId || db.GetCalcResultById(resultId) is not { } result
            || FemCsfeaResultSummary.Parse(result.DataJson) is not { } summary)
            return Fail("fragment_mask_no_result", $"У постановки «{analysis.Tag}» нет сохранённого результата — посчитайте родителя.");

        var parentLoadCases = db.GetFemLoadCases(parentId);
        var fragmentLoadCases = db.GetFemLoadCases(fragment.Id);
        var warnings = new List<string>();
        var map = FragmentMaskSourceKeys.LoadCaseMap(parentLoadCases, fragmentLoadCases, warnings);
        var stages = FemAnalysisParams.Parse(analysis.ParamsJson).ResolveStages(analysis);
        var steps = new List<(FragmentMaskKey, RcSecantStepFields)>();
        foreach (var step in FragmentMaskSourceKeys.Select(summary, stages, parentLoadCases, map, warnings))
        {
            RcSecantStepFields? fields = null;
            if (db.GetFemResultStep(resultId, step.StepN) is { } data)
                try { fields = FemCsfeaStepFieldsCodec.Unpack(data); }
                catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { }
            if (fields == null) warnings.Add($"Стадия «{step.Label}»: поля шага не читаются — пропущена.");
            else steps.Add((step.Key, fields));
        }
        foreach (var w in warnings) diag.Add(new("fragment_mask_source", w, false));
        if (steps.Count == 0)
            return Fail("fragment_mask_no_keys", $"В результате «{analysis.Tag}» нет загружений или стадий, по которым снимаются маски.");
        var cuts = db.GetFragmentCutsWithIds(fragment.Id);
        if (cuts.Count == 0)
            return Fail("fragment_mask_no_cuts", "У фрагмента нет сечений рассечения.");

        var band = Band(db, parentId, z1, z2, out var nodes);
        var parentMembers = db.GetFemMembers(parentId);
        var regionAxes = FemCsfeaInputBuilder.RegionAxes(parentMembers, db.GetPlanarRegions(parentId));
        var parentResult = new CsfeaFieldsParentResult(steps, nodes, band, parentMembers, regionAxes);

        var cutInputs = CutInputs(db, fragment.Id, cuts.Select(c => c.Cut).ToList());
        var extraction = FragmentMaskExtractor.Extract(new FragmentMaskInput
        {
            Cuts = cutInputs, ParentNodes = nodes, ParentElements = CsfeaFieldsParentResult.WithBeamRotation(band, parentMembers),
            Result = parentResult, ToleranceM = Tol,
        });
        diag.AddRange(extraction.Diagnostics);
        if (extraction.Masks.Count == 0)
            return Fail("fragment_mask_empty", "Ни по одному сечению маска не снята (см. предупреждения).");

        // Равновесие масок с нагрузками и реакциями полосы родителя.
        var origin = BandOrigin(band, nodes);
        var fieldsByKey = steps.ToDictionary(s => s.Item1, s => s.Item2);
        var setup = FemCsfeaSetup.FromAnalysis(analysis, parentLoadCases);
        var bounds = Bounds(fragment, z1, z2);
        var loads = BandActions(db, parentId, setup, fieldsByKey.Keys, map, bounds, origin, diag,
            key => Reactions(fieldsByKey[key], nodes, bounds, origin));
        if (loads != null) diag.AddRange(FragmentMaskBalance.Check(cutInputs, extraction.Masks, loads, origin));

        diag.Add(new("fragment_mask_summary", string.Create(CultureInfo.CurrentCulture,
            $"Маски из «{analysis.Tag}»: сечений {extraction.Masks.Select(m => m.CutIndex).Distinct().Count()} из {cuts.Count}, " +
            $"загружений или сочетаний {steps.Count}, масок {extraction.Masks.Count}."), false));
        var set = new FragmentMaskSet
        {
            SourceKind = sourceKind, SourceRef = analysisId.ToString(CultureInfo.InvariantCulture), SourceLabel = analysis.Tag,
            SourceFingerprint = Fingerprint(db, fragment, analysis, result),
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ReportJson = StoreyFragmentBuildReport.ToJson(diag),
            Items = extraction.Masks.Select(m => new FragmentMaskItem(cuts[m.CutIndex].Id, m.Key, m.Samples)).ToList(),
        };
        db.ReplaceFragmentMasks(fragment.Id, set);
        return new(set, diag);
    }

    /// <summary>
    /// Сверка масок с источником (для шапки фрагмента): отпечаток пересчитывается и сравнивается со снятым. Ничего не
    /// меняет.
    /// </summary>
    public static FragmentMaskStatus CheckMasks(DatabaseService db, FemSchema fragment)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(fragment);
        if (db.GetFragmentMasks(fragment.Id, items: false) is not { } set) return FragmentMaskStatus.None;
        if (fragment.ParentSchemaId is not int parentId || db.GetFemSchemaSourceType(parentId) is null)
            return FragmentMaskStatus.SourceDeleted;
        if (set.SourceKind is FragmentMaskSet.SourceScad or FragmentMaskSet.SourceLira)
            return db.GetFemSchemaSourceFile(parentId, SnapshotFileKind(set.SourceKind)) is not { } file
                ? FragmentMaskStatus.SourceDeleted
                : ImportedFingerprint(db, fragment, file.Data) == set.SourceFingerprint ? FragmentMaskStatus.Current : FragmentMaskStatus.Stale;
        if (set.SourceKind is not (FragmentMaskSet.SourceCsfeaLinear or FragmentMaskSet.SourceCsfeaSecant))
            return FragmentMaskStatus.Stale;
        if (!int.TryParse(set.SourceRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
            || db.GetFemAnalysis(id) is not { } analysis || analysis.SchemaId != parentId
            || analysis.ResultId is not int resultId || db.GetCalcResultById(resultId) is not { } result)
            return FragmentMaskStatus.SourceDeleted;
        return Fingerprint(db, fragment, analysis, result) == set.SourceFingerprint ? FragmentMaskStatus.Current : FragmentMaskStatus.Stale;
    }

    /// <summary>Отпечаток источника: постановка, Id и дата её результата, отпечаток полосы родителя (как у <see cref="StoreyFragmentService.CheckParent"/>).</summary>
    static string Fingerprint(DatabaseService db, FemSchema fragment, FemAnalysis analysis, CScore.CalcResult result)
    {
        var snapshot = db.ReadStoreyFragmentSnapshot(fragment.ParentSchemaId!.Value,
            new StoreyFragmentRequest(fragment.FragmentZ1!.Value, fragment.FragmentZ2!.Value, fragment.Tag));
        return string.Create(CultureInfo.InvariantCulture,
            $"{analysis.Id}|{result.Id}|{result.Created}|{StoreyFragmentFingerprint.Compute(snapshot)}");
    }

    /// <summary>
    /// Сечения на входе экстрактора: у конца стержня — точка узла схемы фрагмента <see cref="FragmentCut.NodeTag"/>, у
    /// кромки — нормаль области КонЭ фрагмента (нет области — у горизонтального разреза горизонталь ⟂ отрезку, стена
    /// вертикальна).
    /// </summary>
    internal static List<FragmentMaskCutInput> CutInputs(DatabaseService db, int fragmentId, IReadOnlyList<FragmentCut> cuts)
    {
        var nodes = new Dictionary<string, FemNode>(StringComparer.Ordinal);
        foreach (var n in db.GetFemNodes(fragmentId)) nodes.TryAdd(n.NodeTag, n);
        var members = new Dictionary<string, FemMember>(StringComparer.Ordinal);
        foreach (var m in db.GetFemMembers(fragmentId)) members.TryAdd(m.ElemTag, m);
        var regions = db.GetPlanarRegions(fragmentId).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        return cuts.Select(c =>
        {
            if (c.Kind == FragmentCutKind.End)
                return new FragmentMaskCutInput(c, c.NodeTag is { } t && nodes.TryGetValue(t, out var n) ? new PlanarVector3(n.X, n.Y, n.Z) : null, null);
            PlanarVector3? normal = members.TryGetValue(c.MemberTag, out var m) && m.PlanarRegionId is int rid
                && regions.TryGetValue(rid, out var r) ? r.Frame.LocalZ : null;
            if (normal == null && c.Side != FragmentCutSide.Lateral && c.Segments.Count > 0)
            {
                var d = c.Segments[0].Q - c.Segments[0].P;
                var h = d.Cross(new PlanarVector3(0, 0, 1));
                if (h.Length > 1e-12) normal = h.Normalize();
            }
            return new FragmentMaskCutInput(c, null, normal);
        }).ToList();
    }

    /// <summary>
    /// Внешние воздействия на полосу родителя по ключам масок (кроме самих масок): нагрузки полосы
    /// (<see cref="FragmentBandLoads"/>) по слагаемым ключа и реакции опор полосы из полей ключа. null — вход родителя не
    /// собрался (сверка пропущена, предупреждение).
    /// </summary>
    static Dictionary<FragmentMaskKey, (PlanarVector3 F, PlanarVector3 M)>? BandActions(DatabaseService db, int parentId,
        FemCsfeaSetup setup, IEnumerable<FragmentMaskKey> keys, IReadOnlyDictionary<int, int> map, FragmentBounds bounds,
        PlanarVector3 origin, List<FemValidationDiagnostic> diag, Func<FragmentMaskKey, (PlanarVector3 F, PlanarVector3 M)>? reactions)
    {
        var input = FemCsfeaInputBuilder.Build(db, parentId, setup with { Stages = [] }, sections: false);
        var adapted = FemRcModelAdapter.Adapt(new FemRcModelInput
        {
            MeshNodes = input.MeshNodes, MeshElements = input.MeshElements, Nodes = input.Nodes, Members = input.Members,
            Groups = input.Groups, Supports = input.Supports, Springs = input.Springs, RigidBodies = input.RigidBodies,
            LoadCases = input.LoadCases, NodeLoads = input.NodeLoads, MemberLoads = input.MemberLoads,
            ElementLoads = input.ElementLoads, MeshNodeLoads = input.MeshNodeLoads, Properties = input.Properties,
            RegionAxisX = input.RegionAxisX, Stages = [], AllLoadCases = true,
        });
        if (adapted.Model.LoadCases.Count == 0)
        {
            diag.Add(new("fragment_mask_balance_skipped", "Нагрузки родителя не собраны — равновесие масок не проверено.", false));
            return null;
        }
        var perCase = FragmentBandLoads.Compute(input, adapted, bounds, origin);
        var parentOf = map.ToDictionary(kv => kv.Value, kv => kv.Key);
        var result = new Dictionary<FragmentMaskKey, (PlanarVector3, PlanarVector3)>();
        foreach (var key in keys)
        {
            var f = PlanarVector3.Zero;
            var m = PlanarVector3.Zero;
            foreach (var (lc, k) in key.Terms())
                if (parentOf.TryGetValue(lc, out int p) && perCase.TryGetValue(p, out var l))
                {
                    f += l.F * k;
                    m += l.M * k;
                }
            if (reactions != null)
            {
                var (rf, rm) = reactions(key);
                f += rf;
                m += rm;
            }
            result[key] = (f, m);
        }
        return result;
    }

    /// <summary>Реакции опор внутри фрагмента из полей шага CSfea: сила и момент относительно <paramref name="origin"/>.</summary>
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

    /// <summary>Допуск по отметкам полосы, м.</summary>
    const double Tol = 1e-3;

    /// <summary>Границы фрагмента: полоса z₁…z₂ и призма схемы (<see cref="FemSchema.FragmentPrismJson"/>), если она есть.</summary>
    static FragmentBounds Bounds(FemSchema fragment, double z1, double z2) =>
        new(z1, z2, FragmentPrismCodec.Decode(fragment.FragmentPrismJson)?.Prism, Tol);

    /// <summary>
    /// Сетка родителя у полосы: КЭ, касающиеся замкнутой полосы [z1, z2] (КЭ отброшенной стороны у узла на разрезе —
    /// тоже); <paramref name="nodes"/> — все узлы сетки родителя по тегу.
    /// </summary>
    static List<FemElement> Band(DatabaseService db, int parentId, double z1, double z2, out Dictionary<string, FemMeshNode> nodes)
    {
        var byTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in db.GetFemMeshNodes(parentId)) byTag.TryAdd(n.NodeTag, n);
        nodes = byTag;
        return db.GetFemMeshElements(parentId).Where(e =>
        {
            var g = CScore.Fem.Loads.FemElementGeometry.Of(e, byTag);
            return g != null && g.Points.Max(p => p.Z) >= z1 - Tol && g.Points.Min(p => p.Z) <= z2 + Tol;
        }).ToList();
    }

    /// <summary>Точка приведения моментов сверки — среднее узлов КЭ полосы.</summary>
    static PlanarVector3 BandOrigin(IReadOnlyList<FemElement> band, IReadOnlyDictionary<string, FemMeshNode> nodes)
    {
        var points = band.SelectMany(e => CScore.Fem.Loads.FemElementGeometry.Of(e, nodes)!.Points).ToList();
        return points.Count == 0 ? PlanarVector3.Zero : points.Aggregate(PlanarVector3.Zero, (a, p) => a + p) * (1.0 / points.Count);
    }

    /// <summary>Вид вложения снимка результатов программы для источника масок.</summary>
    public static string SnapshotFileKind(string sourceKind) => sourceKind == FragmentMaskSet.SourceLira
        ? FemSchemaSourceFileKind.LiraMaskResults : FemSchemaSourceFileKind.ScadMaskResults;

    /// <summary>Название программы источника для отчёта и подписи.</summary>
    static string ProgramName(string sourceKind) => sourceKind == FragmentMaskSet.SourceLira ? "ЛИРА" : "SCAD";

    /// <summary>
    /// Маски из снимка результатов программы (SCAD, ЛИРА; спека разд. 4.1, 4.5; план среза 7, задача 5): вложение схемы-
    /// родителя → <see cref="ImportedParentResult"/> → снятие и сверка, как у CSfea (реакций в снимке нет). Есть перемещения,
    /// но нет усилий КЭ полосы — маски кинематические с предупреждением.
    /// </summary>
    static FragmentMaskExtractResult ExtractImported(DatabaseService db, FemSchema fragment, int parentId, double z1, double z2,
        string sourceKind, List<FemValidationDiagnostic> diag)
    {
        FragmentMaskExtractResult Fail(string code, string message)
        {
            diag.Add(new(code, message, true));
            return new(null, diag);
        }
        string program = ProgramName(sourceKind);
        string fileKind = SnapshotFileKind(sourceKind);
        if (db.GetFemSchemaSourceFile(parentId, fileKind) is not { } file)
            return Fail("fragment_mask_no_snapshot", $"У схемы-родителя нет снимка результатов {program}: меню схемы-родителя " +
                $"«{program} → Результаты для фрагментов…».");
        ImportedResultSnapshot snapshot;
        try { snapshot = ImportedResultSnapshotCodec.Decode(file.Data); }
        catch (InvalidDataException ex) { return Fail("fragment_mask_snapshot_broken", $"Снимок результатов {program} не читается: {ex.Message}"); }

        var parentLoadCases = db.GetFemLoadCases(parentId);
        var warnings = new List<string>();
        var map = FragmentMaskSourceKeys.LoadCaseMap(parentLoadCases, db.GetFemLoadCases(fragment.Id), warnings);
        var byNum = FragmentMaskSourceKeys.ImportedLoadCaseMap(snapshot.Loads, parentLoadCases,
            FemLoadOrigin.Import(sourceKind), map, warnings);
        foreach (var w in warnings) diag.Add(new("fragment_mask_source", w, false));
        if (byNum.Count == 0)
            return Fail("fragment_mask_no_keys", $"Ни одно загружение снимка {program} не сопоставлено загружениям фрагмента.");
        var cuts = db.GetFragmentCutsWithIds(fragment.Id);
        if (cuts.Count == 0)
            return Fail("fragment_mask_no_cuts", "У фрагмента нет сечений рассечения.");

        var band = Band(db, parentId, z1, z2, out var nodes);
        var parentResult = new ImportedParentResult(snapshot, byNum, nodes, band);
        if (parentResult.BarsWithoutAxes.Count > 0)
            diag.Add(new("fragment_mask_bar_axes", $"Стержней полосы без местных осей программы: {parentResult.BarsWithoutAxes.Count} " +
                "— их усилия не взяты (обновите схему из программы).", false, parentResult.BarsWithoutAxes));
        if (parentResult.ShellsWithoutAxes.Count > 0)
            diag.Add(new("fragment_mask_shell_axes", $"Пластин полосы без осей выдачи усилий: {parentResult.ShellsWithoutAxes.Count} " +
                "— их усилия не взяты (обновите схему из программы).", false, parentResult.ShellsWithoutAxes));
        var bandTags = band.Select(e => int.TryParse(e.ElemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0)
            .ToHashSet();
        bool hasForces = snapshot.Loads.Any(l => l.BarNums.Any(bandTags.Contains) || l.ShellNums.Any(bandTags.Contains));
        if (!hasForces)
            diag.Add(new("fragment_mask_kinematic_only", $"В снимке {program} нет усилий КЭ полосы — маски только кинематические " +
                "(силовые режимы ГУ разрезов работать не будут).", false));
        if (parentResult.ShearConsistency() is var (ry, rz, n) && n > 0)
            foreach (var (plane, r) in new[] { ("XY", ry), ("XZ", rz) })
                if (r is double v && Math.Abs(v - 1) > 0.2)
                    diag.Add(new("fragment_mask_shear_sign", string.Create(CultureInfo.CurrentCulture,
                        $"Поперечные силы стержней {program} в плоскости {plane} не согласуются с градиентом моментов " +
                        $"(множитель {v:0.00} вместо 1) — силы масок концов стержней могут быть с неверным знаком."), false));

        var cutInputs = CutInputs(db, fragment.Id, cuts.Select(c => c.Cut).ToList());
        var extraction = FragmentMaskExtractor.Extract(new FragmentMaskInput
        {
            Cuts = cutInputs, ParentNodes = nodes, ParentElements = band, Result = parentResult, ToleranceM = Tol,
        });
        diag.AddRange(extraction.Diagnostics);
        if (extraction.Masks.Count == 0)
            return Fail("fragment_mask_empty", "Ни по одному сечению маска не снята (см. предупреждения).");

        if (hasForces)
        {
            var origin = BandOrigin(band, nodes);
            diag.Add(new("fragment_mask_no_reactions", $"В снимке {program} нет реакций опор: если опоры внутри полосы, сверка " +
                "равновесия покажет расхождение.", false));
            try
            {
                var loads = BandActions(db, parentId, new FemCsfeaSetup(), parentResult.Keys, map, Bounds(fragment, z1, z2), origin, diag, null);
                if (loads != null) diag.AddRange(FragmentMaskBalance.Check(cutInputs, extraction.Masks, loads, origin));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                diag.Add(new("fragment_mask_balance_skipped", $"Нагрузки родителя не собраны ({ex.Message}) — равновесие масок не проверено.", false));
            }
        }

        string label = $"{program} — снимок {snapshot.TakenAt.Replace('T', ' ')}";
        diag.Add(new("fragment_mask_summary", string.Create(CultureInfo.CurrentCulture,
            $"Маски из «{label}»: сечений {extraction.Masks.Select(m => m.CutIndex).Distinct().Count()} из {cuts.Count}, " +
            $"загружений {parentResult.Keys.Count}, масок {extraction.Masks.Count}."), false));
        var set = new FragmentMaskSet
        {
            SourceKind = sourceKind, SourceRef = fileKind, SourceLabel = label,
            SourceFingerprint = ImportedFingerprint(db, fragment, file.Data),
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ReportJson = StoreyFragmentBuildReport.ToJson(diag),
            Items = extraction.Masks.Select(m => new FragmentMaskItem(cuts[m.CutIndex].Id, m.Key, m.Samples)).ToList(),
        };
        db.ReplaceFragmentMasks(fragment.Id, set);
        return new(set, diag);
    }

    /// <summary>Отпечаток снимка программы: SHA-256 вложения и отпечаток полосы родителя.</summary>
    static string ImportedFingerprint(DatabaseService db, FemSchema fragment, byte[] data)
    {
        var snapshot = db.ReadStoreyFragmentSnapshot(fragment.ParentSchemaId!.Value,
            new StoreyFragmentRequest(fragment.FragmentZ1!.Value, fragment.FragmentZ2!.Value, fragment.Tag));
        return $"{ImportedResultSnapshotCodec.Fingerprint(data)}|{StoreyFragmentFingerprint.Compute(snapshot)}";
    }
}
