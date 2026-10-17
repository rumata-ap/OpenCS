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

/// <summary>Длительные части вариантов пакета CSfea: N → NL, C → CL по <see cref="FemLongTermPair.Derive"/>.</summary>
public static class FemCsfeaBatchLongTerm
{
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
