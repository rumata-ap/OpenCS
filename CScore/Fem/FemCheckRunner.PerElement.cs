using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CScore.Fem;

/// <summary>Приёмник строк результата проверки по КЭ: получает их порциями по мере расчёта
/// (запись в БД без накопления в памяти). Вызывается из потока расчёта.</summary>
public interface IFemCheckRowSink
{
    /// <summary>Порция строк результата.</summary>
    void Write(IReadOnlyList<FemCheckRow> rows);
}

/// <summary>Сечения и армирование для проверки по КЭ (<see cref="FemCheckRunner.RunPerElement"/>).</summary>
public sealed class FemPerElementInputs
{
    /// <summary>Сечение цели для стержней: у КЭ без своего сечения и у строк без номера КЭ.</summary>
    public CrossSection? TargetBarSection { get; init; }
    /// <summary>Расчётное сечение по id (сечения КЭ сетки и конструктивных элементов).</summary>
    public Func<int, CrossSection?> BarSectionById { get; init; } = _ => null;
    /// <summary>
    /// Считать стержневые КЭ параллельно, каждый на клоне сечения. Только для проверок, которым
    /// хватает <see cref="CrossSection.CloneForCalc"/> (клон не несёт привязку параметрического профиля).
    /// </summary>
    public bool ParallelBars { get; init; }
    /// <summary>Подобранная продольная арматура стержней по номеру КЭ, см² (справочно).</summary>
    public IReadOnlyDictionary<int, double> BarSelectedAsCm2 { get; init; } = new Dictionary<int, double>();
    /// <summary>Источники сечения стержневых КЭ в порядке расчёта; пусто — сечение проекта
    /// (<see cref="BarSectionById"/>, <see cref="TargetBarSection"/>).</summary>
    public IReadOnlyList<IBarElementSectionSource> BarSources { get; init; } = [];

    /// <summary>Сечение-шаблон пластины цели: бетон, материалы, модель; у строк без номера КЭ — само сечение.</summary>
    public PlateSection? PlateTemplate { get; init; }
    /// <summary>Источники армирования пластинчатых КЭ в порядке расчёта; пусто — одно сечение цели.</summary>
    public IReadOnlyList<IPlateElementSectionSource> PlateSources { get; init; } = [];
    /// <summary>Материал бетона сечения-шаблона.</summary>
    public Material? ConcreteMat { get; init; }
    /// <summary>Материал арматуры сечения-шаблона.</summary>
    public Material? RebarMat { get; init; }

    /// <summary>Наборы, среди которых ищется явно заданный NL-набор (п. 8.2.7); пусто — наборы проверки.</summary>
    public IReadOnlyList<ForceSet> LookupForceSets { get; init; } = [];
}

public static partial class FemCheckRunner
{
    const string PlateCheckCode = "rc_plate_check";

    /// <summary>КЭ цели того вида, к которому относится проверка: пластины или стержни.</summary>
    public static IReadOnlyList<FemCheckScopeElement> ScopeElements(FemCheck check, FemCheckScope scope)
    {
        bool isPlate = check.NormCode == PlateCheckCode;
        return scope.Elements.Where(e => (e.Element.ElemType == "shell") == isPlate).ToList();
    }

    /// <summary>В строках наборов есть номера КЭ — проверку можно вести по КЭ.</summary>
    public static bool HasElementNumbers(FemCheck check, IEnumerable<ForceSet> forceSets)
    {
        bool isPlate = check.NormCode == PlateCheckCode;
        return forceSets.Any(fs => fs.ElementStats(isPlate).HasElementRows);
    }

    /// <summary>Готовность цели к проверке по КЭ (спека §6.6) — без решателей.</summary>
    public static FemCheckReadiness EvaluateReadiness(
        FemCheck check, FemCheckScope scope, IReadOnlyList<ForceSet> forceSets, FemPerElementInputs inputs)
    {
        bool isPlate = check.NormCode == PlateCheckCode;
        var elements = ScopeElements(check, scope);
        if (!isPlate)
            return FemCheckReadiness.Evaluate(false, elements, forceSets,
                [.. BarSources(inputs).Select(s => new FemCheckSectionProbe(s.Key, s.MissingReason))]);

        var sources = PlateSources(inputs);
        return FemCheckReadiness.Evaluate(true, elements, forceSets,
            [.. sources.Select(s => new FemCheckSectionProbe(s.Key, e =>
            {
                var r = s.Resolve(e);
                return r.Section == null ? r.Reason ?? "нет армирования" : null;
            }))],
            inputs.PlateTemplate == null ? "Не задано пластинчатое сечение цели (назначается в редакторе пластины или группы)" : null);
    }

    /// <summary>Строк усилий в порции проверки по КЭ (на каждый источник армирования).</summary>
    public const int DefaultChunkRows = 100_000;

    /// <summary>
    /// Проверка по КЭ: каждая строка усилий считается с сечением и армированием своего КЭ
    /// (<see cref="LoadItem.SourceElementNum"/>). Строки по КЭ вне цели пропускаются, строки без номера КЭ
    /// считаются с сечением цели. Каждое сочетание решается с нуля.
    /// <para>
    /// Строки идут порциями по диапазонам номеров КЭ (около <paramref name="chunkRows"/> строк): у наборов,
    /// строки которых не загружены, читаются из БД только строки порции. С <paramref name="rowSink"/> строки
    /// результата уходят в него после каждой порции и в памяти не копятся — пиковая память не зависит от
    /// размера РСУ; без него — собираются в <see cref="CalcResult.FemCheckRows"/>.
    /// </para>
    /// </summary>
    /// <param name="forceSets">Наборы усилий, выбранные для проверки.</param>
    /// <param name="barExecutor">Исполнитель стержневой проверки одной строки.</param>
    /// <param name="progress">Доля выполненных строк, 0…1.</param>
    /// <param name="rowSink">Приёмник строк результата; null — строки в результате.</param>
    /// <param name="chunkRows">Строк усилий в порции.</param>
    /// <exception cref="OperationCanceledException">Проверка отменена — результата нет.</exception>
    public static CalcResult RunPerElement(
        FemCheck check,
        IFemCheckable target,
        FemCheckScope scope,
        IReadOnlyList<ForceSet> forceSets,
        FemPerElementInputs inputs,
        Func<CalcTask, CrossSection, LoadItem, CalcResult> barExecutor,
        IProgress<double>? progress = null,
        CancellationToken ct = default,
        IFemCheckRowSink? rowSink = null,
        int chunkRows = DefaultChunkRows)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        bool isPlate = check.NormCode == PlateCheckCode;

        if (forceSets.Count == 0)
            return MakeError(check, created, target.Tag, "Нет наборов усилий для проверки");

        var readiness = EvaluateReadiness(check, scope, forceSets, inputs);
        if (readiness.BlockingReason != null)
            return MakeError(check, created, target.Tag, readiness.BlockingReason);

        var elements = ScopeElements(check, scope);
        var byNum = new Dictionary<int, FemCheckScopeElement>();
        foreach (var e in elements)
            if (e.ElemNum is int n) byNum.TryAdd(n, e);

        var plateSources = isPlate ? PlateSources(inputs) : [];
        var barSources = isPlate ? [] : BarSources(inputs);
        string[] sourceKeys = isPlate ? [.. plateSources.Select(s => s.Key)] : [.. barSources.Select(s => s.Key)];
        var pParams = isPlate ? PlateCheckParams.Parse(check.ParamsJson) : null;
        var lookupSets = inputs.LookupForceSets.Count > 0 ? inputs.LookupForceSets : forceSets;

        // NL-набор строки (п. 8.2.7): явный NL-набор имеет приоритет над парным «(NL)» для auto-phi1 (SLS).
        var explicitNl = isPlate && pParams!.Kind == "shell_layered" && pParams.NlForceSetId > 0
            ? lookupSets.FirstOrDefault(f => f.Id == pParams.NlForceSetId) : null;
        bool autoNl = isPlate && pParams!.Phi1Mode == "auto" && pParams.Kind.Contains("sls");
        var nlSetOf = forceSets.ToDictionary(fs => fs, fs => explicitNl ?? (autoNl ? FindNlSet(fs, forceSets) : null));

        // ── Порции: диапазоны номеров КЭ цели по статистике наборов (без загрузки строк) ──────────
        var rowsByElem = new SortedDictionary<int, int>();
        int skipped = 0, rowsWithoutElement = 0;
        foreach (var fs in forceSets)
        {
            var stats = fs.ElementStats(isPlate);
            rowsWithoutElement += stats.WithoutElement;
            foreach (var (n, count) in stats.ByElement)
                if (byNum.ContainsKey(n)) rowsByElem[n] = rowsByElem.GetValueOrDefault(n) + count;
                else skipped += count;
        }
        var chunks = new List<(int? From, int? To)>();
        int chunkStart = 0, chunkSize = 0;
        foreach (var (n, count) in rowsByElem)
        {
            if (chunkSize == 0) chunkStart = n;
            chunkSize += count;
            if (chunkSize >= chunkRows) { chunks.Add((chunkStart, n)); chunkSize = 0; }
        }
        if (chunkSize > 0) chunks.Add((chunkStart, rowsByElem.Keys.Last()));
        if (rowsWithoutElement > 0) chunks.Add((null, null));   // строки без номера КЭ — сечение цели

        int total = rowsByElem.Values.Sum() * sourceKeys.Length + rowsWithoutElement;
        int step = Math.Max(1, total / 200);
        int done = 0, emitted = 0, passedRows = 0, notCheckedRows = 0;
        bool anyFailed = false, anyUnchecked = false;
        var collected = rowSink == null ? new List<FemCheckRow>() : null;

        // Агрегаты по КЭ: по источнику — строки КЭ в порядке номеров (сортируются в конце).
        var elementRows = sourceKeys.Select(_ => new List<(FemCheckScopeElement Element, object Row)>()).ToArray();
        var okCount = new int[sourceKeys.Length];
        var failedCount = new int[sourceKeys.Length];
        var notCheckedCount = new int[sourceKeys.Length];
        int lessThanSelected = 0;
        var aggregated = new HashSet<FemCheckScopeElement>();

        // Строки NL-наборов без номера КЭ нужны каждой порции — читаются один раз.
        var nlWithoutElement = new Dictionary<ForceSet, List<ShellLoadItem>>();

        foreach (var (from, to) in chunks)
        {
            ct.ThrowIfCancellationRequested();
            bool withoutElement = from == null;

            // ── Задания порции: строка усилий × источник армирования, сгруппированные по КЭ ──────
            var groups = new List<ElementGroup>();
            // Группа — строки с одним сечением: у источника с армированием по сечениям КЭ — своя на каждое сечение.
            var groupByKey = new Dictionary<(string Source, int ElemNum, int Section), ElementGroup>();
            var groupsByElem = new Dictionary<(string Source, int ElemNum), List<ElementGroup>>();
            ElementGroup? targetGroup = null;   // строки без номера КЭ — сечение цели
            int count = 0;

            ElementGroup GroupFor(int sourceIndex, int? elemNum, int? sectionNum)
            {
                if (elemNum is not int n)
                {
                    if (targetGroup == null)
                    {
                        targetGroup = isPlate
                            ? new ElementGroup(FemCheckRebarSource.Section, null, null,
                                new PlateElementSection(inputs.PlateTemplate, inputs.PlateTemplate!.Tag, "", null), null)
                            : new ElementGroup(sourceKeys[0], null, null, null,
                                new BarElementSection(inputs.TargetBarSection, inputs.TargetBarSection?.Tag ?? "",
                                    inputs.TargetBarSection == null ? "нет расчётного сечения" : null));
                        groups.Add(targetGroup);
                    }
                    return targetGroup;
                }

                string source = sourceKeys[sourceIndex];
                int sectionKey = !isPlate && barSources[sourceIndex].PerSection ? sectionNum ?? 0 : 0;
                if (!groupByKey.TryGetValue((source, n, sectionKey), out var g))
                {
                    var e = byNum[n];
                    g = isPlate
                        ? new ElementGroup(source, n, e, plateSources[sourceIndex].Resolve(e), null)
                        : new ElementGroup(source, n, e, null,
                            barSources[sourceIndex].Resolve(e, sectionKey > 0 ? sectionKey : null));
                    groupByKey[(source, n, sectionKey)] = g;
                    if (!groupsByElem.TryGetValue((source, n), out var list))
                        groupsByElem[(source, n)] = list = [];
                    list.Add(g);
                    groups.Add(g);
                }
                return g;
            }

            // NL-строки по метке: строки NL-набора тех же КЭ и строки без номера КЭ.
            var nlLookups = new Dictionary<ForceSet, Dictionary<string, ShellLoadItem>>();
            Dictionary<string, ShellLoadItem>? NlLookup(ForceSet fs)
            {
                if (nlSetOf[fs] is not { } nlSet) return null;
                if (nlLookups.TryGetValue(nlSet, out var lookup)) return lookup;
                if (!nlWithoutElement.TryGetValue(nlSet, out var without))
                    nlWithoutElement[nlSet] = without = nlSet.ShellRowsOfElements(null, null);
                var rows = withoutElement ? without : nlSet.ShellRowsOfElements(from, to).Concat(without);
                // Метки строк могут повторяться (ручные наборы) — берём первую.
                lookup = new Dictionary<string, ShellLoadItem>();
                foreach (var r in rows) lookup.TryAdd(r.Label, r);
                return nlLookups[nlSet] = lookup;
            }

            foreach (var fs in forceSets)
            {
                var calcType = ExtractCalcType(fs.Tag, check.CalcTypeOverride);
                var task     = BuildCalcTask(check, target, calcType);
                // Строки без номера КЭ считаются один раз — с сечением цели.
                int sourceCount = withoutElement ? 1 : sourceKeys.Length;

                if (isPlate)
                {
                    var rows = fs.ShellRowsOfElements(from, to);
                    if (rows.Count == 0) continue;
                    var nlLookup = NlLookup(fs);
                    for (int si = 0; si < sourceCount; si++)
                        foreach (var shell in rows)
                        {
                            if (shell.SourceElementNum is int n && !byNum.ContainsKey(n)) continue;
                            ShellLoadItem? nlItem = null;
                            nlLookup?.TryGetValue(shell.Label, out nlItem);
                            GroupFor(si, shell.SourceElementNum, null).Jobs.Add(new Job(count++, fs, calcType, task, null, shell, nlItem));
                        }
                }
                else
                {
                    var rows = fs.BarRowsOfElements(from, to);
                    for (int si = 0; si < sourceCount; si++)
                        foreach (var item in rows)
                        {
                            if (item.SourceElementNum is int n && !byNum.ContainsKey(n)) continue;
                            GroupFor(si, item.SourceElementNum, item.SourceSectionNum).Jobs.Add(new Job(count++, fs, calcType, task, item, null, null));
                        }
                }
            }

            // ── Расчёт порции ─────────────────────────────────────────────────────────────────
            var results = new FemCheckRow[count];

            void RunGroup(ElementGroup g)
            {
                // rc_check меняет состояние сечения (диаграммы, фибры) — в параллели у каждого КЭ свой клон.
                var bar = inputs.ParallelBars ? g.Bar?.Section?.CloneForCalc() : g.Bar?.Section;
                foreach (var job in g.Jobs)
                {
                    if (ct.IsCancellationRequested) return;
                    FemCheckRow row;
                    if (isPlate)
                    {
                        // Оси армирования КЭ могут не совпадать с осями выдачи его усилий (раскладка OpenCS).
                        double angle = g.Plate!.ForceAngleDeg;
                        var shell = angle == 0 ? job.Shell! : ShellForceTransform.Rotate(job.Shell!, angle);
                        var nl = angle == 0 || job.Nl == null ? job.Nl : ShellForceTransform.Rotate(job.Nl, angle);
                        row = g.Plate.Section is { } plate
                            ? CheckPlateRow(check, plate, shell, job.ForceSet.Tag, job.CalcType,
                                            inputs.ConcreteMat, inputs.RebarMat, nl)
                            : NotCheckedRow(job, "Нет армирования: " + (g.Plate.Reason ?? ""));
                    }
                    else
                        row = bar != null
                            ? CheckBarRow(barExecutor, job.Task, bar, job.Bar!, job.ForceSet.Tag, job.CalcType)
                            : NotCheckedRow(job, "Нет расчётного сечения" + (g.Bar?.Reason is { Length: > 0 } why ? ": " + why : ""));

                    results[job.Index] = row with
                    {
                        ElemNum      = g.ElemNum,
                        SectionNum   = job.Bar?.SourceSectionNum ?? job.Shell?.SourceSectionNum,
                        SectionLabel = isPlate ? g.Plate!.Label : g.Bar!.Label,
                        RebarKey     = g.Plate?.RebarKey ?? "",
                        RebarSource  = g.Source,
                    };
                    int d = Interlocked.Increment(ref done);
                    if (d % step == 0 || d == total) progress?.Report(Math.Min(1.0, (double)d / total));
                }
            }

            if (isPlate || inputs.ParallelBars)
                Parallel.ForEach(groups, RunGroup);
            else
                foreach (var g in groups) RunGroup(g);
            ct.ThrowIfCancellationRequested();

            // ── Агрегаты по КЭ порции: все строки КЭ — в этой порции ─────────────────────────────
            if (!withoutElement)
                foreach (var e in elements)
                    if (e.ElemNum is int n && n >= from && n <= to)
                        AggregateElement(e, si => groupsByElem.GetValueOrDefault((sourceKeys[si], n)), results);

            foreach (var r in results)
            {
                if (r.Passed) passedRows++;
                else if (r.NotChecked) { notCheckedRows++; anyUnchecked = true; }
                else anyFailed = true;
            }
            emitted += results.Length;
            if (rowSink != null) rowSink.Write(results);
            else collected!.AddRange(results);
        }

        // КЭ без строк усилий (и без номера) — тоже в агрегат: «нет усилий» или «нет армирования».
        foreach (var e in elements)
            if (!aggregated.Contains(e))
                AggregateElement(e, _ => null, []);

        void AggregateElement(FemCheckScopeElement e, Func<int, List<ElementGroup>?> groupsOf, FemCheckRow[] results)
        {
            aggregated.Add(e);
            for (int si = 0; si < sourceKeys.Length; si++)
            {
                string source = sourceKeys[si];
                var elemGroups = groupsOf(si);
                var rows = elemGroups == null ? [] : elemGroups.SelectMany(g => g.Jobs).Select(j => results[j.Index]).ToList();

                string label, status;
                string? reason = null;
                double? asProvided = null, asSelected = null;
                if (isPlate)
                {
                    var section = elemGroups?[0].Plate ?? plateSources[si].Resolve(e);
                    label = section.Label;
                    status = section.Section == null ? "no_rebar" : "";
                    reason = section.Section == null ? section.Reason : null;
                }
                else
                {
                    // У источника с армированием по сечениям КЭ сечений несколько: КЭ без сечения — когда нет ни одного.
                    var sections = elemGroups?.Select(g => g.Bar!).ToList() ?? [barSources[si].Resolve(e, null)];
                    var withSection = sections.Where(s => s.Section != null).ToList();
                    label = (withSection.Count > 0 ? withSection[0] : sections[0]).Label;
                    status = withSection.Count == 0 ? "no_section" : "";
                    reason = withSection.Count == 0 ? sections[0].Reason : null;
                    if (withSection.Count > 0) asProvided = withSection.Max(s => RebarAreaCm2(s.Section!));
                    if (e.ElemNum is int num && inputs.BarSelectedAsCm2.TryGetValue(num, out double sel)) asSelected = sel;
                    // Сравнение с подбором имеет смысл для принятого и заданного армирования, не для самого подбора.
                    if (source != FemCheckRebarSource.Selected
                        && asProvided is double ap && asSelected is double asl && ap < asl - 1e-6) lessThanSelected++;
                }

                int rowsNotChecked = rows.Count(r => r.NotChecked);
                bool elemFailed = rows.Any(r => !r.Passed && !r.NotChecked);
                if (status == "")
                    status = rows.Count == 0 ? "no_forces" : elemFailed ? "failed" : rowsNotChecked > 0 ? "not_checked" : "ok";

                // Определяющая строка: не пройденная без конечного коэффициента, иначе с наибольшим.
                var governing = rows.FirstOrDefault(r => !r.Passed && !r.NotChecked && !double.IsFinite(r.Utilization))
                             ?? rows.Where(r => double.IsFinite(r.Utilization)).MaxBy(r => r.Utilization);
                double? utilMax = governing != null && double.IsFinite(governing.Utilization)
                    ? Math.Round(governing.Utilization, 6) : null;

                switch (status)
                {
                    case "ok": okCount[si]++; break;
                    case "failed": failedCount[si]++; break;
                    default: notCheckedCount[si]++; break;
                }

                elementRows[si].Add((e, new
                {
                    elemNum          = e.ElemNum,
                    elemTag          = e.Element.ElemTag,
                    memberTag        = e.Member?.ElemTag,
                    rebarSource      = source,
                    status,
                    reason,
                    utilMax,
                    passed           = status == "ok",
                    forceSetTag      = governing?.ForceSetTag,
                    label            = governing?.Label,
                    worstFormula     = governing?.WorstFormula,
                    worstDescription = governing?.WorstDescription,
                    rows             = rows.Count,
                    notChecked       = rowsNotChecked,
                    sectionLabel     = label,
                    asSelected       = asSelected is double s1 ? Math.Round(s1, 3) : (double?)null,
                    asProvided       = asProvided is double p1 ? Math.Round(p1, 3) : (double?)null,
                }));
            }
        }

        var sourceSummaries = new List<object>();
        for (int si = 0; si < sourceKeys.Length; si++)
        {
            anyFailed |= failedCount[si] > 0;
            anyUnchecked |= notCheckedCount[si] > 0;
            sourceSummaries.Add(new { source = sourceKeys[si], passed = okCount[si], failed = failedCount[si], notChecked = notCheckedCount[si] });
        }
        var orderedElementRows = elementRows
            .SelectMany(list => list
                .OrderBy(x => x.Element.ElemNum ?? int.MaxValue)
                .ThenBy(x => x.Element.Element.ElemTag, StringComparer.Ordinal)
                .Select(x => x.Row))
            .ToList();

        // ── Сводка ────────────────────────────────────────────────────────────────────────────
        var warnings = new List<string>();
        foreach (var s in plateSources)
            warnings.AddRange(s.Warnings(elements, inputs.ConcreteMat, inputs.RebarMat));
        if (lessThanSelected > 0)
            warnings.Add($"У {lessThanSelected} КЭ продольная арматура принятого сечения меньше подобранной.");

        var dataJson = JsonSerializer.Serialize(new
        {
            normCode       = check.NormCode,
            memberTag      = target.Tag,
            perElement     = true,
            totalRows      = emitted,
            passedRows,
            failedRows     = emitted - passedRows - notCheckedRows,
            notCheckedRows,
            skippedRows    = skipped,
            rebarSources   = isPlate || inputs.BarSources.Count > 0 ? sourceKeys : [],
            summary = new
            {
                elementsTotal         = elements.Count,
                elementsWithForces    = readiness.ElementsWithForces,
                elementsWithoutForces = FemCheckReadiness.FormatRanges(readiness.ElementsWithoutForces),
                rowsWithoutElement    = readiness.RowsWithoutElement,
                sources               = sourceSummaries,
                warnings,
            },
            elements = orderedElementRows,
            rowsStored     = true,
        });

        return new CalcResult
        {
            TaskId   = 0,
            TaskKind = check.NormCode,
            TaskTag  = check.DisplayTag,
            Created  = created,
            // «ok» — только когда все КЭ цели проверены и прошли; непроверенные без не прошедших — «incomplete».
            Status   = anyFailed ? "not_passed" : anyUnchecked ? "incomplete" : "ok",
            DataJson = dataJson,
            FemCheckRows = collected,
        };
    }

    /// <summary>Источники армирования пластин; пусто — одно сечение цели.</summary>
    static IReadOnlyList<IPlateElementSectionSource> PlateSources(FemPerElementInputs inputs) =>
        inputs.PlateSources.Count > 0 ? inputs.PlateSources
        : inputs.PlateTemplate != null ? [new TemplatePlateSectionSource(inputs.PlateTemplate)]
        : [];

    /// <summary>Источники сечения стержней; пусто — сечение проекта (своё → конструктивного элемента → цели).</summary>
    static IReadOnlyList<IBarElementSectionSource> BarSources(FemPerElementInputs inputs) =>
        inputs.BarSources.Count > 0 ? inputs.BarSources
        : [new ProjectBarSectionSource("", inputs.BarSectionById, inputs.TargetBarSection)];

    /// <summary>Суммарная площадь точечной (стержневой) арматуры сечения, см².</summary>
    static double RebarAreaCm2(CrossSection section) =>
        section.Areas.Sum(a => a.Fibers.Where(f => f.TypeFiber == FiberType.point).Sum(f => f.Area)) * 1e4;

    static FemCheckRow NotCheckedRow(Job job, string reason) => new()
    {
        Label            = job.Bar?.Label ?? job.Shell?.Label ?? "",
        ForceSetTag      = job.ForceSet.Tag,
        CalcType         = job.CalcType.ToString(),
        Utilization      = double.NaN,
        Passed           = false,
        NotChecked       = true,
        WorstDescription = reason
    };

    /// <summary>Строка усилий к расчёту.</summary>
    sealed record Job(int Index, ForceSet ForceSet, CalcType CalcType, CalcTask Task,
                      LoadItem? Bar, ShellLoadItem? Shell, ShellLoadItem? Nl);

    /// <summary>Строки одного КЭ для одного источника армирования: у всех одно сечение.</summary>
    sealed class ElementGroup(string source, int? elemNum, FemCheckScopeElement? element,
                              PlateElementSection? plate, BarElementSection? bar)
    {
        public string Source => source;
        public int? ElemNum => elemNum;
        public FemCheckScopeElement? Element => element;
        public PlateElementSection? Plate => plate;
        public BarElementSection? Bar => bar;
        public List<Job> Jobs { get; } = [];
    }
}
