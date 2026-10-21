using System.Globalization;
using CScore;
using CScore.Fem;
using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>
/// Наборы усилий пакета вариантов CSfea «строка на вариант» (спека 2026-10-17 «наборы по вариантам», п. 2.2): на каждую
/// отмеченную группу пакета (тип комбинации C…A или загружения) — набор пластин и набор стержней; в набор — строки КЭ
/// охвата от каждого варианта группы, дошедшего до предела λ. Метка строки «КЭ · вариант», тип расчёта — меткой в конце
/// имени (C, CL, N, NL; A — C; у загружений — типы, выбранные пользователем, набор на тип). Поля вариантов читаются по
/// одному и сразу отпускаются.
/// </summary>
public static class FemCsfeaBatchForceSets
{
    /// <summary>Наборы и замечания (не вошедшие варианты, пустые группы).</summary>
    public sealed record Result(List<ForceSet> Sets, List<string> Warnings);

    /// <summary>Не вошедшие варианты в описании и замечаниях — первые столько.</summary>
    public const int ListedSkipped = 10;

    /// <summary>Порядок групп пакета.</summary>
    public static IReadOnlyList<string> GroupOrder { get; } = ["C", "CL", "N", "NL", "A", FemCsfeaBatchGroup.LoadCases];

    /// <summary>Типы расчёта, которые можно назначить группе загружений.</summary>
    public static IReadOnlyList<string> LoadCaseTypes { get; } = ["C", "CL", "N", "NL"];

    /// <summary>Название группы в имени набора: тип комбинации или «Загружения».</summary>
    public static string GroupTitle(string key) => key == FemCsfeaBatchGroup.LoadCases ? "Загружения" : key;

    /// <summary>Метка типа расчёта группы комбинаций: C/CL/N/NL — свой, A и прочие — C (проверка по прочности).</summary>
    public static string CalcTypeLabel(string key) => key is "CL" or "N" or "NL" ? key : "C";

    /// <summary>Вариант входит в наборы: дошёл до предела λ и поля записаны.</summary>
    public static bool IsIncluded(FemCsfeaBatchSummary b) => b.Completed && b.RecordN != null;

    /// <param name="loadFields">Поля варианта по номеру записи (<see cref="FemCsfeaBatchSummary.RecordN"/>); null — не прочитаны.</param>
    /// <param name="groups">Ключи групп (<see cref="FemCsfeaBatchSummary.GroupKey"/>).</param>
    /// <param name="loadCaseTypes">Типы расчёта наборов группы загружений (<see cref="LoadCaseTypes"/>).</param>
    /// <param name="scope">Охват — КЭ сетки; null — вся схема.</param>
    /// <param name="progress">Прочитано вариантов из числа читаемых.</param>
    /// <param name="longGroups">
    /// У групп N и C с посчитанными длительными частями — ещё набор «(NL)» («(CL)») с той же основой имени: строки — поля
    /// пары (диаграммы варианта, п. 6.1.26), метки — от варианта, чтобы φ1 и ψ «авто» проверки по КЭ находили пару. Null —
    /// у всех таких групп, иначе — только у перечисленных.
    /// </param>
    public static Result Build(FemSchema schema, string analysisTag, FemCsfeaResultSummary summary,
        Func<int, RcSecantStepFields?> loadFields, IReadOnlyCollection<string> groups, IReadOnlyCollection<string> loadCaseTypes,
        FemCsfeaForceSetScope? scope, IReadOnlyCollection<ForceSet> existing, IProgress<(int Done, int Count)>? progress,
        CancellationToken ct, IReadOnlyCollection<string>? longGroups = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(summary);
        var sets = new List<ForceSet>();
        var warnings = new List<string>();
        if (summary.Batch is not { Count: > 0 } batch)
        {
            warnings.Add("Результат — не пакет вариантов или в пакете нет вариантов.");
            return new Result(sets, warnings);
        }

        var keys = GroupOrder.Concat(groups.Except(GroupOrder)).Where(groups.Contains).ToList();
        var types = LoadCaseTypes.Where(loadCaseTypes.Contains).ToList();
        bool WantsLong(string k) => (longGroups?.Contains(k) ?? true) && HasLongParts(summary, k);
        int count = keys.Sum(k => k == FemCsfeaBatchGroup.LoadCases && types.Count == 0 ? 0
            : batch.Count(b => b.GroupKey == k && IsIncluded(b)) * (WantsLong(k) ? 2 : 1));
        int done = 0;
        progress?.Report((0, count));
        var tags = existing.Select(fs => fs.Tag).ToHashSet(StringComparer.Ordinal);
        int num = existing.Count == 0 ? 1 : existing.Max(f => f.Num) + 1;
        string suffix = scope?.TagSuffix ?? "";

        foreach (string key in keys)
        {
            string title = GroupTitle(key);
            var groupTypes = key == FemCsfeaBatchGroup.LoadCases ? types : [CalcTypeLabel(key)];
            string? longType = WantsLong(key) ? LongTypeOf(key) : null;
            if (longType != null) groupTypes = [.. groupTypes, longType];
            var noPair = new List<string>();
            if (groupTypes.Count == 0)
            {
                warnings.Add($"Группа «{title}»: не выбран тип расчёта — наборы не созданы.");
                continue;
            }
            var variants = batch.Where(b => b.GroupKey == key).ToList();
            var skipped = variants.Where(b => !IsIncluded(b)).Select(Skipped).ToList();

            // Строки на каждый тип группы (у загружений одни и те же строки — свой экземпляр на набор).
            var shells = groupTypes.ToDictionary(t => t, _ => new List<ShellLoadItem>());
            var beams = groupTypes.ToDictionary(t => t, _ => new List<LoadItem>());
            int read = 0;
            double maxLambda = 0;
            foreach (var b in variants.Where(IsIncluded))
            {
                ct.ThrowIfCancellationRequested();
                var fields = loadFields(b.RecordN!.Value);
                progress?.Report((++done, count));
                if (fields == null)
                {
                    skipped.Add($"{b.Tag} (поля не прочитаны)");
                    continue;
                }
                foreach (string t in groupTypes.Where(t => t != longType))
                {
                    FemCsfeaForceSetBuilder.AppendShellRows(shells[t], fields, scope?.Elements, b.Tag);
                    FemCsfeaForceSetBuilder.AppendBeamRows(beams[t], fields, scope?.Elements, b.Tag);
                }
                if (longType != null)
                {
                    // Длительная часть: своя — поля пары, совпадает с полной — поля варианта, нулевая — нулевые строки.
                    var longFields = summary.LongOf(b.Index) switch
                    {
                        { IsZero: true } => ZeroForces(fields),
                        { IsSameAsTotal: true } => fields,
                        { } p when summary.LongRecordN(p) is int n => loadFields(n),
                        _ => null,
                    };
                    progress?.Report((++done, count));
                    if (longFields == null) noPair.Add(b.Tag);
                    else
                    {
                        FemCsfeaForceSetBuilder.AppendShellRows(shells[longType], longFields, scope?.Elements, b.Tag);
                        FemCsfeaForceSetBuilder.AppendBeamRows(beams[longType], longFields, scope?.Elements, b.Tag);
                    }
                }
                read++;
                maxLambda = Math.Max(maxLambda, b.LoadFactor);
            }

            if (noPair.Count > 0)
                warnings.Add(string.Create(CultureInfo.CurrentCulture,
                    $"Группа «{title}»: у {noPair.Count} вариантов нет посчитанной длительной части {longType} — в наборе {longType} их строк нет: {string.Join(", ", noPair.Take(ListedSkipped))}{(noPair.Count > ListedSkipped ? $" и ещё {noPair.Count - ListedSkipped}" : "")}."));
            string skippedText = SkippedText(skipped);
            if (skipped.Count > 0) warnings.Add($"Группа «{title}»: {char.ToLowerInvariant(skippedText[0])}{skippedText[1..]}");
            if (read == 0)
            {
                warnings.Add($"Группа «{title}»: нет вариантов, дошедших до предела λ, — наборы не созданы.");
                continue;
            }

            string description = string.Create(CultureInfo.CurrentCulture,
                $"Пакетный расчёт CSfea «{analysisTag}» схемы «{schema.Tag}»: группа «{title}», строка на вариант — вариантов {read}, дошедших до предела λ = {maxLambda:0.###}.");
            if (skipped.Count > 0) description += " " + skippedText;
            if (scope != null) description += " " + scope.Description;

            foreach (var (part, kind) in new[] { ("пластины", "shell"), ("стержни", "bar") })
            {
                if (kind == "shell" ? shells[groupTypes[0]].Count == 0 : beams[groupTypes[0]].Count == 0) continue;
                // Длительный набор без строк (ни у одного варианта нет пары) не создаётся.
                var setTypes = groupTypes.Where(t => t == groupTypes[0] || (kind == "shell" ? shells[t].Count : beams[t].Count) > 0).ToList();
                string baseTag = string.Create(CultureInfo.CurrentCulture, $"{analysisTag} — {title}, {read} вар. — {part}{suffix}");
                // Номер занятого имени — общий для типов группы (пара «(N)» ↔ «(NL)» ищется по основе имени).
                int n = 1;
                while (groupTypes.Any(t => tags.Contains(Tag(baseTag, n, t)))) n++;
                foreach (string t in setTypes)
                {
                    var set = new ForceSet
                    {
                        Num = num++, Tag = Tag(baseTag, n, t), Kind = kind, SourceType = "fea", SourceSchemaId = schema.Id,
                        SecondOrder = summary.SecondOrder,
                        Description = key == FemCsfeaBatchGroup.LoadCases ? description + $" Тип расчёта {t} задан пользователем."
                            : t == longType ? description + $" Длительные части {t} вариантов (пары, диаграммы вариантов), метки строк — от вариантов."
                            : description,
                    };
                    if (kind == "shell") set.ShellItems = shells[t];
                    else set.Items = beams[t];
                    scope?.Bind(set);
                    tags.Add(set.Tag);
                    sets.Add(set);
                }
            }
        }
        return new Result(sets, warnings);
    }

    /// <summary>У вариантов группы считались длительные части (группы N и C пакета с парами).</summary>
    public static bool HasLongParts(FemCsfeaResultSummary summary, string key) => LongTypeOf(key) is { } type
        && summary.BatchLong?.Any(p => p.Kind == FemCsfeaBatchLongKind.Part && p.Type == type) == true;

    /// <summary>Тип длительной части группы: N → NL, C → CL; иначе null.</summary>
    static string? LongTypeOf(string key) => CScore.Fem.Combinations.FemLongTermPair.LongType(key);

    /// <summary>Те же КЭ с нулевыми усилиями (нулевая длительная часть — строки с теми же метками).</summary>
    static RcSecantStepFields ZeroForces(RcSecantStepFields f) => new()
    {
        Stage = f.Stage, Step = f.Step, LoadFactor = f.LoadFactor,
        NodeIds = f.NodeIds, Displacements = new double[f.Displacements.Length],
        ShellIds = f.ShellIds, ShellForces = new double[f.ShellForces.Length], ShellStates = f.ShellStates, ShellFlags = f.ShellFlags,
        BeamIds = f.BeamIds, BeamForces = new double[f.BeamForces.Length], BeamFlags = f.BeamFlags,
    };

    /// <summary>«… — пластины (N)», с номером — «… — пластины (2) (N)».</summary>
    static string Tag(string baseTag, int n, string type) =>
        n == 1 ? $"{baseTag} ({type})" : string.Create(CultureInfo.InvariantCulture, $"{baseTag} ({n}) ({type})");

    /// <summary>«Огибающая C 017 (λ = 0,73)»; не посчитанный — «(не посчитан)», без полей — «(поля не записаны)».</summary>
    static string Skipped(FemCsfeaBatchSummary b) =>
        !b.Done ? $"{b.Tag} (не посчитан)"
        : !b.Completed ? string.Create(CultureInfo.CurrentCulture, $"{b.Tag} (λ = {b.LoadFactor:0.###})")
        : $"{b.Tag} (поля не записаны)";

    /// <summary>«Не вошли 7: …» — первые <see cref="ListedSkipped"/>, далее «и ещё N»; пусто — нет не вошедших.</summary>
    static string SkippedText(IReadOnlyList<string> skipped)
    {
        if (skipped.Count == 0) return "";
        string list = string.Join(", ", skipped.Take(ListedSkipped));
        if (skipped.Count > ListedSkipped)
            list += string.Create(CultureInfo.CurrentCulture, $" и ещё {skipped.Count - ListedSkipped}");
        return string.Create(CultureInfo.CurrentCulture, $"Не вошли {skipped.Count}: {list}.");
    }
}
