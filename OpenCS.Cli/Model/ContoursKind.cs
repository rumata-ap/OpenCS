using System.Text.Json.Nodes;
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Раздел «contours»: пул контуров проекта, ключ — набор и обозначение (<see cref="ContourJson.Key"/>). Пишется раньше
/// сечений: области ссылаются на контуры пула.
/// </summary>
sealed class ContoursKind : IModelKind
{
    public string Name => "contours";
    public string SchemaResource => "OpenCS.Cli.Model.Schemas.contours.schema.json";

    public JsonArray Export(HeadlessProjectContext ctx, Action<string> warn)
    {
        var items = new JsonArray();
        var seen = new HashSet<string>();
        foreach (var c in ctx.Db.Contours.OrderBy(c => c.Id))
        {
            string key = ContourJson.Key(c.GeometrySet, c.Tag);
            if (!seen.Add(key))
            {
                warn(string.Format(Loc.S("CliContourDuplicateSkipped"), key));
                continue;
            }
            if (c.IsPolyline || ContourAuthoring.Validate(c).Count > 0)
            {
                warn(string.Format(Loc.S("CliContourInvalidSkipped"), key));
                continue;
            }
            items.Add(ContourJson.ToJson(c));
        }
        return items;
    }

    /// <summary>Ключи контуров раздела документа (для ссылок областей сечений).</summary>
    public static HashSet<string> KeysIn(JsonObject doc) =>
        doc["contours"] is JsonArray arr
            ? arr.OfType<JsonObject>().Select(o => ContourJson.Key(Str(o, "set"), Str(o, "tag")))
                 .Where(s => s.Length > 0).ToHashSet()
            : [];

    static string Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue(out string? s) ? s : "";

    public IReadOnlyList<ModelChange> Plan(JsonArray items, JsonObject doc, HeadlessProjectContext ctx, ModelErrors errors)
    {
        var changes = new List<ModelChange>();
        var seen = new HashSet<string>();
        for (int i = 0; i < items.Count; i++)
        {
            string path = $"contours[{i}]";
            if (items[i] is not JsonObject item)
            {
                errors.Add(path, Loc.S("CliModelErrNotObject"));
                continue;
            }
            var made = ContourJson.FromJson(item, path, errors);
            if (made == null) continue;
            string key = ContourJson.Key(made.GeometrySet, made.Tag);
            if (!seen.Add(key))
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), key));
                continue;
            }
            var existing = ctx.Db.Contours.Where(c => ContourJson.Key(c.GeometrySet, c.Tag) == key).ToList();
            if (existing.Count > 1)
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrAmbiguousKey"), existing.Count, key));
                continue;
            }
            var target = existing.SingleOrDefault();
            changes.Add(new ModelChange(Name, key, target == null, c => Commit(c, made, target)));
        }
        return changes;
    }

    /// <summary>Новый — добавить; существующий — заменить тип и вершины на месте (тот же Id, ссылки областей целы).</summary>
    static void Commit(HeadlessProjectContext ctx, Contour made, Contour? target)
    {
        if (target != null)
        {
            target.Type = made.Type;
            foreach (var p in target.Points) ctx.Db.Points.Remove(p);
            target.Points.Clear();
            foreach (var p in made.Points)
            {
                p.Contour = target;
                target.Points.Add(p);
                ctx.Db.Points.Add(p);
            }
            made = target;
        }
        var errors = ContourAuthoring.Save(ctx.Db, made);
        if (errors.Count > 0)   // не должно случиться: Plan уже проверил тем же Validate
            throw new CliException(CliExitCode.Failed, string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")));
    }
}

/// <summary>Раздел «circles»: пул кругов проекта, ключ — набор и центр (<see cref="ContourJson.CircleKey"/>).</summary>
sealed class CirclesKind : IModelKind
{
    public string Name => "circles";
    public string SchemaResource => "OpenCS.Cli.Model.Schemas.circles.schema.json";

    public JsonArray Export(HeadlessProjectContext ctx, Action<string> warn)
    {
        var items = new JsonArray();
        var seen = new HashSet<string>();
        foreach (var c in ctx.Db.Circles.OrderBy(c => c.Id))
        {
            string key = ContourJson.CircleKey(c.GeometrySet, c.X, c.Y);
            if (!seen.Add(key))
            {
                warn(string.Format(Loc.S("CliCircleDuplicateSkipped"), key));
                continue;
            }
            items.Add(ContourJson.CircleToJson(c));
        }
        return items;
    }

    public IReadOnlyList<ModelChange> Plan(JsonArray items, JsonObject doc, HeadlessProjectContext ctx, ModelErrors errors)
    {
        var changes = new List<ModelChange>();
        var seen = new HashSet<string>();
        for (int i = 0; i < items.Count; i++)
        {
            string path = $"circles[{i}]";
            if (items[i] is not JsonObject item)
            {
                errors.Add(path, Loc.S("CliModelErrNotObject"));
                continue;
            }
            var made = ContourJson.CircleFromJson(item, path, errors);
            if (made == null) continue;
            string key = ContourJson.CircleKey(made.GeometrySet, made.X, made.Y);
            if (!seen.Add(key))
            {
                errors.Add($"{path}.x", string.Format(Loc.S("CliModelErrDuplicateKey"), key));
                continue;
            }
            var existing = ctx.Db.Circles.Where(c => ContourJson.CircleKey(c.GeometrySet, c.X, c.Y) == key).ToList();
            if (existing.Count > 1)
            {
                errors.Add($"{path}.x", string.Format(Loc.S("CliModelErrAmbiguousKey"), existing.Count, key));
                continue;
            }
            var target = existing.SingleOrDefault();
            changes.Add(new ModelChange(Name, key, target == null, c => Commit(c, made, target)));
        }
        return changes;
    }

    static void Commit(HeadlessProjectContext ctx, CircleP made, CircleP? target)
    {
        if (target != null)
        {
            target.Tag = made.Tag;
            target.X = made.X;
            target.Y = made.Y;
            target.Radius = made.Radius;
            made = target;
        }
        var errors = ContourAuthoring.SaveCircle(ctx.Db, made);
        if (errors.Count > 0)
            throw new CliException(CliExitCode.Failed, string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")));
    }
}
