using System.Text.Json.Nodes;
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>Раздел «materials»: материалы проекта, ключ — обозначение (tag).</summary>
sealed class MaterialsKind : IModelKind
{
    public string Name => "materials";
    public string SchemaResource => "OpenCS.Cli.Model.Schemas.materials.schema.json";

    public JsonArray Export(HeadlessProjectContext ctx, Action<string> warn)
    {
        var items = new JsonArray();
        foreach (var m in ctx.Materials.OrderBy(m => m.Id))
        {
            if (m.Type is MatType.Custom or MatType.None)
            {
                warn(string.Format(Loc.S("CliMaterialSkipped"), m.Tag));
                continue;
            }
            items.Add(MaterialJson.ToJson(m));
        }
        return items;
    }

    public IReadOnlyList<ModelChange> Plan(JsonArray items, HeadlessProjectContext ctx, ModelErrors errors)
    {
        var changes = new List<ModelChange>();
        var seen = new HashSet<string>();
        for (int i = 0; i < items.Count; i++)
        {
            string path = $"materials[{i}]";
            if (items[i] is not JsonObject item)
            {
                errors.Add(path, Loc.S("CliModelErrNotObject"));
                continue;
            }
            var made = MaterialJson.FromJson(item, path, errors);
            if (made == null) continue;
            string key = made.Tag;
            if (!seen.Add(key))
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), key));
                continue;
            }
            var existing = ctx.Materials.Where(m => m.Tag.Trim() == key).ToList();
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

    /// <summary>Новый — добавить; существующий — заменить содержимое на месте (тот же Id, ссылки сечений целы).</summary>
    static void Commit(HeadlessProjectContext ctx, Material made, Material? target)
    {
        if (target != null)
        {
            target.Type = made.Type;
            target.Description = made.Description;
            target.E = made.E;
            target.AggregateType = made.AggregateType;
            target.FireRebarClass = made.FireRebarClass;
            target.BaseType = MatType.None;
            target.CustomDiagramIds = [];
            target.MaterialChars = made.MaterialChars.Select(ch => ch.Clone()).ToList();
            made = target;
        }
        var errors = MaterialAuthoring.Save(ctx.Db, made);
        if (errors.Count > 0)   // не должно случиться: Plan уже проверил тем же Validate
            throw new CliException(CliExitCode.Failed, string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")));
    }
}
