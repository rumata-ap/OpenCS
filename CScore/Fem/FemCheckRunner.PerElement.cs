using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CScore.Fem;

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
        return forceSets.Any(fs => FemCheckReadiness.RowElementNumbers(fs, isPlate).Any(n => n.HasValue));
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

    /// <summary>
    /// Проверка по КЭ: каждая строка усилий считается с сечением и армированием своего КЭ
    /// (<see cref="LoadItem.SourceElementNum"/>). Строки по КЭ вне цели пропускаются, строки без номера КЭ
    /// считаются с сечением цели. Каждое сочетание решается с нуля.
    /// </summary>
    /// <param name="forceSets">Наборы усилий, выбранные для проверки.</param>
    /// <param name="barExecutor">Исполнитель стержневой проверки одной строки.</param>
    /// <param name="progress">Доля выполненных строк, 0…1.</param>
    /// <exception cref="OperationCanceledException">Проверка отменена — результата нет.</exception>
    public static CalcResult RunPerElement(
        FemCheck check,
        IFemCheckable target,
        FemCheckScope scope,
        IReadOnlyList<ForceSet> forceSets,
        FemPerElementInputs inputs,
        Func<CalcTask, CrossSection, LoadItem, CalcResult> barExecutor,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
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

        // ── Задания: строка усилий × источник армирования, сгруппированные по КЭ ──────────────
        var groups = new List<ElementGroup>();
        // Группа — строки с одним сечением: у источника с армированием по сечениям КЭ — своя на каждое сечение.
        var groupByKey = new Dictionary<(string Source, int ElemNum, int Section), ElementGroup>();
        var groupsByElem = new Dictionary<(string Source, int ElemNum), List<ElementGroup>>();
        ElementGroup? targetGroup = null;   // строки без номера КЭ — сечение цели
        int total = 0, skipped = 0;

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

        for (int si = 0; si < sourceKeys.Length; si++)
        {
            foreach (var fs in forceSets)
            {
                var calcType = ExtractCalcType(fs.Tag, check.CalcTypeOverride);
                var task     = BuildCalcTask(check, target, calcType);

                if (isPlate)
                {
                    // Для auto-phi1 (SLS): NL-строки по метке; явный NL-набор имеет приоритет.
                    var nlLookup = pParams!.Phi1Mode == "auto" && pParams.Kind.Contains("sls")
                        ? BuildNlLookup(fs, forceSets) : null;
                    if (pParams.Kind == "shell_layered" && pParams.NlForceSetId > 0
                        && lookupSets.FirstOrDefault(f => f.Id == pParams.NlForceSetId) is { } nlFs)
                        nlLookup = nlFs.ShellItems.GroupBy(s => s.Label).ToDictionary(g => g.Key, g => g.First());

                    foreach (var shell in fs.ShellItems)
                    {
                        if (shell.SourceElementNum is int n && !byNum.ContainsKey(n)) { if (si == 0) skipped++; continue; }
                        if (shell.SourceElementNum == null && si > 0) continue;
                        ShellLoadItem? nlItem = null;
                        nlLookup?.TryGetValue(shell.Label, out nlItem);
                        GroupFor(si, shell.SourceElementNum, null).Jobs.Add(new Job(total++, fs, calcType, task, null, shell, nlItem));
                    }
                }
                else
                {
                    foreach (var item in fs.Items)
                    {
                        if (item.SourceElementNum is int n && !byNum.ContainsKey(n)) { if (si == 0) skipped++; continue; }
                        if (item.SourceElementNum == null && si > 0) continue;
                        GroupFor(si, item.SourceElementNum, item.SourceSectionNum).Jobs.Add(new Job(total++, fs, calcType, task, item, null, null));
                    }
                }
            }
        }

        // ── Расчёт ────────────────────────────────────────────────────────────────────────────
        var results = new CheckRow[total];
        int done = 0;
        int step = Math.Max(1, total / 200);

        void RunGroup(ElementGroup g)
        {
            // rc_check меняет состояние сечения (диаграммы, фибры) — в параллели у каждого КЭ свой клон.
            var bar = inputs.ParallelBars ? g.Bar?.Section?.CloneForCalc() : g.Bar?.Section;
            foreach (var job in g.Jobs)
            {
                if (ct.IsCancellationRequested) return;
                CheckRow row;
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
                if (d % step == 0 || d == total) progress?.Report((double)d / total);
            }
        }

        if (isPlate || inputs.ParallelBars)
            Parallel.ForEach(groups, RunGroup);
        else
            foreach (var g in groups) RunGroup(g);
        ct.ThrowIfCancellationRequested();

        // ── Агрегаты по КЭ ────────────────────────────────────────────────────────────────────
        var elementRows = new List<object>();
        var sourceSummaries = new List<object>();
        bool anyFailed = results.Any(r => !r.Passed && !r.NotChecked);
        bool anyUnchecked = results.Any(r => r.NotChecked);
        int lessThanSelected = 0;

        for (int si = 0; si < sourceKeys.Length; si++)
        {
            string source = sourceKeys[si];
            int ok = 0, failed = 0, notChecked = 0;
            foreach (var e in elements.OrderBy(e => e.ElemNum ?? int.MaxValue).ThenBy(e => e.Element.ElemTag, StringComparer.Ordinal))
            {
                var elemGroups = e.ElemNum is int n ? groupsByElem.GetValueOrDefault((source, n)) : null;
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
                    case "ok": ok++; break;
                    case "failed": failed++; break;
                    default: notChecked++; break;
                }

                elementRows.Add(new
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
                });
            }

            anyFailed |= failed > 0;
            anyUnchecked |= notChecked > 0;
            sourceSummaries.Add(new { source, passed = ok, failed, notChecked });
        }

        // ── Сводка ────────────────────────────────────────────────────────────────────────────
        var warnings = new List<string>();
        foreach (var s in plateSources)
            warnings.AddRange(s.Warnings(elements, inputs.ConcreteMat, inputs.RebarMat));
        if (lessThanSelected > 0)
            warnings.Add($"У {lessThanSelected} КЭ продольная арматура принятого сечения меньше подобранной (ASP).");

        int passedRows = results.Count(r => r.Passed);
        int notCheckedRows = results.Count(r => r.NotChecked);
        var dataJson = JsonSerializer.Serialize(new
        {
            normCode       = check.NormCode,
            memberTag      = target.Tag,
            perElement     = true,
            totalRows      = results.Length,
            passedRows,
            failedRows     = results.Length - passedRows - notCheckedRows,
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
            elements = elementRows,
            rows = results.Select(r => new
            {
                label            = r.Label,
                forceSetTag      = r.ForceSetTag,
                calcType         = r.CalcType,
                utilization      = double.IsFinite(r.Utilization) ? Math.Round(r.Utilization, 6) : (double?)null,
                passed           = r.Passed,
                notChecked       = r.NotChecked,
                worstFormula     = r.WorstFormula,
                worstDescription = r.WorstDescription,
                elemNum          = r.ElemNum,
                sectionNum       = r.SectionNum,
                sectionLabel     = r.SectionLabel,
                rebarKey         = r.RebarKey,
                rebarSource      = r.RebarSource,
            }).ToArray()
        });

        return new CalcResult
        {
            TaskId   = 0,
            TaskKind = check.NormCode,
            TaskTag  = check.DisplayTag,
            Created  = created,
            // «ok» — только когда все КЭ цели проверены и прошли; непроверенные без не прошедших — «incomplete».
            Status   = anyFailed ? "not_passed" : anyUnchecked ? "incomplete" : "ok",
            DataJson = dataJson
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

    static CheckRow NotCheckedRow(Job job, string reason) => new()
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
