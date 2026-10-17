using CScore.Combinations;

namespace CScore.Fem.Combinations;

/// <summary>Вид длительной части комбинации.</summary>
public enum FemLongTermPairKind
{
    /// <summary>Нагрузка отличается от полной — нужен свой прогон.</summary>
    Derived,
    /// <summary>Нагрузка равна полной (нет кратковременных и особых или у всех доля 1).</summary>
    SameAsTotal,
    /// <summary>Длительная часть нулевая — прогонов нет.</summary>
    Zero,
    /// <summary>Формула ссылается на загружение, которого нет в схеме, — пары нет.</summary>
    Invalid
}

/// <summary>Длительная часть комбинации: формула NL/CL, вид и замечания.</summary>
public sealed record FemLongTermPairResult(FemLoadExpression? Long, FemLongTermPairKind Kind, IReadOnlyList<string> Notes);

/// <summary>
/// Длительная часть комбинации N (C) пакета CSfea (спека 2026-10-17, п. 2.1): k_long = k_total · s, где s = 1 у
/// постоянных и длительных, <see cref="FemLoadCase.LongTermShare"/> (null → 0) у кратковременных, 0 у особых. У
/// «авто»-слагаемых совпадает с <see cref="FemCombinationCoefficients.Recompute"/> под группу NL (CL).
/// </summary>
public static class FemLongTermPair
{
    /// <summary>Тип длительной части для типа полной комбинации: N → NL, C → CL; иначе null.</summary>
    public static string? LongType(string? totalType) => totalType?.Trim() switch
    {
        FemCombinationTypes.N => FemCombinationTypes.NL,
        FemCombinationTypes.C => FemCombinationTypes.CL,
        _ => null
    };

    /// <summary>Множитель длительной части загружения.</summary>
    public static double Share(FemLoadCase loadCase) => FemCombinationCoefficients.LoadType(loadCase) switch
    {
        NormLoadType.Permanent or NormLoadType.LongTerm => 1,
        NormLoadType.Accidental => 0,
        _ => loadCase.LongTermShare ?? 0
    };

    /// <summary>Длительная часть формулы <paramref name="total"/> типа N или C.</summary>
    public static FemLongTermPairResult Derive(FemLoadExpression total, IReadOnlyList<FemLoadCase> loadCases, string totalType)
    {
        ArgumentNullException.ThrowIfNull(total);
        ArgumentNullException.ThrowIfNull(loadCases);
        string longType = LongType(totalType)
            ?? throw new ArgumentException($"Длительная часть строится только у комбинаций N и C, а не «{totalType}».", nameof(totalType));

        var byId = loadCases.ToDictionary(lc => lc.Id);
        var notes = new List<string>();
        var noted = new HashSet<int>();
        var terms = new List<FemLoadTerm>();
        bool same = true, anyNonZero = false;

        foreach (var t in TotalTerms(total, loadCases))
        {
            if (!byId.TryGetValue(t.LoadCaseId, out var lc))
            {
                notes.Add($"В формуле загружение № {t.LoadCaseId}, которого нет в схеме, — длительная часть не строится");
                return new FemLongTermPairResult(null, FemLongTermPairKind.Invalid, notes);
            }
            double s = Share(lc);
            if (FemCombinationCoefficients.LoadType(lc) == NormLoadType.ShortTerm && lc.LongTermShare == null
                && t.Coefficient != 0 && noted.Add(lc.Id))
                notes.Add($"{lc.Tag}: доля длительной части не задана — в {longType} не входит (0)");
            double k = t.Coefficient * s;
            if (t.Coefficient != 0 && s != 1) same = false;
            if (k != 0) anyNonZero = true;
            terms.Add(new FemLoadTerm { LoadCaseId = t.LoadCaseId, Coefficient = k, Role = t.Role, Mode = t.Mode });
        }

        var kind = !anyNonZero ? FemLongTermPairKind.Zero : same ? FemLongTermPairKind.SameAsTotal : FemLongTermPairKind.Derived;
        var expr = new FemLoadExpression
        {
            Mode = total.Mode == FemLoadExpressionMode.Sp20 ? FemLoadExpressionMode.Sp20 : FemLoadExpressionMode.Sum,
            Terms = terms,
            CombinationType = longType,
        };
        return new FemLongTermPairResult(expr, kind, notes);
    }

    /// <summary>Слагаемые полной формулы с коэффициентами, как их читает решатель (<see cref="FemLoadExpressionResolver"/>).</summary>
    static IEnumerable<FemLoadTerm> TotalTerms(FemLoadExpression e, IReadOnlyList<FemLoadCase> cases)
    {
        if (e.Mode == FemLoadExpressionMode.All)
            return cases.Select(c => new FemLoadTerm { LoadCaseId = c.Id, Coefficient = 1 });
        if (e.Terms.Count > 0 && (e.Mode != FemLoadExpressionMode.Single || e.LoadCaseIds.Count == 0))
            return e.Terms;
        if (e.LoadCaseIds.Count > 0)
            return e.LoadCaseIds.Select(id => new FemLoadTerm { LoadCaseId = id, Coefficient = 1 });
        return e.Mode == FemLoadExpressionMode.Single && cases.Count > 0
            ? [new FemLoadTerm { LoadCaseId = cases[0].Id, Coefficient = 1 }]
            : [];
    }
}
