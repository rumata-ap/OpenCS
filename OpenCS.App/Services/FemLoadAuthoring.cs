using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Editing;
using CScore.Fem.Imperfections;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Что затронет удаление загружения (для вопроса и запрета).</summary>
/// <param name="Analyses">Постановки, в формуле, стадиях или пакете которых загружение задано явно: пока они есть, удалить нельзя.</param>
/// <param name="Combinations">Комбинации, из которых загружение будет вычеркнуто.</param>
/// <param name="Emptied">Комбинации, которые после этого опустеют и будут удалены.</param>
/// <param name="Loads">Нагрузки загружения (всех видов).</param>
/// <param name="Imperfections">Сохранённые элементы несовершенства загружения.</param>
public sealed record FemLoadCaseDeleteImpact(IReadOnlyList<string> Analyses, IReadOnlyList<string> Combinations,
    IReadOnlyList<string> Emptied, int Loads, int Imperfections)
{
    /// <summary>Удаление запрещено.</summary>
    public bool Blocked => Analyses.Count > 0;
}

/// <summary>
/// Загружения, нагрузки, комбинации и несовершенства МКЭ-схемы: состав удаляемого и запрет, команда удаления, проверки
/// нагрузок сверх <see cref="FemCanonicalValidator"/> и запись несовершенств — одно для страницы схемы, дерева и вида
/// <c>femSchemas</c> документа <c>opencs-model</c>.
/// </summary>
public static class FemLoadAuthoring
{
    // ── Удаление загружения ──────────────────────────────────────────────────────────────────

    /// <summary>Что затронет удаление <paramref name="loadCase"/> из сессии.</summary>
    public static FemLoadCaseDeleteImpact DeleteImpact(DatabaseService db, FemSchemaEditSession session, FemLoadCase loadCase)
    {
        int id = loadCase.Id;
        bool saved = id > 0 && session.Schema.Id > 0;
        var analyses = saved
            ? db.GetFemAnalyses(session.Schema.Id).Where(a => FemAnalysisParams.ReferencedLoadCases(a).Contains(id))
                .Select(a => a.Tag).ToList()
            : [];
        var combinations = new List<string>();
        var emptied = new List<string>();
        foreach (var d in session.LoadDefinitions)
        {
            var e = d.GetExpression();
            if (!e.LoadCaseIds.Contains(id) && e.Terms.All(t => t.LoadCaseId != id)) continue;
            combinations.Add(d.Tag);
            if (FemLoadDefinitionValidator.IsEmpty(Without(e, id))) emptied.Add(d.Tag);
        }
        int loads = session.NodeLoads.Count(l => l.LoadCaseId == id) + session.MemberLoads.Count(l => l.LoadCaseId == id)
            + session.KinematicLoads.Count(l => l.LoadCaseId == id) + session.ElementLoads.Count(l => l.LoadCaseId == id)
            + session.MeshNodeLoads.Count(l => l.LoadCaseId == id);
        int imperfections = saved ? db.GetFemImperfections(session.Schema.Id).Count(i => i.LoadCaseId == id) : 0;
        return new FemLoadCaseDeleteImpact(analyses, combinations, emptied, loads, imperfections);
    }

    /// <summary>Текст отказа: загружение задано в постановках.</summary>
    public static string BlockedText(FemLoadCase loadCase, FemLoadCaseDeleteImpact impact) =>
        string.Format(Loc.S("FemLoadCaseDeleteBlocked"), loadCase.Tag, NameList(impact.Analyses));

    /// <summary>Нужен ли вопрос: есть что терять кроме самого пустого загружения.</summary>
    public static bool NeedsQuestion(FemLoadCaseDeleteImpact impact) =>
        impact.Loads + impact.Imperfections + impact.Combinations.Count > 0;

    /// <summary>Вопрос об удалении: нагрузки, комбинации (вычёркивается, опустевшие удаляются), несовершенства.</summary>
    public static string DeleteQuestion(FemLoadCase loadCase, FemLoadCaseDeleteImpact impact)
    {
        var lines = new List<string>();
        if (impact.Loads > 0) lines.Add(string.Format(Loc.S("FemLoadCaseDeleteLoads"), impact.Loads));
        if (impact.Imperfections > 0) lines.Add(string.Format(Loc.S("FemLoadCaseDeleteImperfections"), impact.Imperfections));
        var kept = impact.Combinations.Except(impact.Emptied).ToList();
        if (kept.Count > 0) lines.Add(string.Format(Loc.S("FemLoadCaseDeleteFromCombinations"), NameList(kept)));
        if (impact.Emptied.Count > 0) lines.Add(string.Format(Loc.S("FemLoadCaseDeleteEmptiedCombinations"), NameList(impact.Emptied)));
        string text = string.Format(Loc.S("FemLoadCaseDeleteConfirm"), loadCase.Tag);
        if (lines.Count > 0) text += "\n\n" + string.Join("\n", lines.Select(l => "• " + l));
        return text + "\n\n" + Loc.S("FemLoadCaseDeleteUndoHint");
    }

    /// <summary>
    /// Одна отменяемая команда: загружение с нагрузками, вычёркивание из комбинаций, удаление опустевших комбинаций.
    /// Элементы несовершенства удаляются при записи схемы (их загружения больше нет).
    /// </summary>
    public static IFemEditCommand DeleteCommand(FemSchemaEditSession session, FemLoadCase loadCase)
    {
        int id = loadCase.Id;
        var commands = new List<IFemEditCommand>();
        foreach (var d in session.LoadDefinitions.ToList())
        {
            var e = d.GetExpression();
            if (!e.LoadCaseIds.Contains(id) && e.Terms.All(t => t.LoadCaseId != id)) continue;
            var rest = Without(e, id);
            if (FemLoadDefinitionValidator.IsEmpty(rest)) { commands.Add(new DeleteLoadDefinitionCommand(d)); continue; }
            commands.Add(new EditLoadDefinitionCommand(d, new FemLoadDefinition
            {
                Tag = d.Tag, Description = d.Description, SourceKind = d.SourceKind, CombinationType = d.CombinationType,
                ExpressionJson = rest.ToJson(),
            }));
        }
        commands.Add(new DeleteLoadCaseCommand(loadCase));
        return commands.Count == 1 ? commands[0] : new CompositeFemCommand(commands);
    }

    static FemLoadExpression Without(FemLoadExpression e, int id) => new()
    {
        Mode = e.Mode,
        LoadCaseIds = e.LoadCaseIds.Where(x => x != id).ToList(),
        Terms = e.Terms.Where(t => t.LoadCaseId != id).ToList(),
        CombinationType = e.CombinationType,
    };

    // ── Проверки ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Проверки нагрузок сверх канона (<see cref="FemCanonicalValidator"/>): нагрузки на КЭ, заданные вручную, ссылаются
    /// на существующие КонЭ и группы, их значения — конечные числа. <c>SourceKeys</c> — теги КонЭ.
    /// </summary>
    public static List<FemValidationDiagnostic> Validate(FemSchemaEditSession session)
    {
        var result = new List<FemValidationDiagnostic>();
        var members = session.Members.Select(m => m.ElemTag).ToHashSet(StringComparer.Ordinal);
        var groups = session.MemberGroups.Select(g => g.Id).ToHashSet();
        var cases = session.LoadCases.ToDictionary(c => c.Id);
        foreach (var load in session.ElementLoads.Where(l => l.Origin == FemLoadOrigin.Manual))
        {
            string caseTag = cases.TryGetValue(load.LoadCaseId, out var lc) ? lc.Tag : load.LoadCaseId.ToString();
            if (load.TargetKind == FemLoadTargetKinds.Members)
                foreach (var tag in load.TargetTags.Where(t => !members.Contains(t)))
                    result.Add(new("element_load_member_missing",
                        string.Format(Loc.S("FemLoadErrMemberMissing"), caseTag, tag), SourceKeys: [tag]));
            if (load.TargetKind == FemLoadTargetKinds.Group && (load.GroupId is not int g || !groups.Contains(g)))
                result.Add(new("element_load_group_missing", string.Format(Loc.S("FemLoadErrGroupMissing"), caseTag)));
            if (load.Values.Any(v => !double.IsFinite(v)))
                result.Add(new("element_load_value_invalid", string.Format(Loc.S("FemLoadErrValue"), caseTag)));
        }
        return result;
    }

    /// <summary>
    /// Отступы нагрузки стержня: неотрицательны, участок (a₁ + a₂) короче стержня, точка — в его пределах. null — верно
    /// или длина неизвестна (проверит запись). Для правки ячейки — сразу, а не при «Сохранить».
    /// </summary>
    public static string? MemberLoadOffsetError(FemSchemaEditSession session, FemMemberLoad load)
    {
        if (!double.IsFinite(load.StartOffsetM) || !double.IsFinite(load.EndOffsetM) || load.StartOffsetM < 0 || load.EndOffsetM < 0)
            return Loc.S("FemLoadErrOffsetNegative");
        if (MemberLength(session, load.MemberId) is not double length) return null;
        bool point = load.DistributionType == "point";
        if (point ? load.StartOffsetM > length : load.StartOffsetM + load.EndOffsetM >= length)
            return string.Format(Loc.S(point ? "FemLoadErrPointOutside" : "FemLoadErrIntervalTooLong"), length);
        return null;
    }

    /// <summary>Длина стержня КонЭ по узлам сессии, м; null — не стержень или узлы не найдены.</summary>
    public static double? MemberLength(FemSchemaEditSession session, int memberId)
    {
        if (session.Members.FirstOrDefault(m => m.Id == memberId) is not { } member || member.ElemType == "shell") return null;
        int[] ids;
        try { ids = JsonSerializer.Deserialize<int[]>(member.NodeIdsJson) ?? []; }
        catch (JsonException) { return null; }
        if (ids.Length != 2) return null;
        var a = session.Nodes.FirstOrDefault(n => n.NodeTag == ids[0].ToString());
        var b = session.Nodes.FirstOrDefault(n => n.NodeTag == ids[1].ToString());
        if (a == null || b == null) return null;
        double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    // ── Несовершенства ───────────────────────────────────────────────────────────────────────

    /// <summary>Имя элемента несовершенства не повторяется в своём загружении (без учёта регистра).</summary>
    public static string? ImperfectionTagError(IEnumerable<FemImperfection> existing, FemImperfection item)
    {
        string tag = item.Tag.Trim();
        if (tag.Length == 0) return null;
        return existing.Any(i => i.Id != item.Id && i.LoadCaseId == item.LoadCaseId
                                 && string.Equals(i.Tag.Trim(), tag, StringComparison.CurrentCultureIgnoreCase))
            ? string.Format(Loc.S("FemImperfectionTagTaken"), tag) : null;
    }

    /// <summary>
    /// Ошибки параметров элемента: вид известен, обязательные числа положительны, охват не пуст, постановка форм —
    /// существующая постановка расчётных длин схемы. Окно проверяет то же по полям; это — для записи и документа.
    /// </summary>
    public static List<string> ImperfectionErrors(DatabaseService db, FemImperfection item)
    {
        var errors = new List<string>();
        static bool Bad(double? v) => v is { } x && (!double.IsFinite(x) || x <= 0);
        try
        {
            switch (item.Kind)
            {
                case FemImperfectionKinds.Sway:
                {
                    var p = FemSwayParams.Parse(item.ParamsJson);
                    if (p.Scope == FemSwayParams.ScopeList && p.MemberTags.Count + p.ElementTags.Count == 0)
                        errors.Add(Loc.S("FemImperfectionScopeEmpty"));
                    if (p.Scope is not (FemSwayParams.ScopeAll or FemSwayParams.ScopeList)) errors.Add(Loc.S("FemImperfectionErrBadValue"));
                    if (!double.IsFinite(p.AngleDeg)) errors.Add(Loc.S("FemImperfectionBadAngle"));
                    if (p.PhiMode == FemSwayParams.PhiManual && (p.PhiInv is null || Bad(p.PhiInv))) errors.Add(Loc.S("FemImperfectionBadPhiInv"));
                    else if (p.PhiMode is not (FemSwayParams.PhiManual or FemSwayParams.PhiEn1993 or FemSwayParams.PhiEn1992))
                        errors.Add(Loc.S("FemImperfectionErrBadValue"));
                    if (Bad(p.HeightM)) errors.Add(Loc.S("FemImperfectionBadH"));
                    if (p.M < 1) errors.Add(Loc.S("FemImperfectionBadM"));
                    break;
                }
                case FemImperfectionKinds.Bow:
                {
                    var p = FemBowParams.Parse(item.ParamsJson);
                    if (p.MemberTags.Count + p.ElementChains.Count == 0) errors.Add(Loc.S("FemImperfectionBowEmpty"));
                    if (p.Plane is not (FemBowParams.PlaneY or FemBowParams.PlaneZ)) errors.Add(Loc.S("FemImperfectionErrBadValue"));
                    if (p.E0Mode == FemBowParams.E0Relative && (p.LOverE0 is null || Bad(p.LOverE0))) errors.Add(Loc.S("FemImperfectionBadLOverE0"));
                    if (p.E0Mode == FemBowParams.E0Absolute && (p.E0Mm is null || Bad(p.E0Mm))) errors.Add(Loc.S("FemImperfectionBadE0Mm"));
                    if (Bad(p.Mu)) errors.Add(Loc.S("FemImperfectionBadMu"));
                    if (Bad(p.SectionHeightMm)) errors.Add(Loc.S("FemImperfectionBadSizeMm"));
                    break;
                }
                case FemImperfectionKinds.WallBow:
                {
                    var p = FemWallBowParams.Parse(item.ParamsJson);
                    if (p.MemberTags.Count + p.ElementTags.Count == 0) errors.Add(Loc.S("FemImperfectionScopeEmpty"));
                    if (Bad(p.Mu)) errors.Add(Loc.S("FemImperfectionBadMu"));
                    if (Bad(p.LOverE0)) errors.Add(Loc.S("FemImperfectionBadLOverE0"));
                    if (Bad(p.E0Mm)) errors.Add(Loc.S("FemImperfectionBadE0Mm"));
                    if (Bad(p.ThicknessMm)) errors.Add(Loc.S("FemImperfectionBadSizeMm"));
                    break;
                }
                case FemImperfectionKinds.Modes:
                {
                    var p = FemModesParams.Parse(item.ParamsJson);
                    if (db.GetFemAnalyses(item.SchemaId).All(a => a.Id != p.AnalysisId || a.Kind != "csfea_buckling"))
                        errors.Add(Loc.S("FemImperfectionErrModesAnalysis"));
                    if (p.Modes.Count == 0 || p.Modes.Any(m => m.Mode < 1)) errors.Add(Loc.S("FemImperfectionErrModesEmpty"));
                    break;
                }
                default:
                    errors.Add(string.Format(Loc.S("FemImperfectionErrKind"), item.Kind));
                    break;
            }
        }
        catch (JsonException) { errors.Add(Loc.S("FemImperfectionErrBadValue")); }
        return errors;
    }

    /// <summary>
    /// Записывает элемент несовершенства: загружение — сохранённое загружение-несовершенство схемы, имя не повторяется,
    /// параметры верны. Возвращает текст ошибки (база не меняется) или null.
    /// </summary>
    public static string? SaveImperfection(DatabaseService db, FemImperfection item)
    {
        var schema = db.FemSchemas.FirstOrDefault(s => s.Id == item.SchemaId);
        if (schema?.LoadCases.FirstOrDefault(c => c.Id == item.LoadCaseId) is not { } lc || !FemSp20Types.IsImperfection(lc))
            return Loc.S("FemImperfectionErrNotImperfectionCase");
        if (ImperfectionTagError(db.GetFemImperfections(item.SchemaId), item) is { } tagError) return tagError;
        if (ImperfectionErrors(db, item) is { Count: > 0 } errors) return errors[0];
        db.SaveFemImperfection(item);
        return null;
    }

    static string NameList(IReadOnlyList<string> names) =>
        string.Join(", ", names.Take(5).Select(n => $"«{n}»")) + (names.Count > 5 ? " " + string.Format(Loc.S("UsedByMore"), names.Count - 5) : "");
}
