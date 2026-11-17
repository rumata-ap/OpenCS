namespace CScore.Import;

/// <summary>Сечение стержня ЛИРЫ: номер КЭ и номер сечения (1 — у первого узла).</summary>
public readonly record struct LiraBarSection(int Element, int Section);

/// <summary>
/// Строка РСУ стержня ЛИРЫ: сечение, номер строки, критерий, группа (столбец «Г» таблицы РСУ), номера входящих загружений
/// (<c>GetLoadCases</c>) и усилия <see cref="LiraCombinationRows.Components"/>.
/// </summary>
public sealed record LiraRsuRow(LiraBarSection At, int Row, int Criterion, string Group, IReadOnlyList<int> LoadCases,
    IReadOnlyList<double> Forces)
{
    /// <summary>Подпись в терминах таблицы РСУ ЛИРЫ: «РСУ э.1 с2 к18 Б1».</summary>
    public string Label => string.IsNullOrEmpty(Group)
        ? $"РСУ э.{At.Element} с{At.Section} к{Criterion}"
        : $"РСУ э.{At.Element} с{At.Section} к{Criterion} {Group}";
}

/// <summary>РСН ЛИРЫ: номер, имя и усилия <see cref="LiraCombinationRows.Components"/> в сечениях стержней.</summary>
public sealed record LiraRsnRow(int Number, string Name, IReadOnlyDictionary<LiraBarSection, IReadOnlyList<double>> Forces)
{
    /// <summary>Подпись: «РСН 3» или «РСН 3 «имя»».</summary>
    public string Label => string.IsNullOrWhiteSpace(Name) || Name.Trim() == Number.ToString(System.Globalization.CultureInfo.InvariantCulture)
        ? $"РСН {Number}" : $"РСН {Number} «{Name.Trim()}»";
}

/// <summary>Сочетание ЛИРЫ, разложенное на загружения: подпись строки и итог подбора.</summary>
/// <param name="Label">Подпись (строка РСУ или РСН).</param>
/// <param name="Fit">Коэффициенты загружений (номера ЛИРЫ) или причина отказа.</param>
/// <param name="Duplicates">Сколько ещё строк дали то же сочетание (<see cref="LiraCombinationRows.Distinct"/>).</param>
public sealed record LiraFittedCombination(string Label, LiraCombinationFitResult Fit, int Duplicates = 0);

/// <summary>
/// Строки РСУ и РСН ЛИРЫ → сочетания загружений (<see cref="LiraCombinationFit"/>). Усилия загружений и сочетаний —
/// в одних единицах API (подбор от единиц не зависит).
/// </summary>
public static class LiraCombinationRows
{
    /// <summary>Компоненты усилий стержня в строках: N, Mk, My, Mz, Qz, Qy.</summary>
    public const int Components = 6;

    /// <summary>Совпадение коэффициентов при слиянии одинаковых сочетаний.</summary>
    const double SameFactor = 1e-3;

    /// <summary>
    /// РСУ: каждая строка раскладывается по своим шести компонентам на загружения строки. Нет усилий загружения в
    /// сечении строки — отказ строки.
    /// </summary>
    public static List<LiraFittedCombination> Rsu(IEnumerable<LiraRsuRow> rows,
        IReadOnlyDictionary<LiraBarSection, IReadOnlyDictionary<int, IReadOnlyList<double>>> cases,
        double tolerance = LiraCombinationFit.DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(cases);
        var list = new List<LiraFittedCombination>();
        var all = rows as IReadOnlyList<LiraRsuRow> ?? rows.ToList();
        var pending = new List<(int Index, LiraRsuRow Row, List<LiraFitEquation> Eq)>();
        foreach (var row in all)
        {
            if (!cases.TryGetValue(row.At, out var byCase))
            {
                list.Add(new(row.Label, new([], 0, "нет усилий загружений в сечении")));
                continue;
            }
            if (row.LoadCases.Where(lc => !byCase.ContainsKey(lc)).Select(lc => (int?)lc).FirstOrDefault() is int lost)
            {
                list.Add(new(row.Label, new([], 0, $"нет усилий загружения {lost} в сечении")));
                continue;
            }
            var eq = Enumerable.Range(0, Components)
                .Select(c => new LiraFitEquation(row.Forces[c], row.LoadCases.Select(lc => byCase[lc][c]).ToList()))
                .ToList();
            var fit = LiraCombinationFit.Fit(row.LoadCases, eq, tolerance);
            if (fit.Ok)
            {
                // Коэффициенты РСУ ЛИРЫ — ψ и γ из короткого ряда (1; 0,95; 0,9; 0,7 …): шаг 0,05, если строка им
                // воспроизводится. При плохой обусловленности МНК даёт 0,994/0,903 — округлённые точнее для другой колонны.
                var snapped = fit.Terms.Select(t => (t.LoadCase, Math.Round(t.Factor / SnapStep) * SnapStep)).ToList();
                double r = LiraCombinationFit.Residual(row.LoadCases, eq, snapped);
                if (r <= Math.Min(tolerance, ExactResidual)) fit = new(snapped.Select(t => (t.LoadCase, Math.Round(t.Item2, 4))).ToList(), r, null);
            }
            else pending.Add((list.Count, row, eq));
            list.Add(new(row.Label, Clean(fit)));
        }

        // Строки, где коэффициенты по шести компонентам не определяются (загружения пропорциональны в сечении, компонент
        // меньше, чем загружений): то же сочетание группы с тем же набором загружений у других строк, если воспроизводит
        // строку; из подходящих — самое частое.
        var pool = new Dictionary<string, List<(IReadOnlyList<(int LoadCase, double Factor)> Terms, int Count)>>(StringComparer.Ordinal);
        var okRows = all.Zip(list).Where(p => p.Second.Fit.Ok);
        foreach (var (row, fitted) in okRows)
        {
            var bucket = pool.TryGetValue(PoolKey(row), out var b) ? b : pool[PoolKey(row)] = [];
            int i = bucket.FindIndex(x => Same(x.Terms, fitted.Fit.Terms));
            if (i < 0) bucket.Add((fitted.Fit.Terms, 1));
            else bucket[i] = (bucket[i].Terms, bucket[i].Count + 1);
        }
        foreach (var (index, row, eq) in pending)
        {
            if (!pool.TryGetValue(PoolKey(row), out var bucket)) continue;
            foreach (var (terms, _) in bucket.OrderByDescending(x => x.Count))
            {
                var factors = terms.ToDictionary(t => t.LoadCase, t => t.Factor);
                double r = LiraCombinationFit.Residual(row.LoadCases, eq,
                    row.LoadCases.Select(lc => (lc, factors.GetValueOrDefault(lc))).ToList());
                if (r > Math.Min(tolerance, ExactResidual)) continue;
                list[index] = list[index] with { Fit = new(terms, r, null) { Matched = true } };
                break;
            }
        }
        return list;
    }

    /// <summary>Шаг округления коэффициентов РСУ.</summary>
    const double SnapStep = 0.05;

    /// <summary>
    /// Невязка «точного» воспроизведения строки (округлённые коэффициенты, коэффициенты другой строки): API отдаёт float,
    /// верные коэффициенты дают ~1e-7, поэтому допуск строже общего — иначе прошли бы близкие, но чужие сочетания.
    /// </summary>
    const double ExactResidual = 1e-4;

    static string PoolKey(LiraRsuRow row) => row.Group + "|" + string.Join(",", row.LoadCases.Order());

    /// <summary>
    /// РСН: коэффициенты всех загружений по всем сечениям строки (система переопределена). Загружения, не влияющие ни на
    /// одно сечение, исключаются из подбора (<see cref="LiraCombinationFitResult.Ignored"/>); нулевые коэффициенты —
    /// загружение не входит в РСН — из итога убираются.
    /// </summary>
    public static List<LiraFittedCombination> Rsn(IEnumerable<LiraRsnRow> rows,
        IReadOnlyDictionary<LiraBarSection, IReadOnlyDictionary<int, IReadOnlyList<double>>> cases,
        double tolerance = LiraCombinationFit.DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(cases);
        var list = new List<LiraFittedCombination>();
        foreach (var row in rows)
        {
            var at = row.Forces.Keys.Where(cases.ContainsKey).ToList();
            if (at.Count == 0)
            {
                list.Add(new(row.Label, new([], 0, "нет усилий загружений в сечениях РСН")));
                continue;
            }
            var all = at.SelectMany(s => cases[s].Keys).Distinct().Order().ToList();
            // Загружение без влияния на все сечения (или без данных в каком-либо сечении) коэффициента не получает.
            double top = at.SelectMany(s => cases[s].Values).SelectMany(v => v).Select(Math.Abs).DefaultIfEmpty().Max();
            var used = all.Where(lc => at.All(s => cases[s].ContainsKey(lc))
                && at.Any(s => cases[s][lc].Any(v => Math.Abs(v) > 1e-9 * top))).ToList();
            var ignored = all.Except(used).ToList();
            var eq = new List<LiraFitEquation>();
            foreach (var s in at)
                for (int c = 0; c < Components; c++)
                    eq.Add(new LiraFitEquation(row.Forces[s][c], used.Select(lc => cases[s][lc][c]).ToList()));
            var fit = LiraCombinationFit.Fit(used, eq, tolerance) with { Ignored = ignored };
            list.Add(new(row.Label, Clean(fit)));
        }
        return list;
    }

    /// <summary>
    /// Принятые сочетания без повторов: строки с теми же коэффициентами (±0,001) сливаются в первую, счётчик повторов —
    /// <see cref="LiraFittedCombination.Duplicates"/>. Непринятые строки остаются как есть.
    /// </summary>
    public static List<LiraFittedCombination> Distinct(IEnumerable<LiraFittedCombination> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var list = new List<LiraFittedCombination>();
        foreach (var r in rows)
        {
            int i = r.Fit.Ok ? list.FindIndex(x => x.Fit.Ok && Same(x.Fit.Terms, r.Fit.Terms)) : -1;
            if (i < 0) list.Add(r);
            else list[i] = list[i] with { Duplicates = list[i].Duplicates + 1 + r.Duplicates };
        }
        return list;
    }

    static bool Same(IReadOnlyList<(int LoadCase, double Factor)> a, IReadOnlyList<(int LoadCase, double Factor)> b)
    {
        if (a.Count != b.Count) return false;
        var bm = b.ToDictionary(t => t.LoadCase, t => t.Factor);
        return a.All(t => bm.TryGetValue(t.LoadCase, out var f) && Math.Abs(f - t.Factor) <= SameFactor);
    }

    /// <summary>Без нулевых коэффициентов (после округления).</summary>
    static LiraCombinationFitResult Clean(LiraCombinationFitResult fit) =>
        fit with { Terms = fit.Terms.Where(t => t.Factor != 0).ToList() };
}
