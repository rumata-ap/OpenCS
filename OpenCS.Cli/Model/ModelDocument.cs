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

    public static IReadOnlyList<IModelKind> Kinds { get; } = [new MaterialsKind()];

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
            if (Find(key) == null) errors.Add(key, Loc.S("CliModelErrUnknownSection"));
            else if (value is not JsonArray) errors.Add(key, Loc.S("CliModelErrNotArray"));
        }
        return doc;
    }

    public static JsonObject Write(HeadlessProjectContext ctx, IReadOnlySet<string>? only, Action<string> warn)
    {
        var doc = new JsonObject { ["format"] = Format, ["version"] = Version };
        foreach (var kind in Kinds.Where(k => only == null || only.Contains(k.Name)))
            doc[kind.Name] = kind.Export(ctx, warn);
        return doc;
    }

    public static IReadOnlyList<ModelChange> Plan(JsonObject doc, HeadlessProjectContext ctx, ModelErrors errors)
    {
        var changes = new List<ModelChange>();
        foreach (var kind in Kinds)
            if (doc[kind.Name] is JsonArray items)
                changes.AddRange(kind.Plan(items, ctx, errors));
        return changes;
    }

    public static string ReadSchema(IModelKind kind)
    {
        using var stream = typeof(ModelDocument).Assembly.GetManifestResourceStream(kind.SchemaResource)
            ?? throw new InvalidOperationException(kind.SchemaResource);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
