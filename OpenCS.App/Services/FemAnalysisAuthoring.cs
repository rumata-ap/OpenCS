using CScore.Fem;
using CScore.Fem.Imperfections;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Что затронет удаление постановки: результат и те, кто на неё ссылается.</summary>
public sealed record FemAnalysisDeleteImpact(bool HasResult, IReadOnlyList<string> Dependents);

/// <summary>
/// Постановка расчёта схемы: проверка с путями полей (для окна постановки и вида <c>femAnalyses</c> документа), имя по
/// умолчанию и его уникальность в схеме, запись со сбросом результата при смене входа, вопрос удаления — одно для окна
/// и CLI. Пути — как в документе: <c>tag</c>, <c>stages[1].step</c>, <c>csfea.iterations.omega0</c>, <c>batch.items[3]</c>.
/// </summary>
public static class FemAnalysisAuthoring
{
    /// <summary>Виды постановок в порядке окна: свой решатель первым.</summary>
    public static readonly string[] Kinds =
    [
        FemCsfeaRunner.AnalysisKind, FemCsfeaRunner.LinearAnalysisKind, FemBucklingRunner.AnalysisKind,
        OpenSeesLinear, OpenSeesNonlinear,
    ];

    public const string OpenSeesLinear = "linear";
    public const string OpenSeesNonlinear = "nonlinear";

    /// <summary>Ошибка имени: пусто или уже занято другой постановкой схемы; null — годится.</summary>
    public static string? TagError(FemSchema schema, string? tag, int selfId = 0)
    {
        string t = (tag ?? "").Trim();
        if (t.Length == 0) return Loc.S("FemAnalysisTagEmpty");
        return schema.Analyses.Any(a => a.Id != selfId && string.Equals(a.Tag.Trim(), t, StringComparison.CurrentCultureIgnoreCase))
            ? string.Format(Loc.S("FemAnalysisTagTaken"), t) : null;
    }

    /// <summary>
    /// Свободное имя на основе <paramref name="baseTag"/>: само имя, иначе «имя (2)», «имя (3)»… Для пустого имени окна
    /// (оно берёт имя первой стадии или нагрузки) — чтобы две постановки одной комбинации не назывались одинаково.
    /// </summary>
    public static string FreeTag(FemSchema schema, string baseTag, int selfId = 0)
    {
        string b = baseTag.Trim();
        if (b.Length == 0) b = Loc.S("FemAnalysisDefaultTag");
        if (TagError(schema, b, selfId) == null) return b;
        for (int i = 2; ; i++)
        {
            string t = $"{b} ({i})";
            if (TagError(schema, t, selfId) == null) return t;
        }
    }

    /// <summary>
    /// Проверка постановки схемы. <paramref name="nodeIds"/> — Id узлов КонЭ схемы (узлы пути OpenSees); null — не
    /// проверять.
    /// </summary>
    public static List<FieldError> Validate(FemSchema schema, FemAnalysis a, IReadOnlyCollection<int>? nodeIds = null)
    {
        var e = new List<FieldError>();
        if (TagError(schema, a.Tag, a.Id) is { } tagError) e.Add(new("tag", tagError));
        if (!Kinds.Contains(a.Kind))
        {
            e.Add(new("kind", string.Format(Loc.S("FemAnalysisKindUnknown"), a.Kind)));
            return e;
        }
        FemAnalysisParams p;
        try { p = FemAnalysisParams.Parse(a.ParamsJson); }
        catch (System.Text.Json.JsonException) { e.Add(new("kind", Loc.S("FemAnalysisParamsBroken"))); return e; }

        var caseIds = schema.LoadCases.Select(c => c.Id).ToHashSet();
        void Load(string path, string? json)
        {
            if (LoadError(json, caseIds) is { } msg) e.Add(new(path, msg));
        }

        switch (a.Kind)
        {
            case OpenSeesLinear:
                Load("load", a.LoadExpressionJson);
                break;
            case OpenSeesNonlinear:
                Stages(p.Stages, e, Load, csfea: false, nodeIds);
                if (p.SteelHardeningModulusMpa is { } h && !(h >= 0 && double.IsFinite(h)))
                    e.Add(new("opensees.hardeningMpa", Loc.S("FemAnalysisHardeningInvalid")));
                break;
            case FemCsfeaRunner.LinearAnalysisKind:
                if (p.Stages.Count == 0) e.Add(new("loadCases", Loc.S("FemCsfeaLinearCasesEmpty")));
                for (int i = 0; i < p.Stages.Count; i++) Load($"loadCases[{i}]", p.Stages[i].LoadExpressionJson);
                Csfea(p.Csfea ?? new(), e, linear: true);
                break;
            case FemCsfeaRunner.AnalysisKind:
                Csfea(p.Csfea ?? new(), e, linear: false);
                if (p.Batch is { } batch) Batch(schema, batch, e, Load);
                else Stages(p.Stages, e, Load, csfea: true, nodeIds);
                break;
            case FemBucklingRunner.AnalysisKind:
                if (p.Stages.Count == 0) e.Add(new("combinations", Loc.S("FemBucklingCombinationsEmpty")));
                for (int i = 0; i < p.Stages.Count; i++) Load($"combinations[{i}]", p.Stages[i].LoadExpressionJson);
                var b = p.Buckling ?? new FemBucklingParams();
                if (b.Modes is < 1 or > 200) e.Add(new("buckling.modes", Loc.S("FemBucklingModesInvalid")));
                if (!(b.GFixed >= 0) || !(b.GPinned > 0) || !(b.SwayStiffness > 0))
                    e.Add(new("buckling", Loc.S("FemBucklingNomogramInvalid")));
                break;
        }
        return e;
    }

    /// <summary>Текст ошибки нагрузки (пусто, разбор, загружения нет в схеме); null — годится.</summary>
    public static string? LoadError(string? json, IReadOnlySet<int> caseIds)
    {
        FemLoadExpression x;
        try { x = FemLoadExpression.Parse(json); }
        catch (System.Text.Json.JsonException) { return Loc.S("FemAnalysisLoadBroken"); }
        var ids = x.LoadCaseIds.Concat(x.Terms.Select(t => t.LoadCaseId)).ToList();
        if (x.Mode == FemLoadExpressionMode.All && x.Terms.Count == 0) return caseIds.Count > 0 ? null : Loc.S("FemAnalysisLoadMissing");
        if (ids.Count == 0) return Loc.S("FemAnalysisLoadMissing");
        return ids.Any(id => !caseIds.Contains(id)) ? Loc.S("FemAnalysisLoadUnresolved") : null;
    }

    static void Stages(List<FemAnalysisStage> stages, List<FieldError> e, Action<string, string?> load, bool csfea,
        IReadOnlyCollection<int>? nodeIds)
    {
        if (stages.Count == 0) { e.Add(new("stages", Loc.S("FemAnalysisStagesEmpty"))); return; }
        for (int i = 0; i < stages.Count; i++)
        {
            var s = stages[i];
            string path = $"stages[{i}]";
            load($"{path}.load", s.LoadExpressionJson);
            bool byLoad = s.PathControl is not { Mode: "DisplacementControl" or "ArcLength" };
            if (byLoad)
            {
                if (!(s.LoadFactorStep is > 0 and var st && double.IsFinite(st)) || csfea && s.LoadFactorStep > 1)
                    e.Add(new($"{path}.step", string.Format(Loc.S(csfea ? "FemAnalysisStageStepInvalidCsfea" : "FemAnalysisStageStepInvalid"), s.Tag)));
                else if (!(s.MaxLoadFactor is { } max && double.IsFinite(max) && max >= s.LoadFactorStep))
                    e.Add(new($"{path}.max", string.Format(Loc.S("FemAnalysisStageMaxInvalid"), s.Tag)));
            }
            if (!csfea)
            {
                PathControl(s.PathControl, $"{path}.pathControl", s.Tag, e, nodeIds);
                PathControl(s.ContinueWith, $"{path}.continueWith", s.Tag, e, nodeIds);
            }
        }
    }

    /// <summary>Параметры пути OpenSees: узел, направление, приращения, число шагов.</summary>
    public static void PathControl(FemAnalysisPathControl? pc, string path, string stage, List<FieldError> e,
        IReadOnlyCollection<int>? nodeIds)
    {
        if (pc == null || pc.Mode == "LoadControl") return;
        void Err(string field, string key) => e.Add(new($"{path}.{field}", string.Format(Loc.S(key), stage)));
        if (pc.Mode == "DisplacementControl")
        {
            if (pc.ControlNodeId is not { } node || nodeIds != null && !nodeIds.Contains(node)) Err("node", "FemPathControlNodeMissing");
            if (pc.ControlDof is not (>= 1 and <= 6)) Err("dof", "FemPathControlDofInvalid");
            // Шаги — модули (знак задаёт целевое перемещение): 0 < min ≤ начальный ≤ max, как требует FemNonlinearModel.
            if (pc is not { InitialIncrement: { } init, MinIncrement: { } min, MaxIncrement: { } max }
                || !double.IsFinite(init) || !double.IsFinite(min) || !double.IsFinite(max)
                || !(min > 0) || min > init || init > max)
                Err("initial", "FemPathControlIncrementsInvalid");
            if (pc.TargetDisplacement is not { } target || !double.IsFinite(target) || target == 0)
                Err("target", "FemPathControlTargetInvalid");
        }
        else if (pc.Mode == "ArcLength")
        {
            if (pc.MonitorNodeId is not { } node || nodeIds != null && !nodeIds.Contains(node)) Err("node", "FemPathControlNodeMissing");
            if (pc.MonitorDof is not (>= 1 and <= 6)) Err("dof", "FemPathControlDofInvalid");
            if (pc is not { ArcLengthS: > 0 and var s, ArcLengthMinS: > 0 and var minS } || !double.IsFinite(s) || minS > s)
                Err("s", "FemPathControlArcInvalid");
            if (pc.ArcLengthAlpha is not { } alpha || !(alpha >= 0) || !double.IsFinite(alpha))
                Err("alpha", "FemPathControlAlphaInvalid");
        }
        else
        {
            e.Add(new($"{path}.mode", string.Format(Loc.S("FemPathControlModeUnknown"), pc.Mode)));
            return;
        }
        if (pc.MaxSteps is not > 0) Err("maxSteps", "FemPathControlMaxStepsInvalid");
    }

    static void Csfea(FemCsfeaParams c, List<FieldError> e, bool linear)
    {
        if (c.ControlDof is < 0 or > 5) e.Add(new("csfea.controlDof", Loc.S("FemCsfeaControlDofInvalid")));
        if (linear) return;
        if (c.PoissonUncracked is { } nu && !(nu >= 0 && nu < 0.5)) e.Add(new("csfea.poisson", Loc.S("FemCsfeaPoissonInvalid")));
        if (c.MaxIterations < 1) e.Add(new("csfea.iterations.max", Loc.S("FemCsfeaMaxIterationsInvalid")));
        if (c.MaxBisections < 0) e.Add(new("csfea.iterations.bisections", Loc.S("FemCsfeaMaxBisectionsInvalid")));
        if (!(c.TolDisplacement > 0) || !double.IsFinite(c.TolDisplacement))
            e.Add(new("csfea.iterations.tolU", Loc.S("FemCsfeaToleranceInvalid")));
        if (!(c.TolStiffness > 0) || !double.IsFinite(c.TolStiffness))
            e.Add(new("csfea.iterations.tolK", Loc.S("FemCsfeaToleranceInvalid")));
        if (!(c.Omega0 > 0 && c.Omega0 <= 1)) e.Add(new("csfea.iterations.omega0", Loc.S("FemCsfeaOmega0Invalid")));
        if (c.ResultRecording is not (FemCsfeaRecording.Final or FemCsfeaRecording.Selected or FemCsfeaRecording.All))
            e.Add(new("csfea.recording.mode", string.Format(Loc.S("CliModelErrOneOf"), "final, selected, all")));
        else if (c.ResultRecording == FemCsfeaRecording.Selected)
        {
            var steps = FemCsfeaParams.ParseRecordSteps(c.RecordSteps, out var stepsError);
            if (steps == null) e.Add(new("csfea.recording.steps", stepsError ?? ""));
            else if (steps.Count == 0 && !c.RecordStageEnds) e.Add(new("csfea.recording.steps", Loc.S("FemCsfeaRecordStepsEmpty")));
        }
    }

    static void Batch(FemSchema schema, FemCsfeaBatch b, List<FieldError> e, Action<string, string?> load)
    {
        if (b.Items.Count == 0) e.Add(new("batch.items", Loc.S("FemCsfeaBatchEmpty")));
        for (int i = 0; i < b.Items.Count; i++) load($"batch.items[{i}]", b.Items[i].LoadExpressionJson);
        if (!(b.LoadFactorStep > 0 && b.LoadFactorStep <= 1) || !(b.MaxLoadFactor >= b.LoadFactorStep) || !double.IsFinite(b.MaxLoadFactor))
            e.Add(new("batch.step", Loc.S("FemCsfeaBatchFactorsInvalid")));
        if (b.Recording is not (FemCsfeaBatchRecording.Fields or FemCsfeaBatchRecording.Envelopes))
            e.Add(new("batch.recording", string.Format(Loc.S("CliModelErrOneOf"), "fields, envelopes")));
        for (int i = 0; i < b.Imperfections.Count; i++)
            if (schema.LoadCases.FirstOrDefault(c => c.Id == b.Imperfections[i].LoadCaseId) is not { } lc || !FemSp20Types.IsImperfection(lc))
                e.Add(new($"batch.imperfections[{i}]", Loc.S("FemCsfeaBatchImperfectionMissing")));
    }

    /// <summary>Вариант пакета «комбинация схемы»: имя, тип СП 20 (бейдж) и копия формулы — как в окне выбора.</summary>
    public static FemCsfeaBatchItem BatchItem(FemLoadDefinition d) => new()
    {
        Tag = d.Tag, LoadExpressionJson = d.GetExpression().ToJson(), Kind = FemCsfeaBatchItemKind.Combination,
        Type = CScore.Fem.Combinations.FemCombinationTypes.Normalize(d.CombinationType),
    };

    /// <summary>Вариант пакета «одно загружение»: бейдж G/L/Q/A или знак несовершенства.</summary>
    public static FemCsfeaBatchItem BatchItem(FemLoadCase lc) => new()
    {
        Tag = lc.Tag, LoadExpressionJson = SingleCase(lc.Id), Kind = FemCsfeaBatchItemKind.LoadCase,
        Type = FemSp20Types.IsImperfection(lc) ? Loc.S("FemLoadCaseImperfectionBadge")
            : CScore.Fem.Combinations.FemCombinationCoefficients.LoadType(lc) switch
            {
                CScore.Combinations.NormLoadType.Permanent => "G",
                CScore.Combinations.NormLoadType.LongTerm => "L",
                CScore.Combinations.NormLoadType.Accidental => "A",
                _ => "Q",
            },
    };

    /// <summary>Формула «одно загружение целиком» — как у окна выбора и списка загружений линейного CSfea.</summary>
    public static string SingleCase(int loadCaseId) =>
        new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [loadCaseId] }.ToJson();

    /// <summary>
    /// Переносит правку <paramref name="edited"/> в сохранённую <paramref name="target"/>: результат сбрасывается при
    /// смене вида, нагрузки или параметров (имя не сбрасывает; у пакета правка только списка вариантов — досчёт при
    /// запуске). Возвращает (изменилось ли что-нибудь, сброшен ли результат).
    /// </summary>
    public static (bool Changed, bool ResultReset) ApplyEdit(FemAnalysis target, FemAnalysis edited)
    {
        bool batchItemsOnly = target.Kind == edited.Kind && FemAnalysisParams.BatchItemsOnlyChanged(target.ParamsJson, edited.ParamsJson);
        bool inputChanged = !batchItemsOnly && (target.Kind != edited.Kind || target.LoadExpressionJson != edited.LoadExpressionJson
            || target.ParamsJson != edited.ParamsJson);
        bool changed = inputChanged || batchItemsOnly && target.ParamsJson != edited.ParamsJson || target.Tag != edited.Tag;
        target.Tag = edited.Tag;
        target.Kind = edited.Kind;
        target.LoadExpressionJson = edited.LoadExpressionJson;
        target.ParamsJson = edited.ParamsJson;
        bool reset = inputChanged && target.ResultId != null;
        if (inputChanged) target.InvalidateResult();
        return (changed, reset);
    }

    /// <summary>Результат и ссылки на постановку: линейные на её жёсткостях, проверки (продавливание, расчётные длины
    /// стали), несовершенства по её формам.</summary>
    public static FemAnalysisDeleteImpact DeleteImpact(DatabaseService db, FemAnalysis analysis)
    {
        var deps = new List<string>();
        if (db.FemSchemas.FirstOrDefault(s => s.Id == analysis.SchemaId) is { } schema)
            foreach (var other in schema.Analyses.Where(o => o.Id != analysis.Id))
                if (SafeParams(other)?.Csfea?.StiffnessSource?.AnalysisId == analysis.Id)
                    deps.Add(string.Format(Loc.S("FemAnalysisDependentAnalysis"), other.Tag));
        string id = analysis.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var check in db.FemChecks.Where(c => c.SchemaId == analysis.SchemaId))
        {
            bool refers = check.NormCode == PunchingFemCheckParams.NormCode
                ? PunchingFemCheckParams.Parse(check.ParamsJson).SourceRef == id
                : check.NormCode == "steel_check" && SteelFemCheckParams.TryParse(check.ParamsJson)?.AutoLef?.AnalysisId == analysis.Id;
            if (refers) deps.Add(string.Format(Loc.S("FemAnalysisDependentCheck"), check.DisplayTag));
        }
        foreach (var imp in db.GetFemImperfections(analysis.SchemaId).Where(i => i.Kind == FemImperfectionKinds.Modes))
            if (SafeModes(imp)?.AnalysisId == analysis.Id)
                deps.Add(string.Format(Loc.S("FemAnalysisDependentImperfection"), imp.Tag));
        return new(analysis.ResultId != null, deps);
    }

    /// <summary>Вопрос удаления: имя, есть ли результат, кто перестанет находить постановку.</summary>
    public static string DeleteQuestion(FemAnalysis analysis, FemAnalysisDeleteImpact impact)
    {
        string text = string.Format(Loc.S("FemAnalysisDeleteConfirm"), analysis.Tag);
        if (impact.HasResult) text += "\n\n" + Loc.S("FemAnalysisDeleteResult");
        if (impact.Dependents.Count > 0)
            text += "\n\n" + Loc.S("FemAnalysisDeleteDependents") + "\n" + string.Join("\n", impact.Dependents.Select(d => "• " + d));
        return text;
    }

    /// <summary>Спрашивает и удаляет; false — пользователь отказался.</summary>
    public static bool Delete(DatabaseService db, FemAnalysis analysis, IUserNotifier notifier)
    {
        if (!notifier.Confirm(DeleteQuestion(analysis, DeleteImpact(db, analysis)), Loc.S("FemAnalysisDeleteTitle"))) return false;
        db.DeleteFemAnalysis(analysis);
        return true;
    }

    static FemAnalysisParams? SafeParams(FemAnalysis a)
    {
        try { return FemAnalysisParams.Parse(a.ParamsJson); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    static FemModesParams? SafeModes(FemImperfection imp)
    {
        try { return FemModesParams.Parse(imp.ParamsJson); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException) { return null; }
    }
}
