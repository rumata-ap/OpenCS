using System.Text.Json.Nodes;

using CScore.Fem.Editing;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Раздел «settings»: настройки проекта, от которых зависят результаты расчёта и импорта (поля
/// <see cref="ProjectSettingsAuthoring.Fields"/> с признаком «в документе»). В документе — <b>объект</b>, не массив:
/// настройки у проекта одни. <c>apply</c> меняет только указанные поля, выгрузка пишет все. Пишется первым: «авто»-
/// коэффициенты комбинаций и вид величин нагрузок схем считаются по уже записанным настройкам. Смена вида величин или
/// γf пересчитывает «авто»-слагаемые комбинаций всех схем (в GUI это делает плашка «Пересчитать»).
/// </summary>
sealed class SettingsKind : IModelKind
{
    public const string KindName = "settings";
    public string Name => KindName;
    public string SchemaResource => "";
    public bool IsObject => true;

    static IEnumerable<SettingField> DocFields => ProjectSettingsAuthoring.Fields.Where(f => f.InDocument);

    static ProjectSettings Live(HeadlessProjectContext ctx) => new()
    {
        Calc = ctx.CalcSettings, Gmsh = ctx.GmshSettings,
        Lira = ctx.Db.LoadLiraImportSettings(), Acad = ctx.Db.LoadAcadImportSettings(),
    };

    public JsonArray Export(HeadlessProjectContext ctx, Action<string> warn)
    {
        var live = Live(ctx);
        var root = new JsonObject();
        foreach (var f in DocFields)
        {
            var parts = f.Path.Split('.');
            var node = root;
            foreach (var part in parts[..^1])
            {
                if (node[part] is not JsonObject child) node[part] = child = new JsonObject();
                node = child;
            }
            node[parts[^1]] = ToJson(f.Get(live));
        }
        return [root];
    }

    static JsonNode? ToJson(object? value) => value switch
    {
        null => null,
        double d => JsonValue.Create(d),
        int i => JsonValue.Create(i),
        bool b => JsonValue.Create(b),
        string s => JsonValue.Create(s),
        IReadOnlyList<int> list => new JsonArray(list.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
        _ => JsonValue.Create(value.ToString()),
    };

    /// <summary>Значение узла документа в виде, который ждёт поле; null + ошибка — тип не тот.</summary>
    static bool FromJson(SettingField f, JsonNode? node, out object? value)
    {
        value = null;
        switch (f.Kind)
        {
            case SettingKind.Bool:
                if (node is JsonValue bv && bv.TryGetValue(out bool b)) { value = b; return true; }
                return false;
            case SettingKind.Choice:
                if (node is JsonValue sv && sv.TryGetValue(out string? s)) { value = s; return true; }
                return false;
            case SettingKind.Integer:
                if (node is JsonValue iv && iv.TryGetValue(out double di) && di == Math.Floor(di) && Math.Abs(di) < int.MaxValue)
                { value = (int)di; return true; }
                return false;
            case SettingKind.Number:
                if (node is JsonValue nv && nv.TryGetValue(out double d)) { value = d; return true; }
                return false;
            case SettingKind.IntList:
                if (node == null) return true;
                if (node is not JsonArray arr) return false;
                var list = new List<int>();
                foreach (var item in arr)
                {
                    if (item is not JsonValue v || !v.TryGetValue(out double x) || x != Math.Floor(x)) return false;
                    list.Add((int)x);
                }
                value = list;
                return true;
        }
        return false;
    }

    static string TypeError(SettingField f) => Loc.S(f.Kind switch
    {
        SettingKind.Bool => "SettingErrBool",
        SettingKind.Choice => "CliModelErrNotString",
        SettingKind.Integer => "CliModelErrNotInteger",
        SettingKind.Number => "CliModelErrNotNumber",
        _ => "SettingErrIntList",
    });

    public IReadOnlyList<ModelChange> Plan(JsonArray items, JsonObject doc, HeadlessProjectContext ctx, ModelErrors errors)
    {
        if (items.Count != 1 || items[0] is not JsonObject root)
        {
            errors.Add(KindName, Loc.S("CliModelErrNotObject"));
            return [];
        }
        var values = new List<(SettingField Field, object? Value)>();
        Walk(root, "", values, errors);
        if (values.Count == 0) return [];
        return
        [
            new ModelChange(KindName, Loc.S("CliSettingsKey"), false, c => Commit(c, values)),
        ];
    }

    static void Walk(JsonObject o, string prefix, List<(SettingField, object?)> values, ModelErrors errors)
    {
        foreach (var (key, node) in o)
        {
            string path = prefix.Length == 0 ? key : $"{prefix}.{key}";
            string docPath = $"{KindName}.{path}";
            var field = ProjectSettingsAuthoring.Find(path);
            if (field is { InDocument: true })
            {
                if (!FromJson(field, node, out var value)) { errors.Add(docPath, TypeError(field)); continue; }
                if (field.Check(value) is { } error) { errors.Add(docPath, error); continue; }
                values.Add((field, value));
            }
            else if (node is JsonObject child && DocFields.Any(f => f.Path.StartsWith(path + ".", StringComparison.Ordinal)))
                Walk(child, path, values, errors);
            else
                errors.Add(docPath, Loc.S("CliModelErrUnknownField"));
        }
    }

    static void Commit(HeadlessProjectContext ctx, List<(SettingField Field, object? Value)> values)
    {
        var live = Live(ctx);
        var before = live.Calc.Clone();
        foreach (var (f, v) in values) f.Set(live, v);
        var errors = ProjectSettingsAuthoring.Save(ctx.Db, live);
        if (errors.Count > 0)
            throw new CliException(CliExitCode.Usage, string.Join(Environment.NewLine, errors.Select(e => $"{KindName}.{e.Path}: {e.Error}")));
        if (ProjectSettingsAuthoring.LoadsDiffer(before, live.Calc)) RecomputeAutoCombinations(ctx);
    }

    /// <summary>Пересчитывает «авто»-слагаемые комбинаций всех схем по новым γf и виду величин; ручные — не трогает.</summary>
    internal static void RecomputeAutoCombinations(HeadlessProjectContext ctx)
    {
        var defaults = ctx.CalcSettings.ToSp20GammaDefaults();
        var kind = ctx.CalcSettings.ToSp20LoadValueKind();
        foreach (var schema in ctx.Db.FemSchemas.ToList())
        {
            var session = FemSchemaAuthoring.OpenSession(ctx.Db, schema);
            var stale = RecomputeAutoTermsCommand.Stale(session, defaults, kind);
            if (stale.Count == 0) continue;
            new RecomputeAutoTermsCommand(defaults, kind).Do(session);
            foreach (var definition in stale) ctx.Db.SaveFemLoadDefinition(definition);
            ctx.LogService.Info(string.Format(Loc.S("CliSettingsCombinationsRecomputed"), schema.Tag, stale.Count));
        }
    }

    /// <summary>JSON-схема раздела — из той же таблицы полей, что проверяет окно и apply.</summary>
    public string GeneratedSchema()
    {
        var root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = "opencs-model: settings",
            ["description"] = Loc.S("CliSettingsSchemaDescription"),
            ["type"] = "object",
        };
        foreach (var f in DocFields)
        {
            var parts = f.Path.Split('.');
            var node = root;
            foreach (var part in parts[..^1])
            {
                node["type"] = "object";
                node["additionalProperties"] = false;
                if (node["properties"] is not JsonObject props) node["properties"] = props = new JsonObject();
                if (props[part] is not JsonObject child) props[part] = child = new JsonObject();
                node = child;
            }
            node["type"] = "object";
            node["additionalProperties"] = false;
            if (node["properties"] is not JsonObject leafProps) node["properties"] = leafProps = new JsonObject();
            leafProps[parts[^1]] = LeafSchema(f);
        }
        return root.ToJsonString(CliOutput.Json);
    }

    static JsonObject LeafSchema(SettingField f)
    {
        var o = new JsonObject();
        switch (f.Kind)
        {
            case SettingKind.Bool: o["type"] = "boolean"; break;
            case SettingKind.Choice: o["enum"] = new JsonArray(f.Choices!.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()); break;
            case SettingKind.IntList:
                o["type"] = new JsonArray("array", "null");
                o["items"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 };
                break;
            default:
                o["type"] = f.Kind == SettingKind.Integer ? "integer" : "number";
                if (f.Allowed != null) o["enum"] = new JsonArray(f.Allowed.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray());
                if (f.Range is { } r)
                {
                    if (r.Min is { } lo) o[r.MinInclusive ? "minimum" : "exclusiveMinimum"] = lo;
                    if (r.Max is { } hi) o[r.MaxInclusive ? "maximum" : "exclusiveMaximum"] = hi;
                }
                break;
        }
        return o;
    }
}
