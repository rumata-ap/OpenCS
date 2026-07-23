using System.Globalization;
using CScore.Combinations;

namespace CScore.Fem.Combinations;

/// <summary>Результат преобразования FEM-загружений в Loading.</summary>
public sealed record FemLoadingConversion(
    IReadOnlyList<Loading> Loadings,
    IReadOnlyList<string> Warnings);

/// <summary>Одна узловая комбинация с коэффициентами исходных загружений.</summary>
public sealed record FemLoadCombination(
    string Tag,
    string CombinationType,
    IReadOnlyDictionary<int, double> Coefficients,
    double[] Vector,
    IReadOnlyDictionary<string, double> Active);

/// <summary>Адаптер канонических FEM-загружений к существующей комбинаторике СП20.</summary>
public static class FemSp20CombinationAdapter
{
    static readonly string[] ComponentPrefixes = ["Fx", "Fy", "Fz", "Mx", "My", "Mz"];
    static readonly HashSet<string> KnownTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "permanent", "long_term", "short_term", "accidental"
    };

    /// <summary>Преобразует выбранные загружения в матрицы Loading с одной строкой.</summary>
    public static FemLoadingConversion ToLoadings(
        IReadOnlyList<FemLoadCase> loadCases,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemNodeLoad> loads,
        IReadOnlyList<int> orderedNodeIds,
        IReadOnlyList<FemMemberLoad>? memberLoads = null)
    {
        ArgumentNullException.ThrowIfNull(loadCases);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(loads);
        ArgumentNullException.ThrowIfNull(orderedNodeIds);
        memberLoads ??= [];

        var warnings = new List<string>();
        var result = new List<Loading>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var memberDescriptors = memberLoads
            .Select((load, index) => (Load: load, Index: index))
            .OrderBy(item => item.Load.Id)
            .ThenBy(item => item.Index)
            .ToArray();
        var components = orderedNodeIds
            .SelectMany(nodeId => ComponentPrefixes.Select(prefix => prefix + nodeId))
            .Concat(memberDescriptors.SelectMany(item => new[]
            {
                $"MemberLoad{item.Load.Id}QxStart", $"MemberLoad{item.Load.Id}QyStart",
                $"MemberLoad{item.Load.Id}QzStart", $"MemberLoad{item.Load.Id}QxEnd",
                $"MemberLoad{item.Load.Id}QyEnd", $"MemberLoad{item.Load.Id}QzEnd"
            }))
            .ToArray();

        foreach (var loadCase in loadCases)
        {
            if (!KnownTypes.Contains(loadCase.Sp20Type))
            {
                warnings.Add($"Загружение {loadCase.Id}: неизвестный Sp20Type '{loadCase.Sp20Type}'.");
                continue;
            }

            string name = string.IsNullOrWhiteSpace(loadCase.Tag)
                ? $"LoadCase {loadCase.Id}"
                : loadCase.Tag;
            if (!names.Add(name))
                name = $"{name} #{loadCase.Id}";
            names.Add(name);

            var nodeVector = FemLoadVectorBuilder.Build(
                nodes,
                loads.Where(load => load.LoadCaseId == loadCase.Id).ToArray(),
                orderedNodeIds,
                loadCase.Id);
            var vector = new double[nodeVector.Length + memberDescriptors.Length * 6];
            Array.Copy(nodeVector, vector, nodeVector.Length);
            for (int i = 0; i < memberDescriptors.Length; i++)
            {
                var memberLoad = memberDescriptors[i].Load;
                if (memberLoad.LoadCaseId != loadCase.Id) continue;
                int offset = nodeVector.Length + i * 6;
                vector[offset] = memberLoad.QxStart;
                vector[offset + 1] = memberLoad.QyStart;
                vector[offset + 2] = memberLoad.QzStart;
                vector[offset + 3] = memberLoad.QxEnd;
                vector[offset + 4] = memberLoad.QyEnd;
                vector[offset + 5] = memberLoad.QzEnd;
            }
            var matrix = new double[1, vector.Length];
            for (int i = 0; i < vector.Length; i++)
                matrix[0, i] = vector[i];

            result.Add(CreateLoading(loadCase, name, matrix, components));
        }

        return new FemLoadingConversion(result, warnings);
    }

    /// <summary>Строит обычную сумму выбранных загружений с коэффициентом единица.</summary>
    public static FemLoadCombination BuildUnitSum(
        IReadOnlyList<FemLoadCase> loadCases,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemNodeLoad> loads,
        IReadOnlyList<int> orderedNodeIds,
        IReadOnlyList<FemMemberLoad>? memberLoads = null)
    {
        var conversion = ToLoadings(loadCases, nodes, loads, orderedNodeIds, memberLoads);
        ThrowIfWarnings(conversion.Warnings);
        if (conversion.Loadings.Count == 0)
            throw new ArgumentException("Не выбрано ни одного загружения.", nameof(loadCases));

        var coefficients = loadCases.ToDictionary(loadCase => loadCase.Id, _ => 1.0);
        var vector = new double[conversion.Loadings[0].Forces.GetLength(1)];
        var active = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < conversion.Loadings.Count; i++)
        {
            var loading = conversion.Loadings[i];
            for (int j = 0; j < vector.Length; j++)
                vector[j] += loading.Forces[0, j];
            active[loading.Name] = 1.0;
        }

        return new FemLoadCombination("Сумма загружений", "unit_sum", coefficients, vector, active);
    }

    /// <summary>Генерирует FEM-векторы огибающей существующим Combinator СП20.</summary>
    public static IReadOnlyList<FemLoadCombination> BuildSp20(
        IReadOnlyList<FemLoadCase> loadCases,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemNodeLoad> loads,
        IReadOnlyList<int> orderedNodeIds,
        string combinationType = "fundamental",
        IReadOnlyList<FemMemberLoad>? memberLoads = null)
    {
        var conversion = ToLoadings(loadCases, nodes, loads, orderedNodeIds, memberLoads);
        ThrowIfWarnings(conversion.Warnings);
        if (conversion.Loadings.Count == 0)
            return [];

        var combType = ParseCombinationType(combinationType);
        var rules = combType == CombType.Fundamental
            ? CombRules.SP20Fundamental()
            : CombRules.SP20Accidental();
        var generated = new Combinator(conversion.Loadings.ToList(), rules).FullEnvelopeWithCases().Cases;
        var loadingByName = conversion.Loadings.ToDictionary(loading => loading.Name, StringComparer.Ordinal);
        var caseIdByName = loadCases.ToDictionary(
            loadCase => string.IsNullOrWhiteSpace(loadCase.Tag) ? $"LoadCase {loadCase.Id}" : loadCase.Tag,
            loadCase => loadCase.Id,
            StringComparer.Ordinal);
        var unique = new Dictionary<string, FemLoadCombination>(StringComparer.Ordinal);

        foreach (var generatedCase in generated)
        {
            string key = FormulaKey(generatedCase.Active);
            if (unique.ContainsKey(key))
                continue;

            var coefficients = new Dictionary<int, double>();
            var vector = new double[conversion.Loadings[0].Forces.GetLength(1)];
            foreach (var (name, coefficient) in generatedCase.Active)
            {
                if (!loadingByName.TryGetValue(name, out var loading))
                    continue;
                if (!caseIdByName.TryGetValue(name, out int caseId))
                {
                    var source = loadCases.FirstOrDefault(loadCase => loadCase.Tag == name);
                    if (source == null)
                        continue;
                    caseId = source.Id;
                }

                coefficients[caseId] = coefficient;
                for (int i = 0; i < vector.Length; i++)
                    vector[i] += coefficient * loading.Forces[0, i];
            }

            unique[key] = new FemLoadCombination(
                $"СП20 {unique.Count + 1:D3}",
                combinationType,
                coefficients,
                vector,
                new Dictionary<string, double>(generatedCase.Active, StringComparer.Ordinal));
        }

        return unique.Values.ToArray();
    }

    static Loading CreateLoading(FemLoadCase loadCase, string name, double[,] matrix, string[] components)
    {
        return loadCase.Sp20Type.ToLowerInvariant() switch
        {
            "permanent" => Loading.Permanent(
                name, matrix, components,
                loadCase.GammaFUnfav ?? 1.1,
                loadCase.GammaFFav ?? 0.9,
                loadCase.Sp20Group),
            "long_term" => Loading.LongTerm(
                name, matrix, components,
                loadCase.GammaFUnfav ?? 1.2,
                loadCase.GammaFFav ?? 1.0,
                loadCase.Psi1 ?? 1.0,
                loadCase.Psi2 ?? 0.95,
                loadCase.Sp20Group),
            "short_term" => Loading.ShortTerm(
                name, matrix, components,
                loadCase.GammaFUnfav ?? 1.4,
                loadCase.GammaFFav ?? 1.0,
                loadCase.Psi1 ?? 1.0,
                loadCase.Psi2 ?? 0.9,
                loadCase.Sp20Group),
            "accidental" => Loading.Accidental(
                name, matrix, components,
                loadCase.GammaFUnfav ?? 1.0,
                loadCase.GammaFFav ?? 1.0,
                loadCase.Sp20Group),
            _ => throw new InvalidOperationException("Недопустимый тип загружения СП20.")
        };
    }

    static CombType ParseCombinationType(string combinationType) =>
        combinationType.Trim().ToLowerInvariant() switch
        {
            "fundamental" => CombType.Fundamental,
            "accidental" => CombType.Accidental,
            _ => throw new ArgumentException(
                $"Неизвестный тип сочетания '{combinationType}'.", nameof(combinationType))
        };

    static string FormulaKey(IReadOnlyDictionary<string, double> active) =>
        string.Join("|", active.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value.ToString("R", CultureInfo.InvariantCulture)}"));

    static void ThrowIfWarnings(IReadOnlyList<string> warnings)
    {
        if (warnings.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, warnings));
    }
}
