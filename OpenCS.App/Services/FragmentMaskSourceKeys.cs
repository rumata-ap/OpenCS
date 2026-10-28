using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using OpenCS.Tasks;

namespace OpenCS.Services;

/// <summary>Шаг результата родителя, по которому снимается маска ключа: <see cref="Label"/> — тег стадии (для отчёта).</summary>
public sealed record FragmentMaskSourceStep(FragmentMaskKey Key, int StepN, string Label);

/// <summary>
/// Ключи масок по сохранённому результату CSfea родителя (спека 2026-10-28, разд. 4.5); ключи — уже в Id загружений
/// фрагмента (карта <see cref="LoadCaseMap"/>):
/// <list type="bullet">
/// <item>линейный — стадия из одного загружения с коэффициентом 1 (после множителя стадии) → загружение;</item>
/// <item>секущий — последний сошедшийся шаг стадии с полями; стадии накапливаются, поэтому ключ — сочетание
/// Σ предыдущих стадий + λ·стадия на Id фрагмента (<see cref="StageKey"/>).</item>
/// </list>
/// Пропуски — предупреждениями, исключений по данным нет.
/// </summary>
public static class FragmentMaskSourceKeys
{
    public static List<FragmentMaskSourceStep> Select(FemCsfeaResultSummary summary, IReadOnlyList<FemAnalysisStage> stages,
        IReadOnlyList<FemLoadCase> parentLoadCases, IReadOnlyDictionary<int, int> loadCaseMap, List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var result = new List<FragmentMaskSourceStep>();
        if (summary.IsBatch)
        {
            warnings.Add("Маски по пакетному расчёту не снимаются: варианты пакета — независимые сочетания; выберите линейный " +
                "расчёт или секущий по стадиям.");
            return result;
        }
        bool linear = summary.Kind == FemCsfeaRunner.LinearAnalysisKind;
        var diag = new List<FemValidationDiagnostic>();
        var rcStages = FemCsfeaInputBuilder.Stages(stages, parentLoadCases, diag);
        var tagById = parentLoadCases.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Tag);
        string LcTag(int id) => tagById.TryGetValue(id, out var t) ? t : $"#{id}";
        var used = new HashSet<int>();
        var accumulated = new Dictionary<int, double>();   // загружение родителя → коэффициент (секущий)
        for (int i = 0; i < rcStages.Count; i++)
        {
            string tag = i < summary.Stages.Count ? summary.Stages[i].Tag : stages[i].Tag;
            var terms = rcStages[i].Terms;
            var before = new Dictionary<int, double>(accumulated);
            foreach (var (lc, k) in terms) accumulated[lc] = accumulated.GetValueOrDefault(lc) + k;
            if (terms.Count == 0)
            {
                warnings.Add($"Стадия «{tag}»: выражение загружений не разбирается — пропущена.");
                continue;
            }
            var step = summary.Steps.Where(s => s.Stage == i && s.Converged && s.Fields).MaxBy(s => s.N);

            if (linear)
            {
                if (terms.Count != 1 || Math.Abs(terms[0].Factor - 1) > 1e-12)
                {
                    warnings.Add($"Стадия «{tag}»: не одно загружение схемы — пропущена (маски линейного расчёта — по загружениям).");
                    continue;
                }
                int lc = terms[0].LoadCaseId;
                if (!loadCaseMap.TryGetValue(lc, out int fragmentLc))
                {
                    warnings.Add($"Стадия «{tag}»: загружения «{LcTag(lc)}» нет во фрагменте — пропущена.");
                    continue;
                }
                if (!used.Add(fragmentLc))
                {
                    warnings.Add($"Загружение «{LcTag(lc)}» встречается в постановке повторно — взята первая стадия.");
                    continue;
                }
                if (step == null)
                {
                    warnings.Add($"Стадия «{tag}»: нет сошедшегося шага с записанными полями — пропущена.");
                    continue;
                }
                result.Add(new(FragmentMaskKey.OfLoadCase(fragmentLc), step.N, tag));
                continue;
            }

            if (step == null)
            {
                warnings.Add($"Стадия «{tag}»: нет сошедшегося шага с записанными полями — пропущена.");
                continue;
            }
            var combination = new Dictionary<int, double>(before);
            foreach (var (lc, k) in terms) combination[lc] = combination.GetValueOrDefault(lc) + step.LoadFactor * k;
            var missing = combination.Keys.Where(lc => !loadCaseMap.ContainsKey(lc)).Select(LcTag).ToList();
            if (missing.Count > 0)
            {
                warnings.Add($"Стадия «{tag}»: загружений {string.Join(", ", missing.Select(m => $"«{m}»"))} нет во фрагменте — пропущена.");
                continue;
            }
            if (step.LoadFactor < 1 - 1e-9)
                warnings.Add(string.Create(CultureInfo.CurrentCulture,
                    $"Стадия «{tag}»: последний сошедшийся шаг с полями — λ = {step.LoadFactor:0.###}; маска — на эту долю стадии."));
            result.Add(new(StageKey(combination.Select(kv => (loadCaseMap[kv.Key], kv.Value))), step.N, tag));
        }
        return result;
    }

    /// <summary>Ключ сочетания в каноническом виде (<see cref="FragmentMaskKey.OfStageTerms"/>).</summary>
    public static FragmentMaskKey StageKey(IEnumerable<(int LoadCaseId, double Factor)> terms) => FragmentMaskKey.OfStageTerms(terms);

    /// <summary>
    /// Карта загружений родителя → фрагмента. Построитель копирует все загружения родителя по порядку (Id не хранятся):
    /// при том же числе и тех же тегах по порядку — по позиции; иначе по однозначному тегу (двойники — предупреждение).
    /// </summary>
    public static Dictionary<int, int> LoadCaseMap(IReadOnlyList<FemLoadCase> parent, IReadOnlyList<FemLoadCase> fragment,
        List<string> warnings)
    {
        var map = new Dictionary<int, int>();
        if (parent.Count == fragment.Count && parent.Zip(fragment).All(p => p.First.Tag == p.Second.Tag))
        {
            for (int i = 0; i < parent.Count; i++) map[parent[i].Id] = fragment[i].Id;
            return map;
        }
        var byTag = fragment.GroupBy(c => c.Tag, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var parentTags = parent.GroupBy(c => c.Tag, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var lc in parent)
        {
            if (!byTag.TryGetValue(lc.Tag, out var copies))
                warnings.Add($"Загружения «{lc.Tag}» родителя нет во фрагменте.");
            else if (copies.Count > 1 || parentTags[lc.Tag] > 1)
                warnings.Add($"Загружение «{lc.Tag}» сопоставляется с фрагментом неоднозначно (повтор имени) — пропущено.");
            else
                map[lc.Id] = copies[0].Id;
        }
        return map;
    }
}
