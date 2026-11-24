using System.CommandLine;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CScore;
using CScore.Import;
using CScore.Sp16;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Cli;

/// <summary>Расчётные задачи: по строкам набора усилий или одна пакетная (*_batch); параметры — JSON и Имя=Значение.</summary>
sealed partial class CliApp
{
    readonly Option<string> _taskKind = new("--kind") { Required = true };
    readonly Option<string> _section = new("--section") { Required = true };
    readonly Option<string?> _taskSet = new("--set");
    readonly Option<string[]> _param = new("--param") { Arity = ArgumentArity.ZeroOrMore };
    readonly Option<string?> _params = new("--params");
    readonly Option<string?> _calcType = new("--calc-type");

    void DescribeTaskOptions()
    {
        _taskKind.Description = Loc.S("CliOptTaskKind");
        _section.Description = Loc.S("CliOptSection");
        _taskSet.Description = Loc.S("CliOptSet");
        _param.Description = Loc.S("CliOptParam");
        _params.Description = Loc.S("CliOptParams");
        _calcType.Description = Loc.S("CliOptCalcType");
        _calcType.AcceptOnlyFromAmong("C", "CL", "N", "NL");
    }

    Command AddTasksCommand() => EditCommand("tasks", "CliCmdAddTasks", AddTasksAsync,
        _taskKind, _section, _taskSet, _elements, _param, _params, _calcType, _tag);

    Task<int> AddTasksAsync(CliCall call)
    {
        var p = call.Parse;
        string kind = p.GetValue(_taskKind)!.Trim();
        var spec = CalcTaskKinds.Get(kind)
            ?? throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrUnknownKind"), kind,
                string.Join(", ", CalcTaskKinds.Names)));
        if (kind.StartsWith("shell_", StringComparison.Ordinal))
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrShellKind"), kind));
        bool batch = kind.EndsWith("_batch", StringComparison.Ordinal);
        string? setRef = p.GetValue(_taskSet);
        if (batch && setRef == null) throw new CliException(CliExitCode.Usage, Loc.S("CliErrBatchNeedsSet"));
        var elements = p.GetValue(_elements) is { } list ? ElementList.Parse(list) : null;
        if (batch && elements != null) throw new CliException(CliExitCode.Usage, Loc.S("CliErrBatchElements"));
        string paramsJson = BuildTaskParams(kind, p.GetValue(_params), p.GetValue(_param) ?? []);
        // Не задан — первый допустимый для вида (трещины и прогибы — N, а не C).
        var calcType = p.GetValue(_calcType) is { } ct ? Enum.Parse<CalcType>(ct) : spec.CalcTypes[0];

        return Edit(call, session =>
        {
            var db = session.Context.Db;
            var section = CliRefs.Resolve(db.CrossSections, p.GetValue(_section)!, s => s.Id, s => s.Tag, "CliKindSection");
            var set = setRef == null ? null : CliRefs.Resolve(db.ForceSets, setRef, f => f.Id, f => f.Tag, "CliKindForceSet");
            if (set != null && set.Kind != "bar")
                throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrSetNotBar"), set.Tag));
            string tag = p.GetValue(_tag) ?? kind;
            int num = CalcTaskAuthoring.NextNum(db);
            CalcTask NewTask(string taskTag, int itemId) => new()
            {
                Num = num++, Tag = taskTag, Kind = kind, CalcType = calcType, SectionId = section.Id,
                ForceSetId = set?.Id ?? 0, ForceItemId = itemId, ParamsJson = paramsJson
            };

            var tasks = new List<CalcTask>();
            if (set == null || batch) tasks.Add(NewTask(tag, 0));
            else
            {
                var rows = set.Items.Where(i => elements == null || i.SourceElementNum is int e && elements.Contains(e))
                    .OrderBy(i => i.Num).ThenBy(i => i.Id).ToList();
                if (rows.Count == 0) throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrNoRows"), set.Tag));
                tasks.AddRange(rows.Select(i => NewTask($"{tag} — {i.Label}", i.Id)));
            }
            // Проверка службой — та же, что у окна задачи и apply; ошибка любой задачи — ничего не записано.
            // Строка набора, которую требует вид, но не задана (--set не указан), — предупреждение: команда исторически
            // создаёт такие задачи-заготовки, расчёт возьмёт нулевые усилия.
            var problems = new List<string>();
            var warnings = new HashSet<string>();
            foreach (var t in tasks)
                foreach (var e in CalcTaskAuthoring.Validate(db, t))
                {
                    if (e.Path == "row" && t.ForceItemId == 0 && set == null) warnings.Add(e.Message);
                    else problems.Add($"{t.Tag}: {(e.Path.StartsWith("params", StringComparison.Ordinal) ? e.Path + ": " : "")}{e.Message}");
                }
            if (problems.Count > 0)
                throw new CliException(CliExitCode.Usage, string.Join(Environment.NewLine, problems.Distinct()));
            foreach (var w in warnings) call.Log.Warning(string.Format(Loc.S("CliTaskNoRowWarning"), w));
            foreach (var t in tasks) db.SaveCalcTask(t);

            if (call.Json) CliOutput.WriteJson(call.Out, tasks.Select(t => new { t.Id, t.Num, t.Tag, t.Kind }).ToList());
            else
            {
                CliOutput.WriteTable(call.Out, [Loc.S("CliColId"), Loc.S("CliColNum"), Loc.S("CliColTag")],
                    tasks.Select(t => new[] { t.Id.ToString(), t.Num.ToString(), t.Tag }));
                call.Out.WriteLine(string.Format(Loc.S("CliTasksAdded"), tasks.Count));
            }
        });
    }

    /// <summary>
    /// Параметры задачи: --params (JSON-объект или @файл) и поверх --param Имя=Значение. Для стали имена сверяются со
    /// свойствами <see cref="SteelDesignParams"/> без учёта регистра, итог — полный канонический JSON.
    /// </summary>
    internal static string BuildTaskParams(string kind, string? paramsText, IReadOnlyList<string> pairs)
    {
        var obj = new JsonObject();
        if (!string.IsNullOrWhiteSpace(paramsText))
        {
            string text = paramsText.Trim();
            if (text.StartsWith('@'))
            {
                string file = text[1..];
                if (!File.Exists(file)) throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrFileNotFound"), file));
                text = File.ReadAllText(file);
            }
            try
            {
                obj = JsonNode.Parse(text) as JsonObject
                      ?? throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrParamsJson"), paramsText));
            }
            catch (JsonException ex)
            {
                throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrParamsJson"), ex.Message));
            }
        }
        foreach (string pair in pairs)
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrParam"), pair));
            obj[pair[..eq].Trim()] = ParamValue(pair[(eq + 1)..].Trim());
        }

        if (!kind.StartsWith("steel_", StringComparison.Ordinal)) return obj.ToJsonString();

        var names = SteelParamNames();
        var canonical = new JsonObject();
        foreach (var (key, value) in obj)
        {
            string name = names.FirstOrDefault(n => string.Equals(n, key, StringComparison.OrdinalIgnoreCase))
                ?? throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrUnknownParam"), kind, key,
                    string.Join(", ", names)));
            canonical[name] = value?.DeepClone();
        }
        try
        {
            return SteelDesignParams.Parse(canonical.ToJsonString()).ToJson();
        }
        catch (JsonException ex)
        {
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrParamsJson"), ex.Message));
        }
    }

    static JsonNode? ParamValue(string text)
    {
        if (text.Equals("true", StringComparison.OrdinalIgnoreCase)) return JsonValue.Create(true);
        if (text.Equals("false", StringComparison.OrdinalIgnoreCase)) return JsonValue.Create(false);
        if (text.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
        if (ForceTableParser.TryNumber(text, out double number)) return JsonValue.Create(number);
        return JsonValue.Create(text);
    }

    /// <summary>Имена параметров стальных проверок: записываемые свойства без устаревших и игнорируемых.</summary>
    static IReadOnlyList<string> SteelParamNames() =>
        typeof(SteelDesignParams).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(pr => pr.CanWrite && !pr.Name.StartsWith("Legacy", StringComparison.Ordinal)
                         && pr.GetCustomAttribute<JsonIgnoreAttribute>() is not { Condition: JsonIgnoreCondition.Always })
            .Select(pr => pr.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? pr.Name)
            .Order(StringComparer.Ordinal).ToList();
}

/// <summary>Наибольший Кисп результата расчёта из его JSON: поле «utilization» либо определяющая строка пакета.</summary>
static class CliUtilization
{
    public static double? Read(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Number(root, "utilization") is { } u) return u;
            if (!root.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array) return null;
            int? governing = Int(root, "governing_num")
                ?? (root.TryGetProperty("verdict_summary", out var summary) && summary.ValueKind == JsonValueKind.Object
                    ? Int(summary, "governing_num") : null);
            double? max = null;
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object || Number(row, "utilization") is not { } value) continue;
                if (governing != null && Int(row, "num") == governing) return value;
                max = max == null ? value : Math.Max(max.Value, value);
            }
            return max;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Format(double? value) =>
        value is { } v ? v.ToString("0.000", CultureInfo.CurrentUICulture) : Loc.S("CliNone");

    static double? Number(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : null;
}
