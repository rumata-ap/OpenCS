using System.Globalization;
using System.Numerics;
using CScore.Combinations;

namespace CScore.Fem.Combinations;

/// <summary>
/// Отбор комбинаций из огибающей: <paramref name="Tolerance"/> — допуск к экстремуму огибающей (доля, 0,03 = 3 %),
/// <paramref name="MaxCount"/> — не более стольких комбинаций в каждой группе (0 — без ограничения).
/// </summary>
public sealed record FemEnvelopeSelectionOptions(double Tolerance = 0.03, int MaxCount = 0)
{
    /// <summary>Группы C/CL/N/NL/A; null — <see cref="FemEnvelopeCombinations.DefaultGroups"/>.</summary>
    public IReadOnlyList<string>? Groups { get; init; }

    /// <summary>Вид величин нагрузок, выбранный при отборе (меняет настройку расчёта); null — из настроек.</summary>
    public Sp20LoadValueKind? ValueKind { get; init; }

    /// <summary>Группы строкой настроек «C,CL» (порядок — как в <see cref="FemCombinationTypes.All"/>).</summary>
    public string GroupsText => string.Join(",",
        FemCombinationTypes.All.Where(g => (Groups ?? FemEnvelopeCombinations.DefaultGroups).Contains(g)));

    /// <summary>Группы из строки настроек; неизвестные пропускаются, пусто — null.</summary>
    public static IReadOnlyList<string>? ParseGroups(string? text)
    {
        var groups = (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(g => FemCombinationTypes.All.Contains(g)).Distinct().ToList();
        return groups.Count == 0 ? null : groups;
    }
}

/// <summary>
/// Итог отбора по группе: формул-кандидатов (уникальных), отобрано, троек «строка × компонента × знак» огибающей и
/// сколько из них воспроизведено отобранными формулами с допуском.
/// </summary>
public sealed record FemEnvelopeSelectionReport(string Group, int Candidates, int Selected, long Triples, long Covered)
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

    /// <summary>Группы по умолчанию: основное C и, при особых загружениях, особое A.</summary>
    public static IReadOnlyList<string> DefaultGroups { get; } = [FemCombinationTypes.C, FemCombinationTypes.A];

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
        string tagPrefix = "Огибающая") =>
        Select(sets, defaults, Sp20LoadValueKind.Normative, warnings, null, null, tagPrefix).Combinations;

    /// <summary>
    /// Формулы огибающей по группам <paramref name="groups"/> (C/CL/N/NL, A — только при особых загружениях; null —
    /// группы <paramref name="options"/>, без них — <see cref="DefaultGroups"/>): огибающая каждой группы — <see cref="SP20Combinations.SP20EnvelopeAndCasesForGroup"/>
    /// с видом величин и долей длительной части загружений. С <paramref name="options"/> — отбор покрывающего набора в
    /// каждой группе: жадно берётся формула, воспроизводящая экстремум с допуском в наибольшем числе ещё не покрытых
    /// троек «строка × компонента × знак» (виды наборов вместе), пока покрываются новые тройки или не набрано
    /// <see cref="FemEnvelopeSelectionOptions.MaxCount"/>; комбинации — в порядке отбора (первые важнее).
    /// <paramref name="options"/> = null — все уникальные формулы. Имена — «Огибающая C 001».
    /// </summary>
    public static (IReadOnlyList<FemLoadCombination> Combinations, IReadOnlyList<FemEnvelopeSelectionReport> Reports) Select(
        IReadOnlyList<(ForceSet Set, FemLoadCase LoadCase)> sets, Sp20GammaDefaults defaults, Sp20LoadValueKind valueKind,
        List<string> warnings, IReadOnlyCollection<string>? groups, FemEnvelopeSelectionOptions? options,
        string tagPrefix = "Огибающая")
    {
        bool hasAccidental = sets.Any(s => FemCombinationCoefficients.LoadType(s.LoadCase) == NormLoadType.Accidental);
        var requested = FemCombinationTypes.All.Where(g => (groups ?? options?.Groups ?? DefaultGroups).Contains(g)).ToList();
        var combinations = new List<FemLoadCombination>();
        var reports = new List<FemEnvelopeSelectionReport>();
        foreach (string group in requested)
        {
            if (group == FemCombinationTypes.A && !hasAccidental) continue;
            var (candidates, blocks) = Candidates(sets, defaults, valueKind, group, warnings);
            long triples = blocks.Sum(b => b.Triples);
            List<FemLoadCombination> selected;
            long covered = triples;
            if (options == null) selected = candidates;
            else
            {
                (var order, covered) = GreedyCover(candidates, blocks, options);
                selected = order.Select(index => candidates[index]).ToList();
            }
            combinations.AddRange(selected.Select((c, rank) => c with { Tag = $"{tagPrefix} {group} {rank + 1:D3}" }));
            reports.Add(new FemEnvelopeSelectionReport(group, candidates.Count, selected.Count, triples, covered));
        }
        return (combinations, reports);
    }

    /// <summary>Уникальные формулы огибающей группы по видам наборов и блоки огибающей для отбора.</summary>
    static (List<FemLoadCombination> Candidates, List<Block> Blocks) Candidates(
        IReadOnlyList<(ForceSet Set, FemLoadCase LoadCase)> sets, Sp20GammaDefaults defaults, Sp20LoadValueKind valueKind,
        string group, List<string> warnings)
    {
        var unique = new Dictionary<string, FemLoadCombination>(StringComparer.Ordinal);
        var blocks = new List<Block>();
        // В N/NL постоянные берутся одним коэффициентом: снижение веса (п. 7.3) — только в C/CL.
        bool reducedApplies = group is FemCombinationTypes.C or FemCombinationTypes.CL;
        foreach (var kind in sets.GroupBy(s => s.Set.Kind))
        {
            var items = kind.ToList();
            var caseIdByTitle = new Dictionary<string, int>(StringComparer.Ordinal);
            var shares = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (set, loadCase) in items)
            {
                string title = SP20Combinations.ParseForceSetName(set.Tag).Title;
                caseIdByTitle[title] = loadCase.Id;
                if (loadCase.LongTermShare is double share) shares[title] = share;
            }
            List<GeneratedCase> cases;
            Envelope envelope;
            List<Loading> loadings;
            try
            {
                var (env, generated, lds, w) = SP20Combinations.SP20EnvelopeAndCasesForGroup(
                    items.Select(i => i.Set).ToList(), group, valueKind, defaults, shares);
                foreach (var warning in w)
                    if (!warnings.Contains(warning)) warnings.Add(warning);
                cases = generated;
                envelope = env;
                loadings = lds;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                if (!warnings.Contains(ex.Message)) warnings.Add(ex.Message);
                continue;
            }

            // Коэффициенты формул — к величинам наборов (у особых расчётных огибающая считает по F/γf и множит
            // коэффициенты обратно), поэтому значения формул — по исходным матрицам наборов.
            var forcesByCase = new Dictionary<int, double[,]>();
            foreach (var raw in SP20Combinations.ForceSetsToLoadings(items.Select(i => i.Set).ToList(), defaults).Loadings)
                if (caseIdByTitle.TryGetValue(raw.Name, out int id) && loadings.Any(l => l.Name == raw.Name))
                    forcesByCase[id] = raw.Forces;
            blocks.Add(new Block(envelope, forcesByCase));
            foreach (var generatedCase in cases)
            {
                var coefficients = new SortedDictionary<int, double>();
                var roles = new Dictionary<int, Sp20TermRole>();
                foreach (var (name, coefficient) in generatedCase.Active)
                {
                    if (!caseIdByTitle.TryGetValue(name, out int caseId)) continue;
                    coefficients[caseId] = coefficient;
                    if (generatedCase.Roles.TryGetValue(name, out var role))
                        roles[caseId] = !reducedApplies && role == Sp20TermRole.PermanentReduced ? Sp20TermRole.Permanent : role;
                }
                if (coefficients.Count == 0) continue;
                string key = string.Join("|", coefficients.Select(p =>
                    $"{p.Key}={p.Value.ToString("R", CultureInfo.InvariantCulture)}:{roles.GetValueOrDefault(p.Key)}"));
                if (unique.ContainsKey(key)) continue;
                unique[key] = new FemLoadCombination("", group, new Dictionary<int, double>(coefficients), [],
                    new Dictionary<string, double>(generatedCase.Active))
                { Roles = roles };
            }
        }
        return (unique.Values.ToList(), blocks);
    }

    /// <summary>Огибающая одного вида наборов группы: усилия загружений по Id и экстремумы.</summary>
    sealed record Block(Envelope Envelope, Dictionary<int, double[,]> Forces)
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
