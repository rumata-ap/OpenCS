using System.Text.Json;
using CScore;
using CScore.Batch;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Проверка стального элемента по СП 16 по всем строкам набора усилий задачи: каждая строка — та же проверка,
/// что у одиночной задачи «steel_check» (вид проверки выбирается по усилиям строки), с параметрами пакета.
/// В строке — Кисп по группам проверок (прочность, общая устойчивость, местная устойчивость, гибкость)
/// и определяющий пункт.
/// </summary>
public sealed class SteelCheckBatchHandler : ITaskHandler
{
    public const string KindCode = "steel_check_batch";

    public string Kind => KindCode;

    static readonly SteelCheckHandler Single = new();

    /// <summary>Пункт местной устойчивости: 7.3 (центрально сжатые), 8.5 (изгибаемые), 9.4 (сжато-изогнутые).</summary>
    public static bool IsLocalStability(string clause) =>
        clause.StartsWith("7.3", StringComparison.Ordinal) || clause.StartsWith("8.5", StringComparison.Ordinal)
        || clause.StartsWith("9.4", StringComparison.Ordinal);

    /// <summary>Группа проверки для столбцов окна: strength, stability (общая), local, constructive.</summary>
    public static string Group(string clause, string category) =>
        category == "stability" && IsLocalStability(clause) ? "local" : category;

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item, CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            if (ctx?.Database is null)
                throw new InvalidOperationException("Для steel_check_batch требуется контекст с DatabaseService.");
            var forceSet = ctx.Database.ForceSets.FirstOrDefault(fs => fs.Id == task.ForceSetId)
                ?? throw new InvalidOperationException($"Набор усилий id={task.ForceSetId} не найден.");

            var items = forceSet.Items;
            var rows = new object[items.Count];
            var verdicts = new BatchRowVerdict[items.Count];
            string sectionTag = section.Tag, steelTag = "";
            object? context = null;
            // Примечания пакета — общие для всех посчитанных строк (профиль, вид проверки); построчные (φe и т. п.)
            // остаются в одиночной задаче по строке.
            HashSet<string>? notes = null;
            var noteOrder = new List<string>();
            var gate = new object();

            BatchProgress.ForEachRow(items.Count, settings, ctx, i =>
            {
                var fi = items[i];
                var single = new CalcTask
                {
                    Id = task.Id, Tag = task.Tag, Kind = "steel_check", SectionId = task.SectionId,
                    ForceSetId = task.ForceSetId, ForceItemId = fi.Id, CalcType = task.CalcType, ParamsJson = task.ParamsJson
                };
                var r = Single.Run(single, section, fi, settings, ctx);
                var row = Row(fi, r, out verdicts[i], out var rowNotes, out var rowContext, out var rowSteel);
                rows[i] = row;
                lock (gate)
                {
                    context ??= rowContext;
                    if (steelTag.Length == 0 && rowSteel != null) steelTag = rowSteel;
                    if (verdicts[i].Status != BatchRowStatus.Error)
                    {
                        if (notes == null)
                        {
                            notes = new HashSet<string>(rowNotes, StringComparer.Ordinal);
                            noteOrder.AddRange(rowNotes);
                        }
                        else notes.IntersectWith(rowNotes);
                    }
                }
            });

            var nums = items.Select(fi => fi.Num).ToList();
            int? g = BatchVerdictSummary.GoverningIndex(verdicts);
            var data = new
            {
                schemaVersion = 1,
                sectionTag, steelTag, context,
                notes = noteOrder.Where(n => notes?.Contains(n) == true).ToArray(),
                total = items.Count,
                governing_num = g is { } gi ? nums[gi] : (int?)null,
                verdict_summary = BatchVerdictSummary.ToJson(verdicts, nums),
                rows
            };
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = created,
                Status = BatchVerdictSummary.PackageStatus(verdicts),
                DataJson = JsonSerializer.Serialize(data)
            };
        }
        catch (Exception ex)
        {
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = created,
                Status = "error", DataJson = JsonSerializer.Serialize(new { error = ex.Message })
            };
        }
    }

    /// <summary>Строка пакета из результата одиночной проверки.</summary>
    static object Row(LoadItem fi, CalcResult r, out BatchRowVerdict verdict, out List<string> notes,
        out object? context, out string? steelTag)
    {
        notes = [];
        context = null;
        steelTag = null;
        using var doc = JsonDocument.Parse(r.DataJson);
        var root = doc.RootElement;
        var forces = new Dictionary<string, object?>
        {
            ["force_item_id"] = fi.Id, ["num"] = fi.Num, ["label"] = fi.Label,
            ["N"] = fi.N, ["Mx"] = fi.Mx, ["My"] = fi.My, ["vx"] = fi.Vx, ["vy"] = fi.Vy, ["t"] = fi.T,
        };

        if (r.Status == "error")
        {
            string error = root.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
            verdict = BatchRowVerdict.Error(error);
            forces["status"] = "error";
            forces["error"] = error;
            forces["verdict"] = verdict.ToJson();
            return forces;
        }

        steelTag = root.TryGetProperty("steelTag", out var st) ? st.GetString() : null;
        if (root.TryGetProperty("context", out var c)) context = JsonSerializer.Deserialize<JsonElement>(c.GetRawText());
        if (root.TryGetProperty("notes", out var ns))
            notes.AddRange(ns.EnumerateArray().Select(n => n.GetString() ?? "").Where(n => n.Length > 0));

        var criteria = new List<BatchCriterion>();
        var groups = new Dictionary<string, (double Util, string Clause)>();
        foreach (var d in root.GetProperty("details").EnumerateArray())
        {
            if (d.GetProperty("status").GetString() == "NotApplicable") continue;
            string clause = d.GetProperty("clause").GetString() ?? "";
            string category = d.TryGetProperty("category", out var cat) ? cat.GetString() ?? "" : "";
            double? ratio = d.TryGetProperty("ratio", out var rv) && rv.ValueKind == JsonValueKind.Number ? rv.GetDouble() : null;
            string formula = d.TryGetProperty("formula", out var f) ? f.GetString() ?? "" : "";
            string description = d.TryGetProperty("description", out var de) ? de.GetString() ?? "" : "";
            string text = formula.Length > 0 ? $"{description}, {formula}" : description;
            criteria.Add(new BatchCriterion(clause, ratio, $"СП 16.13330.2017, п. {clause}", text));
            string group = Group(clause, category);
            if (ratio is { } u && (!groups.TryGetValue(group, out var prev) || u > prev.Util)) groups[group] = (u, clause);
        }

        verdict = criteria.Count == 0
            ? BatchRowVerdict.NotApplicable("steel_no_checks", "ни одна проверка не применима")
            : BatchRowVerdict.Worst([.. criteria]);
        forces["status"] = r.Status;
        forces["effective_kind"] = root.TryGetProperty("effectiveKind", out var ek) ? ek.GetString() : null;
        forces["utilization"] = verdict.Util is { } vu && double.IsFinite(vu) ? vu : null;
        foreach (string group in new[] { "strength", "stability", "local", "constructive" })
        {
            forces["util_" + group] = groups.TryGetValue(group, out var gv) ? gv.Util : null;
            forces["clause_" + group] = groups.TryGetValue(group, out var gc) ? gc.Clause : null;
        }
        forces["verdict"] = verdict.ToJson();
        return forces;
    }
}
