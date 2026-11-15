using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CScore;
using CScore.Batch;
using CScore.Sp63Punching;
using OpenCS.Reporting;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Расчёт одной строки пакета продавливания.</summary>
/// <param name="Item">Строка набора.</param>
/// <param name="Result">Результат ядра (без трассировки).</param>
/// <param name="Reversed">N другого знака, чем преобладающий в наборе.</param>
/// <param name="Verdict">Причина строки: наибольший Кисп по вариантам контура.</param>
public sealed record PunchingBatchRow(LoadItem Item, PunchingResult Result, bool Reversed, BatchRowVerdict Verdict,
    PunchingDesignResult? Design = null);

/// <summary>Огибающая варианта контура по строкам набора.</summary>
/// <param name="Id">Идентификатор варианта.</param>
/// <param name="Kind">Вид.</param>
/// <param name="Zone">Зона.</param>
/// <param name="Max">Наибольший Кисп (+∞ — нулевая несущая способность); null — строк нет.</param>
/// <param name="Num">Номер строки набора с наибольшим Кисп.</param>
public sealed record PunchingEnvelopeEntry(string Id, PunchingVariantKind Kind, PunchingZone Zone, double? Max, int? Num);

/// <summary>Итог пакета продавливания по набору усилий.</summary>
public sealed record PunchingBatchOutcome(
    IReadOnlyList<PunchingBatchRow> Rows, int? GoverningIndex, IReadOnlyList<PunchingEnvelopeEntry> Envelope,
    double RatioConcrete, double RatioMaxSw, PunchingStatus Status, IReadOnlyList<PunchingWarning> Warnings)
{
    /// <summary>Определяющая строка (наибольший Кисп) или null.</summary>
    public PunchingBatchRow? Governing => GoverningIndex is { } g ? Rows[g] : null;
}

/// <summary>
/// Продавливание по всем строкам набора усилий («punching_batch»): та же геометрия, материалы и настройки,
/// что у одиночной задачи, усилия строки — F = |N|, Mx, My. Строки считаются ядром без трассировки,
/// определяющая — повторно с трассировкой и хранится целиком в формате одиночной задачи.
/// </summary>
public static class PunchingBatchRunner
{
    /// <summary>Вид задачи.</summary>
    public const string Kind = "punching_batch";

    /// <summary>Одиночная задача или пакет продавливания.</summary>
    public static bool IsPunchingKind(string? kind) => kind is PunchingRunner.Kind or Kind;

    /// <summary>Параметры одиночной задачи с усилиями строки набора: F = |N|, Mx, My.</summary>
    public static PunchingTaskParams RowParams(PunchingTaskParams p, LoadItem item)
        => p with { Forces = p.Forces with { F = Math.Abs(item.N), Mx = item.Mx, My = item.My } };

    /// <summary>Преобладающий знак N набора (по сумме модулей); +1 при пустом или нулевом наборе.</summary>
    public static int DominantSign(IEnumerable<LoadItem> items)
    {
        double pos = 0, neg = 0;
        foreach (var i in items)
            if (i.N > 0) pos += i.N; else neg -= i.N;
        return neg > pos ? -1 : 1;
    }

    /// <summary>Выполняет пакет; ошибки — статусом error, не исключением.</summary>
    public static CalcResult Run(CalcTask task, CalcSettings settings, TaskRunContext? ctx)
    {
        string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            var forceSet = ctx?.Database?.ForceSets.FirstOrDefault(fs => fs.Id == task.ForceSetId);
            if (forceSet is null) return PunchingRunner.Error(task, created, ["force_set_missing"]);
            var items = forceSet.Items.OrderBy(i => i.Num).ToList();
            if (items.Count == 0) return PunchingRunner.Error(task, created, ["force_set_empty"]);

            var p = PunchingTaskParams.Parse(task.ParamsJson);
            var errors = p.Validate();
            if (errors.Count > 0) return PunchingRunner.Error(task, created, errors);
            var strengths = PunchingRunner.ResolveStrengths(p, ctx?.Database?.Materials ?? [], task.CalcType, out var materialErrors);
            if (materialErrors.Count > 0) return PunchingRunner.Error(task, created, materialErrors);

            var outcome = Evaluate(p, strengths, items, settings, ctx);
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = created,
                Status = BatchVerdictSummary.PackageStatus(outcome.Rows.Select(r => r.Verdict)),
                DataJson = Serialize(task, forceSet.Tag, p, strengths, outcome)
            };
        }
        catch (Exception ex)
        {
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = created, Status = "error",
                DataJson = JsonSerializer.Serialize(new { error = ex.Message, traceVersion = 1 })
            };
        }
    }

    /// <summary>
    /// Считает все строки (параметры уже проверены, Rbt и Rsw известны). settings = null — последовательно,
    /// без прогресса (живой пересчёт окна параметров).
    /// </summary>
    public static PunchingBatchOutcome Evaluate(PunchingTaskParams p, PunchingStrengths s, IReadOnlyList<LoadItem> items,
        CalcSettings? settings = null, TaskRunContext? ctx = null)
    {
        int sign = DominantSign(items);
        var results = new PunchingResult[items.Count];
        // Вход и геометрия контуров от строки не зависят — одни на все строки, меняются только F, Mx, My.
        var baseInput = p.ToInput(s.Rbt, s.Rsw);
        var geometry = items.Count > 0 ? PunchingGeometryCache.Prepare(baseInput) : null;
        void Body(int i) => results[i] = PunchingChecker.Check(baseInput with
        {
            Forces = baseInput.Forces with { F = Math.Abs(items[i].N), Mx = items[i].Mx, My = items[i].My }
        }, includeTrace: false, geometry);
        if (settings is not null) BatchProgress.ForEachRow(items.Count, settings, ctx, Body);
        else if (items.Count > 256) Parallel.For(0, items.Count, Body);
        else for (int i = 0; i < items.Count; i++) Body(i);

        // Подбор qsw по строкам, где бетона не хватает (без ширины зоны — она ищется по худшей строке при сериализации).
        var designs = new PunchingDesignResult?[items.Count];
        void DesignBody(int i)
        {
            if (results[i].UtilizationConcreteOnly <= 1.0) return;
            designs[i] = PunchingDesign.Solve(baseInput with
            {
                Forces = baseInput.Forces with { F = Math.Abs(items[i].N), Mx = items[i].Mx, My = items[i].My }
            }, includeZone: false, geometry);
        }
        if (items.Count > 64) Parallel.For(0, items.Count, DesignBody);
        else for (int i = 0; i < items.Count; i++) DesignBody(i);

        int edgeCount = p.Edges.Count;
        var rows = new List<PunchingBatchRow>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            bool reversed = Math.Sign(items[i].N) == -sign;
            rows.Add(new PunchingBatchRow(items[i], results[i], reversed, RowVerdict(results[i], reversed, edgeCount), designs[i]));
        }
        int? governing = BatchVerdictSummary.GoverningIndex(rows.Select(r => r.Verdict).ToList());

        // Огибающая: варианты одинаковы во всех строках (геометрия общая) — берутся из первой.
        var envelope = new List<PunchingEnvelopeEntry>();
        foreach (var v in results.Length > 0 ? results[0].Variants : Array.Empty<PunchingVariantResult>())
        {
            double? max = null;
            int? num = null;
            foreach (var r in rows)
            {
                var rv = r.Result.Variants.FirstOrDefault(x => x.Variant.Id == v.Variant.Id);
                if (rv is null) continue;
                if (max is null || rv.Utilization > max) { max = rv.Utilization; num = r.Item.Num; }
            }
            envelope.Add(new PunchingEnvelopeEntry(v.Variant.Id, v.Variant.Kind, v.Variant.Zone, max, num));
        }

        double kb = results.Length > 0 ? results.Max(r => r.UtilizationConcreteOnly) : 0;
        double kmax = results.Length > 0 ? results.Max(r => r.UtilizationMaxReinforcement) : 0;
        var status = kb <= 1.0 ? PunchingStatus.ConcreteSufficient
            : kmax <= 1.0 ? PunchingStatus.ReinforcementRequired
            : PunchingStatus.NotEnsured;

        var warnings = results.SelectMany(r => r.Warnings).DistinctBy(w => w.Code + "\u0001" + w.Text).ToList();
        var reversedNums = rows.Where(r => r.Reversed).Select(r => r.Item.Num).ToList();
        if (reversedNums.Count > 0)
        {
            string list = string.Join(", ", reversedNums.Take(10)) + (reversedNums.Count > 10 ? "…" : "");
            warnings.Add(new PunchingWarning("reversed_rows",
                $"Строки с N другого знака ({reversedNums.Count}): № {list} — сила направлена в обратную сторону, " +
                "расчёт выполнен по |N|; проверьте сторону приложения нагрузки.",
                [reversedNums.Count.ToString(CultureInfo.InvariantCulture), list]));
        }
        return new PunchingBatchOutcome(rows, governing, envelope, kb, kmax, status, warnings);
    }

    /// <summary>Причина строки: наибольший Кисп по вариантам, участвующим в выборе определяющего.</summary>
    public static BatchRowVerdict RowVerdict(PunchingResult r, bool reversed, int edgeCount)
    {
        var criteria = r.Variants
            .Where(v => v.Variant.Kind != PunchingVariantKind.PylonReduced)
            .Select(v =>
            {
                string title = PunchingReportText.VariantTitle(v.Variant.Kind, v.Variant.Zone, v.Variant.Id, edgeCount);
                string text = $"{title}: Кисп = {K(v.Utilization)}" + (reversed ? "; N другого знака" : "");
                return new BatchCriterion(v.Variant.Id, v.Utilization, Reference(v.Variant.Kind, v.Variant.Zone), text);
            })
            .ToArray();
        return criteria.Length == 0
            ? BatchRowVerdict.Error("Не построено ни одного расчётного контура.")
            : BatchRowVerdict.Worst(criteria);
    }

    /// <summary>Кисп для текста причины — округление вверх, как в окне и отчёте продавливания.</summary>
    static string K(double k) => double.IsFinite(k) ? ReportNumberFormatter.FormatUtilization(k).Replace('.', ',') : "∞";

    /// <summary>Пункт и рисунок нормы для варианта контура.</summary>
    public static string Reference(PunchingVariantKind kind, PunchingZone zone)
    {
        if (zone == PunchingZone.BeyondReinforcement) return "СП 63.13330.2018, п. 8.1.48, рис. 8.13";
        return kind switch
        {
            PunchingVariantKind.Closed => "СП 63.13330.2018, п. 8.1.46, рис. 8.11а",
            PunchingVariantKind.OpenEdge => "СП 63.13330.2018, п. 8.1.46, рис. 8.11б",
            PunchingVariantKind.OpenCorner => "СП 63.13330.2018, п. 8.1.46, рис. 8.11в",
            PunchingVariantKind.PylonBroken => "СП 430.1325800, п. 6.3.8, рис. 6.1а",
            _ => "СП 63.13330.2018, п. 8.1.46"
        };
    }

    static string StatusCode(PunchingStatus s) => s switch
    {
        PunchingStatus.ConcreteSufficient => "concrete_sufficient",
        PunchingStatus.ReinforcementRequired => "reinforcement_required",
        _ => "not_ensured"
    };

    /// <summary>JSON результата пакета (см. спеку среза 2).</summary>
    public static string Serialize(CalcTask task, string forceSetTag, PunchingTaskParams p, PunchingStrengths s, PunchingBatchOutcome o)
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["forceSetTag"] = forceSetTag,
            ["total"] = o.Rows.Count,
            ["calcType"] = task.CalcType.ToString()
        };

        JsonNode? detail = null;
        if (o.Governing is { } gov)
        {
            // Определяющая строка — с трассировкой, в формате одиночной задачи.
            var rp = RowParams(p, gov.Item);
            var input = rp.ToInput(s.Rbt, s.Rsw);
            var full = PunchingChecker.Check(input);
            detail = JsonNode.Parse(PunchingRunner.Serialize(task, rp, s, input, full));
            detail!["forceRow"] = new JsonObject { ["num"] = gov.Item.Num, ["label"] = gov.Item.Label, ["N"] = gov.Item.N };

            var envelope = o.Envelope.ToDictionary(e => e.Id, e => e.Max);
            var scene = PunchingScene.From(input, full);
            scene = scene with
            {
                Variants = scene.Variants.Select(v => v with
                {
                    Utilization = envelope.GetValueOrDefault(v.Id) ?? v.Utilization,
                    Governs = v.Id == gov.Verdict.Code
                }).ToList()
            };
            root["scene"] = JsonNode.Parse(PunchingSceneJson.ToJson(scene));
            if (detail["inputs"] is JsonObject inputs)
            {
                var copy = inputs.DeepClone().AsObject();
                copy.Remove("F"); copy.Remove("Mx"); copy.Remove("My");
                root["inputs"] = copy;
            }
            if (detail["traceCommon"] is JsonArray common)
                root["traceCommon"] = new JsonArray(common
                    .Where(n => n?["stepId"]?.GetValue<string>() is not ("sp63.punching.mloc_x" or "sp63.punching.mloc_y" or "sp63.punching.fred"))
                    .Select(n => n!.DeepClone()).ToArray());
        }

        // Подбор — по строке с наибольшим требованием (∞ — «не обеспечить» / «раскладка не работает»), с шириной зоны.
        var worst = o.Rows.Where(r => r.Design is not null).MaxBy(r => r.Design!.Lambda);
        if (worst is not null)
        {
            var wp = RowParams(p, worst.Item);
            var wd = PunchingDesign.Solve(wp.ToInput(s.Rbt, s.Rsw));
            root["design"] = JsonNode.Parse(PunchingResultJson.WriteDesign(wd));
            root["design_num"] = worst.Item.Num;
            root["design_label"] = worst.Item.Label;
            root["design_rows"] = o.Rows.Count(r => r.Design is not null);
        }

        var g = o.Governing;
        double? util = g?.Verdict.Util;
        root["passed"] = g is not null && g.Verdict.IsPassed && o.Rows.All(r => r.Verdict.IsPassed);
        root["utilization"] = Finite(util);
        root["utilization_inf"] = util is { } u0 && double.IsPositiveInfinity(u0);
        root["governing_num"] = g?.Item.Num;
        root["governing_label"] = g?.Item.Label;
        root["governing_variant"] = g?.Verdict.Code;
        root["status"] = StatusCode(o.Status);
        root["ratio_concrete"] = Finite(o.RatioConcrete);
        root["ratio_max_sw"] = Finite(o.RatioMaxSw);
        root["verdict_summary"] = JsonSerializer.SerializeToNode(
            BatchVerdictSummary.ToJson(o.Rows.Select(r => r.Verdict).ToList(), o.Rows.Select(r => r.Item.Num).ToList()));

        root["variants"] = new JsonArray(o.Envelope.Select(e => (JsonNode)new JsonObject
        {
            ["id"] = e.Id, ["kind"] = e.Kind.ToString(), ["zone"] = e.Zone.ToString(),
            ["envelope"] = Finite(e.Max), ["envelope_inf"] = e.Max is { } m && double.IsPositiveInfinity(m),
            ["envelope_num"] = e.Num
        }).ToArray());

        var rows = new JsonArray();
        foreach (var r in o.Rows)
        {
            var ratios = new JsonObject();
            foreach (var v in r.Result.Variants) ratios[v.Variant.Id] = Finite(v.Utilization);
            rows.Add(new JsonObject
            {
                ["force_item_id"] = r.Item.Id, ["num"] = r.Item.Num, ["label"] = r.Item.Label,
                ["N"] = r.Item.N, ["Mx"] = r.Item.Mx, ["My"] = r.Item.My, ["F"] = Math.Abs(r.Item.N),
                ["fred"] = Finite(r.Result.Fred),
                ["ratio"] = Finite(r.Verdict.Util),
                ["governing"] = r.Verdict.Code,
                ["ratios"] = ratios,
                ["ratio_concrete"] = Finite(r.Result.UtilizationConcreteOnly),
                ["ratio_max_sw"] = Finite(r.Result.UtilizationMaxReinforcement),
                ["node_status"] = StatusCode(r.Result.Status),
                ["design_state"] = r.Design is { } rd ? DesignCode(rd.State) : null,
                ["qsw_req"] = r.Design is { Cross: false } du ? Finite(du.QswReq) : null,
                ["qsw_x_req"] = r.Design is { Cross: true } dx ? Finite(dx.QswYReq) : null,
                ["qsw_y_req"] = r.Design is { Cross: true } dy ? Finite(dy.QswXReq) : null,
                ["reversed"] = r.Reversed,
                ["status"] = r.Verdict.Status,
                ["verdict"] = JsonSerializer.SerializeToNode(r.Verdict.ToJson())
            });
        }
        root["rows"] = rows;

        var warnings = new JsonArray();
        foreach (var w in o.Warnings)
        {
            var wo = new JsonObject { ["code"] = w.Code, ["text"] = w.Text };
            if (w.Args is { Count: > 0 } args) wo["args"] = new JsonArray(args.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray());
            warnings.Add(wo);
        }
        foreach (var w in s.Warnings) warnings.Add(new JsonObject { ["code"] = "material", ["text"] = w });
        root["warnings"] = warnings;
        if (detail is not null) root["governing_detail"] = detail;
        return root.ToJsonString();
    }

    static JsonNode? Finite(double? v) => v is double x && double.IsFinite(x) ? JsonValue.Create(x) : null;

    static string DesignCode(PunchingDesignState s) => s switch
    {
        PunchingDesignState.NotRequired => "not_required",
        PunchingDesignState.ByCalculation => "by_calculation",
        PunchingDesignState.ByThreshold => "by_threshold",
        PunchingDesignState.NotEnsured => "not_ensured",
        _ => "layout_ineffective"
    };
}

/// <summary>Обработчик вида «punching_batch».</summary>
public sealed class PunchingBatchHandler : ITaskHandler
{
    /// <inheritdoc/>
    public string Kind => PunchingBatchRunner.Kind;

    /// <inheritdoc/>
    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item, CalcSettings settings, TaskRunContext? ctx = null)
        => PunchingBatchRunner.Run(task, settings, ctx);
}
