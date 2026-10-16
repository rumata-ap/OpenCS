using System.Globalization;
using System.Numerics;
using CScore.Combinations;

namespace CScore.Fem.Combinations;

/// <summary>
/// Отбор комбинаций из огибающей: <paramref name="Tolerance"/> — допуск к экстремуму огибающей (доля, 0,03 = 3 %),
/// <paramref name="MaxCount"/> — не более стольких комбинаций (0 — без ограничения).
/// </summary>
public sealed record FemEnvelopeSelectionOptions(double Tolerance = 0.03, int MaxCount = 0);

/// <summary>
/// Итог отбора: формул-кандидатов (уникальных), отобрано, троек «строка × компонента × знак» огибающей и сколько
/// из них воспроизведено отобранными формулами с допуском.
/// </summary>
public sealed record FemEnvelopeSelectionReport(int Candidates, int Selected, long Triples, long Covered)
{
    public double CoveredShare => Triples == 0 ? 1 : (double)Covered / Triples;
}

/// <summary>
/// Линейный расчёт — комбинирование усилий (спека 2026-10-14, п. 5.2.3–5.2.4): наборы усилий по загружениям с
/// атрибутами СП 20 в имени и комбинации нагрузок из формул, определяющих огибающую усилий.
/// </summary>
public static class FemEnvelopeCombinations
{
    /// <summary>
    /// Нижняя граница допуска — доля наибольшего |значения| компоненты в наборе: экстремумы около нуля (шум, нулевые
    /// строки) не требуют своих формул.
    /// </summary>
    public const double AbsoluteFloorShare = 1e-3;

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
        string tagPrefix = "Огибающая") => Select(sets, defaults, warnings, null, tagPrefix).Combinations;

    /// <summary>
    /// Формулы огибающей (<see cref="FromLoadCaseForceSets"/>) с отбором покрывающего набора: жадно берётся формула,
    /// воспроизводящая экстремум с допуском в наибольшем числе ещё не покрытых троек «строка × компонента × знак»
    /// (все виды наборов и типы сочетаний вместе), пока покрываются новые тройки или не набрано
    /// <see cref="FemEnvelopeSelectionOptions.MaxCount"/>. Комбинации — в порядке отбора (первые важнее).
    /// <paramref name="options"/> = null — все уникальные формулы без отбора.
    /// </summary>
    public static (IReadOnlyList<FemLoadCombination> Combinations, FemEnvelopeSelectionReport Report) Select(
        IReadOnlyList<(ForceSet Set, FemLoadCase LoadCase)> sets, Sp20GammaDefaults defaults, List<string> warnings,
        FemEnvelopeSelectionOptions? options, string tagPrefix = "Огибающая")
    {
        var unique = new Dictionary<string, FemLoadCombination>(StringComparer.Ordinal);
        var blocks = new List<Block>();
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
                Envelope envelope;
                List<Loading> loadings;
                try
                {
                    var (env, generated, lds, w) = SP20Combinations.SP20EnvelopeAndCasesFromForceSets(
                        items.Select(i => i.Set).ToList(), combType, defaults);
                    warnings.AddRange(w);
                    cases = generated;
                    envelope = env;
                    loadings = lds;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    warnings.Add(ex.Message);
                    continue;
                }

                string type = combType == CombType.Accidental ? FemCombinationTypes.A : FemCombinationTypes.C;
                var forcesByCase = new Dictionary<int, double[,]>();
                foreach (var loading in loadings)
                    if (caseIdByTitle.TryGetValue(loading.Name, out int id)) forcesByCase[id] = loading.Forces;
                blocks.Add(new Block(type, envelope, forcesByCase));
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

        var candidates = unique.Values.ToList();
        long triples = blocks.Sum(b => b.Triples);
        if (options == null)
            return (candidates, new FemEnvelopeSelectionReport(candidates.Count, candidates.Count, triples, triples));

        var (order, covered) = GreedyCover(candidates, blocks, options);
        var selected = order.Select((index, rank) => candidates[index] with { Tag = $"{tagPrefix} {rank + 1:D3}" }).ToList();
        return (selected, new FemEnvelopeSelectionReport(candidates.Count, selected.Count, triples, covered));
    }

    /// <summary>Огибающая одного вида наборов и типа сочетания: усилия загружений по Id и экстремумы.</summary>
    sealed record Block(string Type, Envelope Envelope, Dictionary<int, double[,]> Forces)
    {
        public int Sections => Envelope.NSections;
        public int Components => Envelope.ComponentNames.Length;
        public long Triples => (long)Sections * Components * 2;
    }

    /// <summary>
    /// Жадное покрытие: битовые маски троек, которые формула воспроизводит (max: значение ≥ экстремум − допуск,
    /// min: ≤ экстремум + допуск; допуск = доля·|экстремум| + <see cref="AbsoluteFloorShare"/>·max|компоненты|).
    /// </summary>
    static (List<int> Order, long Covered) GreedyCover(List<FemLoadCombination> candidates, List<Block> blocks,
        FemEnvelopeSelectionOptions options)
    {
        double tolerance = Math.Max(0, options.Tolerance);
        var offsets = new long[blocks.Count + 1];
        for (int b = 0; b < blocks.Count; b++) offsets[b + 1] = offsets[b] + blocks[b].Triples;
        int words = (int)((offsets[^1] + 63) / 64);
        var floors = blocks.Select(Floors).ToList();

        var masks = new ulong[candidates.Count][];
        Parallel.For(0, candidates.Count, c =>
        {
            var mask = new ulong[words];
            var combination = candidates[c];
            for (int b = 0; b < blocks.Count; b++)
            {
                var block = blocks[b];
                if (block.Type != combination.CombinationType) continue;
                var terms = combination.Coefficients.Where(p => block.Forces.ContainsKey(p.Key))
                    .Select(p => (Coefficient: p.Value, Forces: block.Forces[p.Key])).ToArray();
                int nc = block.Components;
                for (int s = 0; s < block.Sections; s++)
                    for (int k = 0; k < nc; k++)
                    {
                        double value = 0;
                        foreach (var (coefficient, forces) in terms) value += coefficient * forces[s, k];
                        double max = block.Envelope.MaxValues[k, s], min = block.Envelope.MinValues[k, s];
                        long t = offsets[b] + ((long)s * nc + k) * 2;
                        if (value >= max - (tolerance * Math.Abs(max) + floors[b][k])) mask[t >> 6] |= 1UL << (int)(t & 63);
                        t++;
                        if (value <= min + (tolerance * Math.Abs(min) + floors[b][k])) mask[t >> 6] |= 1UL << (int)(t & 63);
                    }
            }
            masks[c] = mask;
        });

        var covered = new ulong[words];
        var used = new bool[candidates.Count];
        var order = new List<int>();
        long coveredCount = 0;
        int limit = options.MaxCount > 0 ? options.MaxCount : int.MaxValue;
        var gains = new int[candidates.Count];
        while (order.Count < limit)
        {
            Parallel.For(0, candidates.Count, c =>
            {
                if (used[c]) { gains[c] = 0; return; }
                int gain = 0;
                var mask = masks[c];
                for (int w = 0; w < words; w++) gain += BitOperations.PopCount(mask[w] & ~covered[w]);
                gains[c] = gain;
            });
            int best = -1, bestGain = 0;
            for (int c = 0; c < candidates.Count; c++)
                if (gains[c] > bestGain) { bestGain = gains[c]; best = c; }
            if (best < 0) break;
            used[best] = true;
            order.Add(best);
            coveredCount += bestGain;
            var bestMask = masks[best];
            for (int w = 0; w < words; w++) covered[w] |= bestMask[w];
        }
        return (order, coveredCount);
    }

    /// <summary>Нижняя граница допуска по компонентам блока.</summary>
    static double[] Floors(Block block)
    {
        var floors = new double[block.Components];
        for (int k = 0; k < block.Components; k++)
        {
            double scale = 0;
            for (int s = 0; s < block.Sections; s++)
                scale = Math.Max(scale, Math.Max(Math.Abs(block.Envelope.MaxValues[k, s]), Math.Abs(block.Envelope.MinValues[k, s])));
            floors[k] = AbsoluteFloorShare * scale + 1e-12;
        }
        return floors;
    }
}
