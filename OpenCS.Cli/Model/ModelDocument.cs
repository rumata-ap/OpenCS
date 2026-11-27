using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>Документ opencs-model: чтение с проверкой заголовка, выгрузка, план изменений по видам.</summary>
static class ModelDocument
{
    public const string Format = "opencs-model";
    public const int Version = 1;

    /// <summary>Виды в порядке записи: настройки проекта — первыми (по ним считаются «авто»-коэффициенты комбинаций схем), диаграммы — раньше материалов custom, контуры пула — раньше сечений, материалы —
    /// раньше сечений пластин, сечения — раньше огневых, сечения, огневые и наборы — раньше задач, сечения и задачи кручения — раньше схем МКЭ,
    /// схемы (загружения, комбинации, узлы) — раньше постановок, постановки (расчётные длины стали) — раньше проверок по КЭ.</summary>
    public static IReadOnlyList<IModelKind> Kinds { get; } =
        [
            new SettingsKind(), new DiagramsKind(), new MaterialsKind(), new ContoursKind(), new CirclesKind(), new SectionsKind(),
            new PlateSectionsKind(), new FireSectionsKind(), new ForceSetsKind(), new TasksKind(), new FemSchemasKind(), new FemFragmentsKind(),
            new FemAnalysesKind(), new FemChecksKind()
        ];

    public static IModelKind? Find(string name) => Kinds.FirstOrDefault(k => k.Name == name);

    public static JsonObject? Read(string path, ModelErrors errors)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path),
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException e)
        {
            errors.Add("$", string.Format(Loc.S("CliModelErrBadJson"), e.Message));
            return null;
        }
        catch (IOException e)
        {
            throw new CliException(CliExitCode.Usage, string.Format(Loc.S("CliErrFileNotFound"), path) + " " + e.Message);
        }
        if (root is not JsonObject doc)
        {
            errors.Add("$", Loc.S("CliModelErrNotObject"));
            return null;
        }
        if (doc["format"] is not JsonValue f || !f.TryGetValue(out string? format) || format != Format)
            errors.Add("format", string.Format(Loc.S("CliModelErrFormat"), Format));
        if (doc["version"] is not JsonValue v || !v.TryGetValue(out int version) || version != Version)
            errors.Add("version", string.Format(Loc.S("CliModelErrVersion"), Version));
        foreach (var (key, value) in doc)
        {
            if (key is "format" or "version") continue;
            var kind = Find(key);
            if (kind == null) errors.Add(key, Loc.S("CliModelErrUnknownSection"));
            else if (kind.IsObject ? value is not JsonObject : value is not JsonArray)
                errors.Add(key, Loc.S(kind.IsObject ? "CliModelErrNotObject" : "CliModelErrNotArray"));
        }
        return doc;
    }

    public static JsonObject Write(HeadlessProjectContext ctx, IReadOnlySet<string>? only, Action<string> warn)
    {
        var doc = new JsonObject { ["format"] = Format, ["version"] = Version };
        foreach (var kind in Kinds.Where(k => only == null || only.Contains(k.Name)))
        {
            var items = kind.Export(ctx, warn);
            doc[kind.Name] = kind.IsObject ? items[0]?.DeepClone() : items;
        }
        return doc;
    }

    public static IReadOnlyList<ModelChange> Plan(JsonObject doc, HeadlessProjectContext ctx, ModelErrors errors)
    {
        var changes = new List<ModelChange>();
        foreach (var kind in Kinds)
            if (doc[kind.Name] is JsonArray items)
                changes.AddRange(kind.Plan(items, doc, ctx, errors));
            else if (kind.IsObject && doc[kind.Name] is JsonObject single)
                changes.AddRange(kind.Plan([single.DeepClone()], doc, ctx, errors));
        return changes;
    }

    public static string ReadSchema(IModelKind kind)
    {
        if (kind.GeneratedSchema() is { } generated) return generated;
        using var stream = typeof(ModelDocument).Assembly.GetManifestResourceStream(kind.SchemaResource)
            ?? throw new InvalidOperationException(kind.SchemaResource);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
