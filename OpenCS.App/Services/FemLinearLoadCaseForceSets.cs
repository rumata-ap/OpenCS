using CScore;
using CScore.Combinations;
using CScore.Fem;
using CScore.Fem.Combinations;
using CSfea.CScoreBridge.Structural;
using OpenCS.Tasks;

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
    /// шага стадии с записанными полями. Имена — «Q(γf=1.4): ЗГ2 — расчёт — пластины [группа]».
    /// </summary>
    public static Result Build(FemSchema schema, FemAnalysis analysis, FemCsfeaResultSummary summary,
        Func<int, RcSecantStepFields?> loadFields, IReadOnlyCollection<ForceSet> existing, Sp20GammaDefaults defaults)
    {
        var sets = new List<(ForceSet, FemLoadCase)>();
        var warnings = new List<string>();
        if (summary.Kind != FemCsfeaRunner.LinearAnalysisKind)
        {
            warnings.Add($"Постановка «{analysis.Tag}» не линейная: наборы по загружениям строятся только по линейному расчёту.");
            return new Result(sets, warnings);
        }

        var stages = FemAnalysisParams.Parse(analysis.ParamsJson).ResolveStages(analysis);
        var byId = schema.LoadCases.ToDictionary(lc => lc.Id);
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
                set.Tag = FemEnvelopeCombinations.ForceSetTag(loadCase, $"{loadCase.Tag} — {analysis.Tag} — {part}", defaults);
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
    public static IReadOnlyList<FemLoadDefinition> EnvelopeDefinitions(FemSchema schema, Result result,
        Sp20GammaDefaults defaults, Sp20LoadValueKind valueKind, List<string> warnings)
    {
        var combinations = FemEnvelopeCombinations.FromLoadCaseForceSets(result.Sets, defaults, warnings);
        return FemLoadDefinitionFactory.FromCombinations(schema, schema.LoadCases, combinations, "envelope",
            FemLoadExpressionMode.Sum, defaults, valueKind);
    }
}
