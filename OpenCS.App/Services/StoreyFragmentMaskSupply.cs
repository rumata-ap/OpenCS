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
    public static FemAnalysis? PrepareSecant(DatabaseService db, FemSchema fragment, out string? reason)
    {
        if (!Context(db, fragment, out var ctx, out reason)) return null;
        var inverse = ctx.Map.ToDictionary(kv => kv.Value, kv => kv.Key);
        var variants = new List<(FemAnalysis Analysis, FemAnalysisParams Pars, List<FemAnalysisStage> Stages)>();
        foreach (var a in db.GetFemAnalyses(fragment.Id).Where(a => a.Kind == FemCsfeaRunner.AnalysisKind))
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
        bool prefixes = variants.All(v => v.Stages.Count <= longest.Stages.Count
            && v.Stages.Zip(longest.Stages).All(p => p.First.LoadExpressionJson == p.Second.LoadExpressionJson));
        if (!prefixes)
        {
            reason = Loc.S("StoreyFragmentMasksSecantNotPrefix");
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
        var pSecant = new FemAnalysisParams { Stages = longest.Stages, CalcType = longest.Pars.CalcType, Csfea = csfea };
        return Save(db, ctx.ParentId, SecantAnalysisTag, FemCsfeaRunner.AnalysisKind, longest.Stages[0].LoadExpressionJson,
            pSecant.ToJson());
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
