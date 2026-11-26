using CScore;
using CScore.Fem;
using CScore.Sp16;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Ссылки проверки по КЭ: схема, проверки проекта (повтор имени), конструктивные элементы этой схемы (цель-элемент), наборы
/// усилий и постановки схемы (расчётные длины стали «Авто»).
/// </summary>
public sealed record FemCheckRefs(
    FemSchema Schema, IReadOnlyCollection<FemCheck> Checks, IReadOnlyCollection<FemMember> Members,
    IReadOnlyCollection<ForceSet> ForceSets, IReadOnlyCollection<FemAnalysis> Analyses)
{
    /// <summary>Ссылки по проекту: проверки, конструктивные элементы, наборы и постановки схемы.</summary>
    public static FemCheckRefs Of(DatabaseService db, FemSchema schema) => new(
        schema, db.FemChecks, db.GetFemMembers(schema.Id), db.ForceSets,
        schema.Analyses.Count > 0 ? schema.Analyses : db.GetFemAnalyses(schema.Id));
}

/// <summary>
/// Нормативные проверки по КЭ 1-й и 2-й ГПС (сталь, ЖБ стержни, ЖБ пластины): проверка с путями полей документа
/// (<c>tag</c>, <c>target</c>, <c>forceSets[2]</c>, <c>eta.muX</c>, <c>plate.acrcLimMm</c>), имя по умолчанию без повтора в
/// схеме, запись с целью и сбросом результата при смене входа, вопрос удаления — одно для окон, страницы группы и CLI.
/// </summary>
public static class FemCheckAuthoring
{
    public const string Steel = "steel_check";
    public const string RcBar = "rc_check";
    public const string RcPlate = "rc_plate_check";

    /// <summary>Виды пластин 1-й ГПС в порядке окна.</summary>
    public static readonly string[] PlateUlsKinds = ["shell_simpl_wa_uls", "shell_simpl_capri_uls", "shell_layered"];
    /// <summary>Виды пластин 2-й ГПС в порядке окна.</summary>
    public static readonly string[] PlateSlsKinds = ["shell_simpl_wa_sls", "shell_simpl_capri_sls", "shell_layered"];

    static readonly string[] CalcTypes = ["C", "CL", "N", "NL"];
    static readonly string[] SlsCalcTypes = ["N", "NL"];

    /// <summary>Проверка пластин по трещинам (2-я ГПС).</summary>
    public static bool IsSls(FemCheck c) => c.NormCode == RcPlate && IsSlsParams(PlateCheckParams.Parse(c.ParamsJson));

    static bool IsSlsParams(PlateCheckParams p) =>
        p.CheckGroup == "sls" || p.CheckGroup.Length == 0 && p.Kind.EndsWith("_sls", StringComparison.Ordinal);

    // ── Имя ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Ошибка имени: пусто или уже занято другой проверкой той же схемы; null — годится.</summary>
    public static string? TagError(IEnumerable<FemCheck> checks, int schemaId, string? tag, int selfId = 0)
    {
        string t = (tag ?? "").Trim();
        if (t.Length == 0) return Loc.S("FemCheckTagEmpty");
        return checks.Any(c => c.Id != selfId && c.SchemaId == schemaId
                && string.Equals(c.Tag.Trim(), t, StringComparison.CurrentCultureIgnoreCase))
            ? string.Format(Loc.S("FemCheckTagTaken"), t) : null;
    }

    /// <summary>Свободное имя на основе <paramref name="baseTag"/>: само имя, иначе «имя (2)», «имя (3)»…</summary>
    public static string FreeTag(IEnumerable<FemCheck> checks, int schemaId, string baseTag, int selfId = 0)
    {
        var list = checks as IReadOnlyCollection<FemCheck> ?? checks.ToList();
        string b = baseTag.Trim();
        if (b.Length == 0) b = Loc.S("FemCheckDefaultTag");
        if (TagError(list, schemaId, b, selfId) == null) return b;
        for (int i = 2; ; i++)
        {
            string t = $"{b} ({i})";
            if (TagError(list, schemaId, t, selfId) == null) return t;
        }
    }

    /// <summary>Имя по умолчанию: «цель / вид» — вид словами, у пластин — метод проверки.</summary>
    public static string AutoTag(string targetTag, string normCode, string? plateKind, bool sls = false) =>
        $"{targetTag.Trim()} / {KindLabel(normCode, plateKind, sls)}";

    /// <summary>Подпись вида проверки (как в списке окна).</summary>
    public static string KindLabel(string normCode, string? plateKind, bool sls = false) => normCode switch
    {
        Steel => Loc.S("FemCheckNormCodeSteel"),
        RcBar => Loc.S("FemCheckNormCodeRcBar"),
        RcPlate => plateKind switch
        {
            "shell_simpl_wa_uls" => Loc.S("PlateKindWaUls"),
            "shell_simpl_capri_uls" => Loc.S("PlateKindCapriUls"),
            "shell_simpl_wa_sls" => Loc.S("PlateKindWaSls"),
            "shell_simpl_capri_sls" => Loc.S("PlateKindCapriSls"),
            "shell_layered" => Loc.S(sls ? "PlateKindLayeredSls" : "PlateKindLayered"),
            _ => Loc.S("FemCheckNormCodeRcPlate"),
        },
        _ => normCode,
    };

    // ── Проверка ────────────────────────────────────────────────────────────────────────────

    /// <summary>Проверка проверки 1-й/2-й ГПС (продавливание — только имя и цель-схема, его поля проверяет своё окно).</summary>
    public static List<FieldError> Validate(FemCheckRefs refs, FemCheck c)
    {
        var e = new List<FieldError>();
        if (TagError(refs.Checks, refs.Schema.Id, c.Tag, c.Id) is { } tagError) e.Add(new("tag", tagError));
        if (c.SchemaId != refs.Schema.Id) e.Add(new("schema", Loc.S("FemCheckTargetMissing")));
        if (c.NormCode == PunchingFemCheckParams.NormCode) return e;
        if (c.NormCode is not (Steel or RcBar or RcPlate))
        {
            e.Add(new("kind", string.Format(Loc.S("FemCheckKindUnknown"), c.NormCode)));
            return e;
        }

        // Цель-элемент — по ненулевому Id: в плане CLI у элементов из документа условные отрицательные Id.
        bool targetOk = c.ElementId is int eid && eid != 0
            ? refs.Members.Any(m => m.Id == eid)
            : c.MemberId != 0 && refs.Schema.MemberGroups.Any(g => g.Id == c.MemberId);
        if (!targetOk) e.Add(new("target", Loc.S("FemCheckTargetMissing")));

        bool plate = c.NormCode == RcPlate;
        var plateParams = plate ? PlateCheckParams.Parse(c.ParamsJson) : null;
        bool sls = plateParams != null && IsSlsParams(plateParams);

        var ids = c.GetForceSetIds();
        for (int i = 0; i < ids.Count; i++)
        {
            if (refs.ForceSets.FirstOrDefault(f => f.Id == ids[i]) is not { } fs)
                e.Add(new($"forceSets[{i}]", Loc.S("FemCheckForceSetMissing")));
            else if ((fs.Kind == "shell") != plate)
                e.Add(new($"forceSets[{i}]", string.Format(Loc.S(plate ? "FemCheckForceSetNotShell" : "FemCheckForceSetNotBar"), fs.Tag)));
        }

        if (c.CalcTypeOverride is { } ct && !(sls ? SlsCalcTypes : CalcTypes).Contains(ct))
            e.Add(new("calcType", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", sls ? SlsCalcTypes : CalcTypes))));

        switch (c.NormCode)
        {
            case Steel:
                if (SteelFemCheckParams.TryParse(c.ParamsJson) is { } st) SteelParams(st, refs, e);
                break;
            case RcBar:
                var bar = BarCheckParams.Parse(c.ParamsJson);
                Rebar(bar.RebarSources, plate: false, e);
                if (bar.Eta is { Enabled: true } be) Eta(be, plate: false, e);
                break;
            case RcPlate:
                PlateParams(plateParams!, sls, refs, e);
                break;
        }
        return e;
    }

    static void Rebar(IReadOnlyList<string> keys, bool plate, List<FieldError> e)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            bool known = keys[i] is FemCheckRebarSource.Section or FemCheckRebarSource.Assigned or FemCheckRebarSource.Selected
                || plate && keys[i] == FemCheckRebarSource.Layout;
            if (!known) e.Add(new($"rebar[{i}]", string.Format(Loc.S("FemCheckRebarSourceInvalid"), keys[i])));
        }
    }

    static void Num(List<FieldError> e, string path, string labelKey, double v, Func<double, bool> ok, string ruleKey)
    {
        if (!double.IsFinite(v) || !ok(v)) e.Add(new(path, $"{Label(labelKey)}: {Loc.S(ruleKey)}"));
    }

    static string Label(string key) => Loc.S(key).Trim().TrimEnd(':');

    static void Eta(FemEtaParams eta, bool plate, List<FieldError> e)
    {
        if (eta.LengthM is { } l) Num(e, "eta.lengthM", "FemCheckEtaLength", l, v => v > 0, "FieldErrPositive");
        Num(e, "eta.muX", "EtaMuxLabel", eta.MuX, v => v > 0, "FieldErrPositive");
        if (!plate) Num(e, "eta.muY", "EtaMuyLabel", eta.MuY, v => v > 0, "FieldErrPositive");
        if (eta.PsiMode is not (FemEtaParams.PsiAuto or FemEtaParams.PsiManual))
            e.Add(new("eta.psi", string.Format(Loc.S("CliModelErrOneOf"), "auto, manual")));
        Num(e, "eta.psiX", "EtaPsixLabel", eta.PsiX, v => v >= 0, "FieldErrNonNegative");
        if (!plate) Num(e, "eta.psiY", "EtaPsiyLabel", eta.PsiY, v => v >= 0, "FieldErrNonNegative");
        if (eta.SlendernessThreshold is { } s) Num(e, "eta.slendernessThreshold", "FemCheckEtaSlenderness", s, v => v > 0, "FieldErrPositive");
    }

    static void SteelParams(SteelFemCheckParams p, FemCheckRefs refs, List<FieldError> e)
    {
        if (p.MeshLef is { } m)
        {
            Num(e, "steel.muX", "EtaMuxLabel", m.MuX, v => v > 0, "FieldErrPositive");
            Num(e, "steel.muY", "EtaMuyLabel", m.MuY, v => v > 0, "FieldErrPositive");
            Num(e, "steel.muB", "Sp16MeshMuBShort", m.MuB, v => v >= 0, "FieldErrNonNegative");
        }
        if (p.AutoLef is { } a && !refs.Analyses.Any(x => x.Id == a.AnalysisId))
            e.Add(new("steel.analysis", Loc.S("FemCheckSteelAnalysisMissing")));
    }

    static void PlateParams(PlateCheckParams p, bool sls, FemCheckRefs refs, List<FieldError> e)
    {
        if (!(sls ? PlateSlsKinds : PlateUlsKinds).Contains(p.Kind))
            e.Add(new("plate.method", Loc.S("FemCheckPlateMethodInvalid")));
        Rebar(p.RebarSources, plate: true, e);
        if (p.Phi1Mode is not ("auto" or "manual"))
            e.Add(new("plate.phi1", string.Format(Loc.S("CliModelErrOneOf"), "auto, manual")));
        if (!sls)
        {
            if (p.Eta is { Enabled: true } eta) Eta(eta, plate: true, e);
            return;
        }
        Num(e, "plate.acrcLimMm", "FemCheckDlgAcrcLim", p.AcrcLimMm, v => v > 0, "FieldErrPositive");
        bool layered = p.Kind == "shell_layered";
        if (!layered)
        {
            if (p.Phi1Mode == "manual") Num(e, "plate.phi1", "FemCheckPhi1Label", p.Phi1, v => v > 0, "FieldErrPositive");
            Num(e, "plate.phi2", "FemSlsDlgPhi2Label", p.Phi2, v => v > 0, "FieldErrPositive");
            return;
        }
        Num(e, "plate.phi2", "FemSlsDlgPhi2Label", p.Phi2, v => v > 0, "FieldErrPositive");
        if (p.NlForceSetId > 0)
        {
            if (refs.ForceSets.FirstOrDefault(f => f.Id == p.NlForceSetId) is not { Kind: "shell" })
                e.Add(new("plate.nlForceSet", Loc.S("FemCheckNlSetMissing")));
        }
        else if (!double.IsFinite(p.LtFraction) || p.LtFraction < 0 || p.LtFraction > 1)
            e.Add(new("plate.ltFraction", $"{Label("FemSlsDlgLtFraction")}: {string.Format(Loc.S("FieldErrRangeCC"), 0, 1)}"));
    }

    // ── Запись ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Копия проверки (для черновика правки).</summary>
    public static FemCheck Copy(FemCheck c) => new()
    {
        Id = c.Id, SchemaId = c.SchemaId, MemberId = c.MemberId, ElementId = c.ElementId, NormCode = c.NormCode,
        ParamsJson = c.ParamsJson, ResultId = c.ResultId, Tag = c.Tag, ForceSetIdsJson = c.ForceSetIdsJson,
        CalcTypeOverride = c.CalcTypeOverride,
    };

    /// <summary>
    /// Параметры, приведённые к одному виду (разбор и повторная запись): старые записи без новых полей и та же правка
    /// другим порядком полей — одно и то же.
    /// </summary>
    static string? Normalized(string normCode, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return normCode switch
        {
            RcPlate => PlateCheckParams.Parse(json).ToJson(),
            RcBar => BarCheckParams.Parse(json).ToJson(),
            Steel => SteelFemCheckParams.TryParse(json)?.ToJson() ?? json,
            _ => json,
        };
    }

    static string Sets(string? json) => string.IsNullOrEmpty(json) || json == "null" ? "[]" : json;

    /// <summary>
    /// Переносит правку <paramref name="edited"/> в сохранённую <paramref name="target"/>. Вход (схема, цель, вид,
    /// наборы, тип характеристик, параметры) изменился — результат сбрасывается; имя — нет. Параметры, равные по
    /// содержанию, остаются прежним текстом. Возвращает (изменилось ли что-нибудь, изменился ли вход).
    /// </summary>
    public static (bool Changed, bool InputChanged) ApplyEdit(FemCheck target, FemCheck edited)
    {
        bool sameParams = target.NormCode == edited.NormCode
            && Normalized(target.NormCode, target.ParamsJson) == Normalized(edited.NormCode, edited.ParamsJson);
        bool input = target.SchemaId != edited.SchemaId || target.MemberId != edited.MemberId
            || (target.ElementId ?? 0) != (edited.ElementId ?? 0) || target.NormCode != edited.NormCode || !sameParams
            || Sets(target.ForceSetIdsJson) != Sets(edited.ForceSetIdsJson) || target.CalcTypeOverride != edited.CalcTypeOverride;
        bool changed = input || target.Tag != edited.Tag;
        target.SchemaId = edited.SchemaId;
        target.MemberId = edited.MemberId;
        target.ElementId = edited.ElementId;
        target.NormCode = edited.NormCode;
        if (!sameParams) target.ParamsJson = edited.ParamsJson;
        target.ForceSetIdsJson = Sets(edited.ForceSetIdsJson);
        target.CalcTypeOverride = edited.CalcTypeOverride;
        target.Tag = edited.Tag;
        return (changed, input);
    }

    /// <summary>
    /// Записывает новую проверку или правку существующей. Смена входа удаляет результаты проверки (как удаление) —
    /// иначе дерево и мозаика показывали бы результат прежних параметров. Возвращает (записано ли, сброшен ли результат).
    /// </summary>
    public static (bool Changed, bool ResultReset) Save(DatabaseService db, FemCheck? existing, FemCheck draft)
    {
        if (existing == null)
        {
            draft.Id = 0;
            draft.ResultId = null;
            db.SaveFemCheck(draft);
            return (true, false);
        }
        bool hadResult = existing.ResultId != null || db.GetCalcResultHeaderByFemCheck(existing.Id) != null;
        var (changed, input) = ApplyEdit(existing, draft);
        if (!changed) return (false, false);
        db.SaveFemCheck(existing, resetResult: input, refreshTree: true);
        return (true, input && hadResult);
    }

    // ── Удаление ────────────────────────────────────────────────────────────────────────────

    /// <summary>Вопрос удаления: имя и есть ли результат.</summary>
    public static string DeleteQuestion(FemCheck check, bool hasResult)
    {
        string text = string.Format(Loc.S("FemCheckDeleteConfirm"), check.DisplayTag);
        if (hasResult) text += "\n\n" + Loc.S("FemCheckDeleteResult");
        return text;
    }

    /// <summary>Спрашивает и удаляет проверку вместе с её результатами; false — пользователь отказался.</summary>
    public static bool Delete(DatabaseService db, FemCheck check, IUserNotifier notifier)
    {
        bool hasResult = check.ResultId != null || db.GetCalcResultHeaderByFemCheck(check.Id) != null;
        if (!notifier.Confirm(DeleteQuestion(check, hasResult), Loc.S("FemCheckDeleteTitle"))) return false;
        db.DeleteFemCheck(check);
        return true;
    }
}
