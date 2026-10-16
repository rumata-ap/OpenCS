using CScore;
using CScore.Combinations;
using CScore.Fem;
using CScore.Fem.Combinations;
using CSfea.CScoreBridge.Structural;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Линейный расчёт CSfea — комбинирование усилий (спека 2026-10-14, п. 5.2.3–5.2.4): наборы усилий по всем
/// загружениям постановки одним пакетом с атрибутами СП 20 в имени (их комбинирует <see cref="SP20Combinations"/> и
/// диалог СП 20) и комбинации нагрузок из огибающей — для нелинейного расчёта.
/// </summary>
public static class FemLinearLoadCaseForceSets
{
    /// <summary>Наборы по загружениям (пластины и стержни на загружение) и замечания.</summary>
    public sealed record Result(List<(ForceSet Set, FemLoadCase LoadCase)> Sets, List<string> Warnings);

    /// <summary>Загружение стадии: одно загружение схемы с коэффициентом 1; иначе null (комбинация — не загружение).</summary>
    public static int? StageLoadCaseId(FemLoadExpression expression)
    {
        if (expression.Terms.Count == 0)
            return expression.Mode == FemLoadExpressionMode.Single && expression.LoadCaseIds.Count == 1
                ? expression.LoadCaseIds[0] : null;
        return expression.Terms.Count == 1 && expression.Terms[0].Coefficient == 1 ? expression.Terms[0].LoadCaseId : null;
    }

    /// <summary>
    /// Наборы по стадиям линейной постановки, каждая из которых — одно загружение схемы: поля последнего сошедшегося
    /// шага стадии с записанными полями. Имена — «Q(γf=1.4): ЗГ2 — расчёт — пластины [группа]»; занятое имя
    /// получает номер после заголовка («… — пластины (2) [группа]»).
    /// </summary>
    /// <param name="loadCases">Загружения с текущими атрибутами (редактор схемы); null — загружения схемы.</param>
    public static Result Build(FemSchema schema, FemAnalysis analysis, FemCsfeaResultSummary summary,
        Func<int, RcSecantStepFields?> loadFields, IReadOnlyCollection<ForceSet> existing, Sp20GammaDefaults defaults,
        IReadOnlyList<FemLoadCase>? loadCases = null)
    {
        var sets = new List<(ForceSet, FemLoadCase)>();
        var warnings = new List<string>();
        if (summary.Kind != FemCsfeaRunner.LinearAnalysisKind)
        {
            warnings.Add($"Постановка «{analysis.Tag}» не линейная: наборы по загружениям строятся только по линейному расчёту.");
            return new Result(sets, warnings);
        }

        var stages = FemAnalysisParams.Parse(analysis.ParamsJson).ResolveStages(analysis);
        var byId = (loadCases ?? schema.LoadCases).GroupBy(lc => lc.Id).ToDictionary(g => g.Key, g => g.First());
        var tags = existing.Select(fs => fs.Tag).ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<int>();
        var numbered = existing.ToList();
        for (int i = 0; i < stages.Count; i++)
        {
            string stageTag = i < summary.Stages.Count ? summary.Stages[i].Tag : stages[i].Tag;
            if (StageLoadCaseId(FemLoadExpression.Parse(stages[i].LoadExpressionJson)) is not { } id
                || !byId.TryGetValue(id, out var loadCase))
            {
                warnings.Add($"Строка «{stageTag}»: не одно загружение схемы — пропущена.");
                continue;
            }
            if (!used.Add(id))
            {
                warnings.Add($"Загружение «{loadCase.Tag}» встречается в постановке повторно — взята первая строка.");
                continue;
            }
            var step = summary.Steps.Where(s => s.Stage == i && s.Converged && s.Fields).MaxBy(s => s.N);
            if (step == null || loadFields(step.N) is not { } fields)
            {
                warnings.Add($"Загружение «{loadCase.Tag}»: нет сошедшегося шага с записанными полями.");
                continue;
            }
            foreach (var set in FemCsfeaForceSetBuilder.Build(schema, analysis.Tag, step, loadCase.Tag, fields, numbered))
            {
                string part = set.Kind == "shell" ? "пластины" : "стержни";
                string title = $"{loadCase.Tag} — {analysis.Tag} — {part}";
                set.Tag = FemEnvelopeCombinations.ForceSetTag(loadCase, title, defaults);
                for (int n = 2; tags.Contains(set.Tag); n++)
                    set.Tag = FemEnvelopeCombinations.ForceSetTag(loadCase, $"{title} ({n})", defaults);
                tags.Add(set.Tag);
                numbered.Add(set);
                sets.Add((set, loadCase));
            }
        }
        return new Result(sets, warnings);
    }

    /// <summary>
    /// «Комбинации из огибающей линейного расчёта»: уникальные формулы огибающей усилий по наборам загружений —
    /// определения схемы с «авто»-слагаемыми (тип C, при особых загружениях и A).
    /// </summary>
    /// <param name="loadCases">Загружения с текущими атрибутами (редактор схемы); null — загружения схемы.</param>
    public static IReadOnlyList<FemLoadDefinition> EnvelopeDefinitions(FemSchema schema, Result result,
        Sp20GammaDefaults defaults, Sp20LoadValueKind valueKind, List<string> warnings,
        IReadOnlyList<FemLoadCase>? loadCases = null)
    {
        var combinations = FemEnvelopeCombinations.FromLoadCaseForceSets(result.Sets, defaults, warnings);
        return FemLoadDefinitionFactory.FromCombinations(schema, loadCases ?? schema.LoadCases, combinations, "envelope",
            FemLoadExpressionMode.Sum, defaults, valueKind);
    }

    /// <summary>Последняя линейная постановка CSfea схемы с сохранённым результатом; null — такой нет.</summary>
    public static FemAnalysis? LatestLinear(IEnumerable<FemAnalysis> analyses) => analyses
        .Where(a => a.Kind == FemCsfeaRunner.LinearAnalysisKind && a.ResultId is > 0
            && a.Status is "ok" or "not_converged" or "partial")
        .OrderByDescending(a => a.Id)
        .FirstOrDefault();

    /// <summary>
    /// <see cref="Build"/> по сохранённому результату постановки: сводка — из результата, поля шагов — из БД.
    /// null — результат не найден или повреждён.
    /// </summary>
    public static Result? BuildFromDatabase(DatabaseService db, FemSchema schema, FemAnalysis analysis,
        IReadOnlyCollection<ForceSet> existing, Sp20GammaDefaults defaults, IReadOnlyList<FemLoadCase>? loadCases = null)
    {
        if (analysis.ResultId is not int resultId || db.GetCalcResultById(resultId) is not { } result
            || FemCsfeaResultSummary.Parse(result.DataJson) is not { } summary) return null;
        RcSecantStepFields? Fields(int stepN)
        {
            if (db.GetFemResultStep(resultId, stepN) is not { } data) return null;
            try { return FemCsfeaStepFieldsCodec.Unpack(data); }
            catch (Exception ex) when (ex is System.IO.InvalidDataException or System.IO.EndOfStreamException) { return null; }
        }
        return Build(schema, analysis, summary, Fields, existing, defaults, loadCases);
    }
}
