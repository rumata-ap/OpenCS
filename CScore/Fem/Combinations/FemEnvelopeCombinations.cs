using System.Globalization;
using CScore.Combinations;

namespace CScore.Fem.Combinations;

/// <summary>
/// Линейный расчёт — комбинирование усилий (спека 2026-10-14, п. 5.2.3–5.2.4): наборы усилий по загружениям с
/// атрибутами СП 20 в имени и комбинации нагрузок из формул, определяющих огибающую усилий.
/// </summary>
public static class FemEnvelopeCombinations
{
    /// <summary>
    /// Имя набора усилий загружения в формате <see cref="SP20Combinations.ParseForceSetName"/>:
    /// «Q(γf=1.4): <paramref name="title"/> [группа]»; γf — загружения или по умолчанию.
    /// </summary>
    public static string ForceSetTag(FemLoadCase loadCase, string title, Sp20GammaDefaults defaults)
    {
        string prefix = FemCombinationCoefficients.LoadType(loadCase) switch
        {
            NormLoadType.Permanent => "G",
            NormLoadType.LongTerm => "L",
            NormLoadType.Accidental => "A",
            _ => "Q"
        };
        string gamma = FemCombinationCoefficients.GammaF(loadCase, defaults).ToString("0.###", CultureInfo.InvariantCulture);
        string group = string.IsNullOrWhiteSpace(loadCase.Sp20Group) ? "" : $" [{loadCase.Sp20Group.Trim()}]";
        return $"{prefix}(γf={gamma}): {title}{group}";
    }

    /// <summary>
    /// Уникальные формулы огибающей усилий по наборам загружений (наборы одного вида — пластины или стержни —
    /// комбинируются вместе): основное сочетание C, при особых загружениях — и особое A. Слагаемые — с ролями.
    /// </summary>
    public static IReadOnlyList<FemLoadCombination> FromLoadCaseForceSets(
        IReadOnlyList<(ForceSet Set, FemLoadCase LoadCase)> sets, Sp20GammaDefaults defaults, List<string> warnings,
        string tagPrefix = "Огибающая")
    {
        var unique = new Dictionary<string, FemLoadCombination>(StringComparer.Ordinal);
        bool hasAccidental = sets.Any(s => FemCombinationCoefficients.LoadType(s.LoadCase) == NormLoadType.Accidental);
        foreach (var group in sets.GroupBy(s => s.Set.Kind))
        {
            var items = group.ToList();
            var caseIdByTitle = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (set, loadCase) in items)
                caseIdByTitle[SP20Combinations.ParseForceSetName(set.Tag).Title] = loadCase.Id;
            var combTypes = hasAccidental ? new[] { CombType.Fundamental, CombType.Accidental } : [CombType.Fundamental];
            foreach (var combType in combTypes)
            {
                List<GeneratedCase> cases;
                try
                {
                    var (_, generated, _, w) = SP20Combinations.SP20EnvelopeAndCasesFromForceSets(
                        items.Select(i => i.Set).ToList(), combType, defaults);
                    warnings.AddRange(w);
                    cases = generated;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    warnings.Add(ex.Message);
                    continue;
                }

                string type = combType == CombType.Accidental ? FemCombinationTypes.A : FemCombinationTypes.C;
                foreach (var generatedCase in cases)
                {
                    var coefficients = new SortedDictionary<int, double>();
                    var roles = new Dictionary<int, Sp20TermRole>();
                    foreach (var (name, coefficient) in generatedCase.Active)
                    {
                        if (!caseIdByTitle.TryGetValue(name, out int caseId)) continue;
                        coefficients[caseId] = coefficient;
                        if (generatedCase.Roles.TryGetValue(name, out var role)) roles[caseId] = role;
                    }
                    if (coefficients.Count == 0) continue;
                    string key = type + "|" + string.Join("|", coefficients.Select(p =>
                        $"{p.Key}={p.Value.ToString("R", CultureInfo.InvariantCulture)}:{roles.GetValueOrDefault(p.Key)}"));
                    if (unique.ContainsKey(key)) continue;
                    unique[key] = new FemLoadCombination($"{tagPrefix} {unique.Count + 1:D3}", type,
                        new Dictionary<int, double>(coefficients), [], new Dictionary<string, double>(generatedCase.Active))
                    { Roles = roles };
                }
            }
        }
        return unique.Values.ToList();
    }
}
