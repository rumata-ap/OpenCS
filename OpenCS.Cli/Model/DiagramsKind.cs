using System.Text.Json.Nodes;
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>Раздел «diagrams»: пул диаграмм проекта, ключ — обозначение (tag). Пишется раньше материалов custom.</summary>
sealed class DiagramsKind : IModelKind
{
    public string Name => "diagrams";
    public string SchemaResource => "OpenCS.Cli.Model.Schemas.diagrams.schema.json";

    public JsonArray Export(HeadlessProjectContext ctx, Action<string> warn)
    {
        var items = new JsonArray();
        foreach (var d in ctx.Diagrams.OrderBy(d => d.Id))
            items.Add(DiagramJson.ToJson(d));
        return items;
    }

    /// <summary>Обозначения диаграмм раздела документа (для ссылок материалов custom).</summary>
    public static HashSet<string> TagsIn(JsonObject doc) =>
        doc["diagrams"] is JsonArray arr
            ? arr.OfType<JsonObject>().Select(o => o["tag"] is JsonValue v && v.TryGetValue(out string? s) ? s.Trim() : "")
                 .Where(s => s.Length > 0).ToHashSet()
            : [];

    public IReadOnlyList<ModelChange> Plan(JsonArray items, JsonObject doc, HeadlessProjectContext ctx, ModelErrors errors)
    {
        var changes = new List<ModelChange>();
        var seen = new HashSet<string>();
        for (int i = 0; i < items.Count; i++)
        {
            string path = $"diagrams[{i}]";
            if (items[i] is not JsonObject item)
            {
                errors.Add(path, Loc.S("CliModelErrNotObject"));
                continue;
            }
            var made = DiagramJson.FromJson(item, path, errors);
            if (made == null) continue;
            string key = made.Tag;
            if (!seen.Add(key))
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), key));
                continue;
            }
            var existing = ctx.Diagrams.Where(d => d.Tag.Trim() == key).ToList();
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

    /// <summary>Новая — добавить; существующая — заменить содержимое на месте (тот же Id, ссылки материалов целы).</summary>
    static void Commit(HeadlessProjectContext ctx, Diagramm made, Diagramm? target)
    {
        if (target != null)
        {
            target.Type = made.Type;
            target.MaterialType = made.MaterialType;
            target.CalcType = made.CalcType;
            target.Ic = made.Ic;
            target.It = made.It;
            target.CharacteristicStrains = made.CharacteristicStrains;
            made = target;
        }
        var errors = DiagramAuthoring.Save(ctx.Db, made);
        if (errors.Count > 0)   // не должно случиться: Plan уже проверил тем же Validate
            throw new CliException(CliExitCode.Failed, string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")));
    }
}
