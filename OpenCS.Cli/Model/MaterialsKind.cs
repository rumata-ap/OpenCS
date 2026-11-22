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
            if (m.Type == MatType.None)
            {
                warn(string.Format(Loc.S("CliMaterialSkipped"), m.Tag));
                continue;
            }
            if (m.Type == MatType.Custom && MaterialJson.CustomDiagramTags(m, ctx.Diagrams) == null)
            {
                warn(string.Format(Loc.S("CliMaterialCustomSkipped"), m.Tag));
                continue;
            }
            items.Add(MaterialJson.ToJson(m, ctx.Diagrams));
        }
        return items;
    }

    public IReadOnlyList<ModelChange> Plan(JsonArray items, JsonObject doc, HeadlessProjectContext ctx, ModelErrors errors)
    {
        var changes = new List<ModelChange>();
        var docDiagrams = DiagramsKind.TagsIn(doc);
        var seen = new HashSet<string>();
        for (int i = 0; i < items.Count; i++)
        {
            string path = $"materials[{i}]";
            if (items[i] is not JsonObject item)
            {
                errors.Add(path, Loc.S("CliModelErrNotObject"));
                continue;
            }
            var made = MaterialJson.FromJson(item, path, errors, out var diagramTags);
            // Ссылки custom проверяются и при других ошибках материала — все ошибки документа за один прогон.
            bool linksOk = diagramTags == null || CheckDiagramTags(diagramTags, docDiagrams, ctx, path, errors);
            if (made == null || !linksOk) continue;
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
            changes.Add(new ModelChange(Name, key, target == null, c => Commit(c, made, diagramTags, target)));
        }
        return changes;
    }

    /// <summary>Ссылки custom на диаграммы: обозначение есть в разделе diagrams документа или однозначно в пуле проекта.</summary>
    static bool CheckDiagramTags(Dictionary<CalcType, string> tags, HashSet<string> docDiagrams, HeadlessProjectContext ctx,
        string path, ModelErrors errors)
    {
        int before = errors.Count;
        foreach (var (ct, tag) in tags)
        {
            if (docDiagrams.Contains(tag)) continue;
            int n = ctx.Diagrams.Count(d => d.Tag.Trim() == tag);
            if (n == 0) errors.Add($"{path}.custom.diagrams.{ct}", string.Format(Loc.S("CliMaterialErrDiagramNotFound"), tag));
            else if (n > 1) errors.Add($"{path}.custom.diagrams.{ct}", string.Format(Loc.S("CliModelErrAmbiguousKey"), n, tag));
        }
        return errors.Count == before;
    }

    /// <summary>Новый — добавить; существующий — заменить содержимое на месте (тот же Id, ссылки сечений целы).</summary>
    static void Commit(HeadlessProjectContext ctx, Material made, Dictionary<CalcType, string>? diagramTags, Material? target)
    {
        // Диаграммы документа уже записаны (раздел diagrams идёт раньше): обозначения → Id пула.
        if (diagramTags != null)
            made.CustomDiagramIds = diagramTags.ToDictionary(kv => kv.Key, kv => ctx.Diagrams.Single(d => d.Tag.Trim() == kv.Value).Id);
        if (target != null)
        {
            target.Type = made.Type;
            target.Description = made.Description;
            target.E = made.E;
            target.AggregateType = made.AggregateType;
            target.FireRebarClass = made.FireRebarClass;
            target.BaseType = made.BaseType;
            target.CustomDiagramIds = new Dictionary<CalcType, int>(made.CustomDiagramIds);
            target.MaterialChars = made.MaterialChars.Select(ch => ch.Clone()).ToList();
            made = target;
        }
        var errors = MaterialAuthoring.Save(ctx.Db, made);
        if (errors.Count > 0)   // не должно случиться: Plan уже проверил тем же Validate
            throw new CliException(CliExitCode.Failed, string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")));
    }
}
