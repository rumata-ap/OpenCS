using CScore.Fem;
using CScore.Fem.Combinations;
using OpenCS.Tasks;

namespace OpenCS.Services;

/// <summary>Оценка длительных прогонов пакета без расчёта (спека 2026-10-17 «длительные пары», п. 2.3).</summary>
/// <param name="Pairs">Прогонов длительных частей (вид Derived).</param>
/// <param name="SameAsTotal">Длительных частей, совпадающих с полной нагрузкой, — без прогона.</param>
/// <param name="Zero">Нулевых длительных частей — без прогона.</param>
/// <param name="Invalid">Комбинаций, у которых длительная часть не строится.</param>
/// <param name="Deflections">Прогонов на диаграммах NL.</param>
public sealed record FemCsfeaBatchLongTermEstimate(int Pairs, int SameAsTotal, int Zero, int Invalid, int Deflections)
{
    public int Runs => Pairs + Deflections;
}

/// <summary>Длительная часть варианта <paramref name="Item"/> пакета; <paramref name="Stage"/> — её стадия адаптера (только Derived).</summary>
public sealed record FemCsfeaBatchPairPlan(int Item, FemLongTermPairResult Pair, int? Stage);

/// <summary>Длительные части вариантов пакета CSfea: N → NL, C → CL по <see cref="FemLongTermPair.Derive"/>.</summary>
public static class FemCsfeaBatchLongTerm
{
    /// <summary>Длительные части вариантов по порядку; стадии Derived нумеруются после стадий вариантов.</summary>
    public static IReadOnlyList<FemCsfeaBatchPairPlan> Plans(FemCsfeaBatch batch, IReadOnlyList<FemLoadCase> loadCases)
    {
        var plans = new List<FemCsfeaBatchPairPlan>();
        int stage = batch.Items.Count;
        for (int i = 0; i < batch.Items.Count; i++)
            if (Plan(batch.Items[i], loadCases, batch) is { } pair)
                plans.Add(new FemCsfeaBatchPairPlan(i, pair, pair.Kind == FemLongTermPairKind.Derived ? stage++ : null));
        return plans;
    }

    /// <summary>Стадии адаптера: варианты (<see cref="FemCsfeaBatch.ToStages"/>), затем длительные части Derived.</summary>
    public static IReadOnlyList<FemAnalysisStage> Stages(FemCsfeaBatch batch, IReadOnlyList<FemLoadCase> loadCases)
    {
        var stages = batch.ToStages().ToList();
        foreach (var plan in Plans(batch, loadCases).Where(p => p.Stage != null))
            stages.Add(new FemAnalysisStage
            {
                Tag = PairTag(batch.Items[plan.Item].Tag, plan.Pair.Long!.CombinationType),
                LoadExpressionJson = plan.Pair.Long.ToJson(),
                LoadFactorStep = batch.LoadFactorStep, MaxLoadFactor = batch.MaxLoadFactor,
            });
        return stages;
    }

    /// <summary>«Огибающая N 012 — длительная часть NL».</summary>
    public static string PairTag(string variantTag, string? longType) => $"{variantTag} — длительная часть {longType}";

    /// <summary>Длительная часть варианта; null — у варианта её нет (загружение, другой тип, тип не отмечен).</summary>
    public static FemLongTermPairResult? Plan(FemCsfeaBatchItem item, IReadOnlyList<FemLoadCase> loadCases, FemCsfeaBatch batch)
    {
        if (item.Kind != FemCsfeaBatchItemKind.Combination || FemLongTermPair.LongType(item.Type) == null
            || !batch.HasLongTermPairs(item.Type)) return null;
        return FemLongTermPair.Derive(FemLoadExpression.Parse(item.LoadExpressionJson), loadCases, item.Type);
    }

    /// <summary>Нужен ли варианту прогон на диаграммах NL.</summary>
    public static bool NeedsDeflection(FemCsfeaBatchItem item, FemLongTermPairResult? pair, FemCsfeaBatch batch) =>
        batch.HasLongTermDeflection && item.Type == FemCombinationTypes.N
        && pair?.Kind is FemLongTermPairKind.Derived or FemLongTermPairKind.SameAsTotal;

    public static FemCsfeaBatchLongTermEstimate Estimate(FemCsfeaBatch batch, IReadOnlyList<FemLoadCase> loadCases)
    {
        int pairs = 0, same = 0, zero = 0, invalid = 0, deflections = 0;
        foreach (var item in batch.Items)
        {
            var pair = Plan(item, loadCases, batch);
            switch (pair?.Kind)
            {
                case FemLongTermPairKind.Derived: pairs++; break;
                case FemLongTermPairKind.SameAsTotal: same++; break;
                case FemLongTermPairKind.Zero: zero++; break;
                case FemLongTermPairKind.Invalid: invalid++; break;
            }
            if (NeedsDeflection(item, pair, batch)) deflections++;
        }
        return new FemCsfeaBatchLongTermEstimate(pairs, same, zero, invalid, deflections);
    }

    /// <summary>Есть ли в пакете комбинации типа <paramref name="type"/> (для доступности флажка).</summary>
    public static bool HasCombinations(IEnumerable<FemCsfeaBatchItem> items, string type) =>
        items.Any(i => i.Kind == FemCsfeaBatchItemKind.Combination && i.Type == type);
}
