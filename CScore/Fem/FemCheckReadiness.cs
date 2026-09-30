using System.Globalization;
using System.Text;

namespace CScore.Fem;

/// <summary>Покрытие КЭ цели сечениями (стержни) или армированием из одного источника (пластины).</summary>
/// <param name="Source">Ключ источника армирования (<see cref="FemCheckRebarSource"/>); у стержней — пусто.</param>
/// <param name="Ready">КЭ, для которых сечение есть.</param>
/// <param name="NotReady">Номера КЭ без сечения (по возрастанию).</param>
/// <param name="Reasons">Причины отсутствия сечения и число КЭ по каждой.</param>
public sealed record FemCheckSourceReadiness(
    string Source, int Ready, IReadOnlyList<int> NotReady, IReadOnlyList<(string Reason, int Count)> Reasons);

/// <summary>Источник сечений для оценки готовности: ключ и причина отсутствия сечения у КЭ (null — сечение есть).</summary>
public sealed record FemCheckSectionProbe(string Source, Func<FemCheckScopeElement, string?> MissingReason);

/// <summary>
/// Готовность цели к проверке по КЭ: покрытие её КЭ строками усилий и сечениями/армированием.
/// Считается без решателей — до запуска проверки.
/// </summary>
/// <param name="ElementsTotal">Число КЭ цели.</param>
/// <param name="ElementsWithForces">КЭ цели, для которых есть строки усилий.</param>
/// <param name="ElementsWithoutForces">Номера КЭ цели без строк усилий (по возрастанию).</param>
/// <param name="RowsForTarget">Строк усилий, относящихся к КЭ цели.</param>
/// <param name="RowsOutsideTarget">Строк усилий по КЭ вне цели (в проверке пропускаются).</param>
/// <param name="RowsWithoutElement">Строк без номера КЭ (проверяются по сечению цели).</param>
/// <param name="SetsWithoutElementNumbers">Наборов, в строках которых нет номеров КЭ.</param>
/// <param name="Sources">Покрытие сечениями по каждому источнику.</param>
/// <param name="BlockingReason">Почему проверку нельзя запустить; null — можно.</param>
public sealed record FemCheckReadiness(
    int ElementsTotal,
    int ElementsWithForces,
    IReadOnlyList<int> ElementsWithoutForces,
    int RowsForTarget,
    int RowsOutsideTarget,
    int RowsWithoutElement,
    int SetsWithoutElementNumbers,
    IReadOnlyList<FemCheckSourceReadiness> Sources,
    string? BlockingReason)
{
    /// <summary>Проверку можно запускать.</summary>
    public bool CanRun => BlockingReason == null;

    /// <summary>Все КЭ цели покрыты и усилиями, и сечениями каждого источника.</summary>
    public bool IsComplete =>
        CanRun && ElementsWithoutForces.Count == 0 && Sources.All(s => s.NotReady.Count == 0);

    /// <summary>Оценить готовность цели.</summary>
    /// <param name="isPlate">Проверка пластин (строки — <see cref="ForceSet.ShellItems"/>), иначе стержней.</param>
    /// <param name="elements">КЭ цели нужного вида.</param>
    /// <param name="forceSets">Наборы усилий, выбранные для проверки.</param>
    /// <param name="probes">Источники сечений: у стержней — один, у пластин — по числу источников армирования.</param>
    /// <param name="templateProblem">Блокирующая причина вне покрытия (например, у цели нет сечения-шаблона).</param>
    public static FemCheckReadiness Evaluate(
        bool isPlate,
        IReadOnlyList<FemCheckScopeElement> elements,
        IReadOnlyList<ForceSet> forceSets,
        IReadOnlyList<FemCheckSectionProbe> probes,
        string? templateProblem = null)
    {
        var numbers = new HashSet<int>();
        foreach (var e in elements)
            if (e.ElemNum is int n) numbers.Add(n);

        var covered = new HashSet<int>();
        int rowsForTarget = 0, rowsOutside = 0, rowsWithoutElement = 0, setsWithoutNumbers = 0;
        foreach (var fs in forceSets)
        {
            bool anyNumber = false;
            int rows = 0;
            foreach (int? num in RowElementNumbers(fs, isPlate))
            {
                rows++;
                if (num is not int n) { rowsWithoutElement++; continue; }
                anyNumber = true;
                if (numbers.Contains(n)) { rowsForTarget++; covered.Add(n); }
                else rowsOutside++;
            }
            if (rows > 0 && !anyNumber) setsWithoutNumbers++;
        }

        var sources = new List<FemCheckSourceReadiness>(probes.Count);
        foreach (var probe in probes)
        {
            var notReady = new List<int>();
            var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
            int ready = 0;
            foreach (var e in elements)
            {
                string? reason = probe.MissingReason(e);
                if (reason == null) { ready++; continue; }
                if (e.ElemNum is int n) notReady.Add(n);
                reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
            }
            notReady.Sort();
            sources.Add(new FemCheckSourceReadiness(probe.Source, ready, notReady,
                [.. reasons.OrderByDescending(r => r.Value).Select(r => (r.Key, r.Value))]));
        }

        string? blocking = templateProblem;
        if (blocking == null && rowsForTarget + rowsWithoutElement == 0)
            blocking = "В выбранных наборах нет строк усилий для КЭ цели";
        if (blocking == null && rowsWithoutElement == 0 && sources.Count > 0 && sources.All(s => s.Ready == 0))
            blocking = isPlate ? "Ни у одного КЭ цели нет армирования из выбранных источников"
                               : "Ни у одного КЭ цели нет расчётного сечения";

        var withoutForces = numbers.Where(n => !covered.Contains(n)).Order().ToList();
        return new FemCheckReadiness(elements.Count, elements.Count(e => e.ElemNum is int n && covered.Contains(n)),
            withoutForces, rowsForTarget, rowsOutside, rowsWithoutElement, setsWithoutNumbers, sources, blocking);
    }

    /// <summary>Номера КЭ строк набора (null — строка без номера КЭ).</summary>
    internal static IEnumerable<int?> RowElementNumbers(ForceSet fs, bool isPlate) =>
        isPlate ? fs.ShellItems.Select(s => s.SourceElementNum) : fs.Items.Select(i => i.SourceElementNum);

    /// <summary>Номера диапазонами: «1-5, 8, 10-12»; длинный список обрывается многоточием.</summary>
    public static string FormatRanges(IEnumerable<int> numbers, int maxRanges = 20)
    {
        var sorted = numbers.Distinct().Order().ToList();
        var sb = new StringBuilder();
        int ranges = 0;
        for (int i = 0; i < sorted.Count;)
        {
            int j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1) j++;
            if (ranges == maxRanges) { sb.Append(", …"); break; }
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(sorted[i].ToString(CultureInfo.InvariantCulture));
            if (j > i) sb.Append('-').Append(sorted[j].ToString(CultureInfo.InvariantCulture));
            ranges++;
            i = j + 1;
        }
        return sb.ToString();
    }
}
