using CScore.Fem;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Служебные постановки родителя для масок всех загружений фрагмента. Маски фрагмента снимаются из одной постановки
/// родителя (<see cref="FragmentMaskSourceKeys"/>), поэтому в ней должны быть все воздействия, которые использует расчёт
/// фрагмента:
/// <list type="bullet">
/// <item>линейная «Маски фрагмента» — стадия «одно загружение целиком» на каждое общее загружение родителя;</item>
/// <item>секущая «Маски фрагмента · секущий» — стадии секущих постановок фрагмента, переведённые на загружения родителя.</item>
/// </list>
/// Пользовательские постановки не меняются; служебные создаются или обновляются при каждом вызове (расчёт — у вызывающего).
/// </summary>
public static class StoreyFragmentMaskSupply
{
    /// <summary>Имя служебной линейной постановки родителя.</summary>
    public const string AnalysisTag = "Маски фрагмента";

    /// <summary>Имя служебной секущей постановки родителя.</summary>
    public const string SecantAnalysisTag = "Маски фрагмента · секущий";

    /// <summary>Линейная служебная постановка: стадия на каждое общее загружение. null — постановку не создать.</summary>
    public static FemAnalysis? Prepare(DatabaseService db, FemSchema fragment, out string? reason)
    {
        if (!Context(db, fragment, out var ctx, out reason)) return null;
        // Несовершенство (крен) нагрузок не содержит: стадия из него одного — ошибка «без загружений» на весь расчёт.
        var stages = ctx.ParentLoadCases.Where(c => ctx.Map.ContainsKey(c.Id) && !FemSp20Types.IsImperfection(c)).Select(c => new FemAnalysisStage
        {
            Tag = c.Tag,
            LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [c.Id] }.ToJson(),
            LoadFactorStep = 1.0,
            MaxLoadFactor = 1.0,
        }).ToList();
        if (stages.Count == 0)
        {
            reason = Loc.S("StoreyFragmentMasksNoCommonLoadCases");
            return null;
        }
        // Параметры — как у линейной постановки из диалога по умолчанию (тип расчёта N, настройки CSfea по умолчанию).
        string paramsJson = new FemAnalysisParams { Stages = stages, CalcType = CScore.CalcType.N, Csfea = new FemCsfeaParams() }.ToJson();
        return Save(db, ctx.ParentId, AnalysisTag, FemCsfeaRunner.LinearAnalysisKind, stages[0].LoadExpressionJson, paramsJson);
    }

    /// <summary>
    /// Секущая служебная постановка: стадии самой длинной секущей постановки фрагмента (остальные должны быть её
    /// префиксами — тогда их ключи масок входят в набор) с Id загружений родителя. Параметры секущего расчёта (тип
    /// расчёта, настройки CSfea) — как у той же постановки фрагмента. Пакетные постановки не поддерживаются.
    /// </summary>
    /// <param name="fragmentAnalysisId">Секущая постановка фрагмента, по которой строится родитель; null — по всем секущим
    /// постановкам фрагмента (они должны продолжать одна другую).</param>
    public static FemAnalysis? PrepareSecant(DatabaseService db, FemSchema fragment, out string? reason, int? fragmentAnalysisId = null)
    {
        if (SecantParams(db, fragment, fragmentAnalysisId, out reason, out int parentId) is not { } pars) return null;
        return Save(db, parentId, SecantAnalysisTag, FemCsfeaRunner.AnalysisKind, pars.Stages[0].LoadExpressionJson, pars.ToJson());
    }

    /// <summary>Служебную секущую постановку можно построить (без записи в БД); иначе <paramref name="reason"/> — почему.</summary>
    public static bool CanPrepareSecant(DatabaseService db, FemSchema fragment, int? fragmentAnalysisId, out string? reason) =>
        SecantParams(db, fragment, fragmentAnalysisId, out reason, out _) != null;

    /// <summary>Параметры служебной секущей постановки родителя; null — отказ с причиной.</summary>
    static FemAnalysisParams? SecantParams(DatabaseService db, FemSchema fragment, int? fragmentAnalysisId, out string? reason,
        out int parentId)
    {
        parentId = 0;
        if (!Context(db, fragment, out var ctx, out reason)) return null;
        parentId = ctx.ParentId;
        var inverse = ctx.Map.ToDictionary(kv => kv.Value, kv => kv.Key);
        var variants = new List<(FemAnalysis Analysis, FemAnalysisParams Pars, List<FemAnalysisStage> Stages)>();
        foreach (var a in SecantAnalyses(db, fragment).Where(a => fragmentAnalysisId == null || a.Id == fragmentAnalysisId))
        {
            var pars = FemAnalysisParams.Parse(a.ParamsJson);
            if (pars.Batch != null)
            {
                reason = Loc.S("StoreyFragmentMasksSecantBatch");
                return null;
            }
            var stages = new List<FemAnalysisStage>();
            foreach (var s in pars.ResolveStages(a))
            {
                if (!TranslateExpression(s.LoadExpressionJson, inverse, out string parentJson))
                {
                    reason = Loc.S("StoreyFragmentMasksSecantNoParentCase");
                    return null;
                }
                stages.Add(new FemAnalysisStage
                {
                    Tag = s.Tag, LoadExpressionJson = parentJson, LoadFactorStep = s.LoadFactorStep, MaxLoadFactor = s.MaxLoadFactor,
                });
            }
            variants.Add((a, pars, stages));
        }
        if (variants.Count == 0)
        {
            reason = Loc.S("StoreyFragmentMasksNoFragmentSecant");
            return null;
        }
        var longest = variants.MaxBy(v => v.Stages.Count);
        foreach (var v in variants)
        {
            int k = v.Stages.Zip(longest.Stages).TakeWhile(p => p.First.LoadExpressionJson == p.Second.LoadExpressionJson).Count();
            if (k == v.Stages.Count) continue;
            // Постановки расходятся: служебный родитель — один набор стадий. Назвать, какие постановки и где, и отдельно —
            // крен: родитель с креном и без даёт разные маски, одним расчётом их не получить.
            bool impV = v.Stages.Any(s => HasImperfection(s.LoadExpressionJson, ctx.ParentLoadCases));
            bool impL = longest.Stages.Any(s => HasImperfection(s.LoadExpressionJson, ctx.ParentLoadCases));
            string imperfection = impV == impL ? ""
                : string.Format(Loc.S("StoreyFragmentMasksSecantImperfection", SecantImperfectionFallback), impV ? v.Analysis.Tag : longest.Analysis.Tag,
                    impV ? longest.Analysis.Tag : v.Analysis.Tag);
            reason = string.Format(Loc.S("StoreyFragmentMasksSecantNotPrefix", SecantNotPrefixFallback), longest.Analysis.Tag, v.Analysis.Tag, k + 1,
                v.Stages[k].Tag, imperfection);
            return null;
        }
        // Родитель для масок: концы всех стадий записываются (ключ маски — стадия), второй порядок — полный (P-Δ каркаса;
        // «только P-δ» и флажок масок первого порядка — настройки фрагмента, у родителя не нужны).
        var csfea = FemAnalysisParams.Parse(longest.Pars.ToJson()).Csfea ?? new FemCsfeaParams();
        csfea.SecondOrderScope = FemCsfeaSecondOrderScope.Full;
        csfea.FragmentSecondOrderOnFirstOrderMasks = false;
        if (csfea.ResultRecording == FemCsfeaRecording.Final)
        {
            csfea.ResultRecording = FemCsfeaRecording.Selected;
            csfea.RecordSteps = "";
        }
        csfea.RecordStageEnds = true;
        return new FemAnalysisParams { Stages = longest.Stages, CalcType = longest.Pars.CalcType, Csfea = csfea };
    }

    // Форматы отказа без словаря строк (тесты, ранний старт) — те же, что в Strings.ru-RU.xaml.
    const string SecantNotPrefixFallback = "Секущие постановки фрагмента «{0}» и «{1}» расходятся на стадии {2} («{3}»){4}. " +
        "Служебный расчёт родителя для масок — один набор стадий: выберите в окне масок постановку фрагмента, по которой его считать.";
    const string SecantImperfectionFallback = ": «{0}» — с креном, «{1}» — без, а крен меняет перемещения и усилия родителя";

    /// <summary>Секущие (нелинейные) постановки CSfea фрагмента — варианты для служебного секущего расчёта родителя.</summary>
    public static IReadOnlyList<FemAnalysis> SecantAnalyses(DatabaseService db, FemSchema fragment)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(fragment);
        return db.GetFemAnalyses(fragment.Id).Where(a => a.Kind == FemCsfeaRunner.AnalysisKind).ToList();
    }

    /// <summary>Выражение загружения ссылается на загружение-несовершенство (крен).</summary>
    static bool HasImperfection(string json, IReadOnlyList<FemLoadCase> loadCases)
    {
        var e = FemLoadExpression.Parse(json);
        var ids = e.LoadCaseIds.Concat(e.Terms.Select(t => t.LoadCaseId)).ToHashSet();
        return loadCases.Any(c => ids.Contains(c.Id) && FemSp20Types.IsImperfection(c));
    }

    /// <summary>Общая проверка: фрагмент, родитель и карта загружений родитель → фрагмент.</summary>
    static bool Context(DatabaseService db, FemSchema fragment, out Ctx ctx, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(fragment);
        ctx = null!;
        reason = null;
        if (fragment.ParentSchemaId is not int parentId || fragment.FragmentZ1 is null || fragment.FragmentZ2 is null)
        {
            reason = Loc.S("StoreyFragmentMasksNotFragment");
            return false;
        }
        if (db.GetFemSchemaSourceType(parentId) is null)
        {
            reason = Loc.S("StoreyFragmentMasksParentDeleted");
            return false;
        }
        var parentLoadCases = db.GetFemLoadCases(parentId);
        var map = FragmentMaskSourceKeys.LoadCaseMap(parentLoadCases, db.GetFemLoadCases(fragment.Id), []);
        ctx = new Ctx(parentId, parentLoadCases, map);
        return true;
    }

    /// <summary>Перевод выражения загружения фрагмента на Id загружений родителя; false — загружения нет у родителя.</summary>
    static bool TranslateExpression(string json, IReadOnlyDictionary<int, int> inverse, out string parentJson)
    {
        var e = FemLoadExpression.Parse(json);
        var ids = e.LoadCaseIds.Select(id => inverse.TryGetValue(id, out int p) ? (int?)p : null).ToList();
        var terms = e.Terms.Select(t => inverse.TryGetValue(t.LoadCaseId, out int p) ? (int?)p : null).ToList();
        if (ids.Any(i => i == null) || terms.Any(i => i == null))
        {
            parentJson = "";
            return false;
        }
        parentJson = new FemLoadExpression
        {
            Mode = e.Mode, LoadCaseIds = ids.Select(i => i!.Value).ToList(), CombinationType = e.CombinationType,
            Terms = e.Terms.Select((t, i) => new FemLoadTerm { LoadCaseId = terms[i]!.Value, Coefficient = t.Coefficient, Role = t.Role, Mode = t.Mode })
                .ToList(),
        }.ToJson();
        return true;
    }

    /// <summary>Найти служебную постановку по имени и виду или создать; стадии и параметры записываются целиком.</summary>
    static FemAnalysis Save(DatabaseService db, int parentId, string tag, string kind, string loadExpressionJson, string paramsJson)
    {
        var analysis = db.GetFemAnalyses(parentId).FirstOrDefault(a => a.Kind == kind && a.Tag == tag)
            ?? new FemAnalysis { SchemaId = parentId, Tag = tag, Kind = kind };
        analysis.ParamsJson = paramsJson;
        analysis.LoadExpressionJson = loadExpressionJson;
        db.SaveFemAnalysis(analysis);
        return analysis;
    }

    /// <summary>Контекст родителя: Id, загружения и карта родитель → фрагмент.</summary>
    sealed record Ctx(int ParentId, IReadOnlyList<FemLoadCase> ParentLoadCases, IReadOnlyDictionary<int, int> Map);
}
