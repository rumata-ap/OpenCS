using System.Text.Json.Nodes;
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Раздел «forceSets»: наборы усилий стержней и пластин, ключ — полное имя (tag, как в дереве; атрибуты СП 20 — в нём
/// же). Строки — таблицей: <c>columns</c> (метка, усилия вида, <c>elem</c>/<c>sect</c> — номера КЭ и сечения внешней
/// схемы) и <c>rows</c> (массивы). Единицы — как в GUI: кН, кН·м (стержни); кН/м, кН·м/м, σ — кПа (пластины);
/// знаки — конвенция OpenCS. Наборы, привязанные к схеме МКЭ (импорт на схему, расчёт), не выгружаются и через
/// <c>apply</c> не правятся (решение пользователя 23.11): их пересоздают импорт и расчёт. При обновлении Id набора
/// сохраняется, строки заменяются по порядку с Id прежних строк по позиции — ссылки задач на строки целы; убрать
/// строку, на которую ссылается задача, нельзя.
/// </summary>
sealed class ForceSetsKind : IModelKind
{
    public string Name => "forceSets";
    public string SchemaResource => "OpenCS.Cli.Model.Schemas.forceSets.schema.json";

    const string Label = "label", Elem = "elem", Sect = "sect";

    static string[] ValueColumns(string kind) => kind == "shell" ? ForceSetAuthoring.ShellColumns : ForceSetAuthoring.BarColumns;

    /// <summary>Набор привязан к схеме МКЭ (импорт на схему, результат расчёта).</summary>
    static bool Linked(ForceSet fs) => fs.SourceSchemaId != null || fs.SourceMemberId != null || fs.SourceElementId != null;

    static JsonNode? Number(double? v) => v is double d ? JsonValue.Create(d + 0.0) : null;

    public JsonArray Export(HeadlessProjectContext ctx, Action<string> warn)
    {
        var items = new JsonArray();
        var seen = new HashSet<string>();
        int linked = 0;
        foreach (var fs in ctx.Db.ForceSets.OrderBy(f => f.Num))
        {
            if (Linked(fs)) { linked++; continue; }
            string key = fs.Tag.Trim();
            if (!seen.Add(key))
            {
                warn(string.Format(Loc.S("CliForceSetDuplicateSkipped"), key));
                continue;
            }
            var errors = ForceSetAuthoring.Validate(fs);
            if (errors.Count > 0)
            {
                warn(string.Format(Loc.S("CliForceSetInvalidSkipped"), key, $"{errors[0].Path}: {errors[0].Message}"));
                continue;
            }
            items.Add(ToJson(fs));
        }
        if (linked > 0) warn(string.Format(Loc.S("CliForceSetLinkedSkipped"), linked));
        return items;
    }

    static JsonObject ToJson(ForceSet fs)
    {
        var o = new JsonObject { ["tag"] = fs.Tag.Trim(), ["kind"] = fs.Kind };
        if (!string.IsNullOrWhiteSpace(fs.Description)) o["description"] = fs.Description;
        if (!string.IsNullOrEmpty(fs.SourceType)) o["source"] = fs.SourceType;
        if (fs.SecondOrder) o["secondOrder"] = true;
        if (!string.IsNullOrWhiteSpace(fs.Imperfections)) o["imperfections"] = fs.Imperfections;

        bool shell = fs.Kind == "shell";
        var values = ValueColumns(fs.Kind).ToList();
        if (shell && !fs.ShellItems.Any(i => i.SigmaX != null || i.SigmaY != null || i.TauXY != null))
            values.RemoveAll(ForceSetAuthoring.StressColumns.Contains);
        bool elements = shell ? fs.ShellItems.Any(i => i.SourceElementNum != null || i.SourceSectionNum != null)
                              : fs.Items.Any(i => i.SourceElementNum != null || i.SourceSectionNum != null);
        var columns = new JsonArray { Label };
        foreach (var c in values) columns.Add(c);
        if (elements) { columns.Add(Elem); columns.Add(Sect); }
        o["columns"] = columns;

        var rows = new JsonArray();
        if (shell)
            foreach (var i in fs.ShellItems)
            {
                var r = new JsonArray { i.Label };
                foreach (var c in values) r.Add(Number(ForceSetAuthoring.ShellValue(i, c)));
                if (elements) { r.Add(i.SourceElementNum); r.Add(i.SourceSectionNum); }
                rows.Add(r);
            }
        else
            foreach (var i in fs.Items)
            {
                var r = new JsonArray { i.Label };
                foreach (var c in values) r.Add(Number(ForceSetAuthoring.BarValue(i, c)));
                if (elements) { r.Add(i.SourceElementNum); r.Add(i.SourceSectionNum); }
                rows.Add(r);
            }
        o["rows"] = rows;
        return o;
    }

    /// <summary>Набор документа: поля и строки в единицах базы.</summary>
    sealed record SetDoc(string Path, ForceSet Set);

    static SetDoc? Parse(JsonObject o, string path, ModelErrors errors)
    {
        int before = errors.Count;
        string? tag = SectionJson.Str(o, "tag", path, errors, required: true);
        if (tag != null && tag.Trim().Length == 0) errors.Add($"{path}.tag", Loc.S("ForceSetErrTagEmpty"));
        string? kind = SectionJson.Str(o, "kind", path, errors, required: true);
        if (kind != null && !ForceSetAuthoring.Kinds.Contains(kind))
            errors.Add($"{path}.kind", Loc.S("ForceSetErrKind"));
        var fs = new ForceSet
        {
            Tag = (tag ?? "").Trim(),
            Kind = kind ?? "bar",
            Description = SectionJson.Str(o, "description", path, errors) is { Length: > 0 } d ? d : null,
            SourceType = SectionJson.Str(o, "source", path, errors) is { Length: > 0 } s ? s : null,
            SecondOrder = SectionJson.Bool(o, "secondOrder", path, errors) ?? false,
            Imperfections = SectionJson.Str(o, "imperfections", path, errors) is { Length: > 0 } im ? im : null,
        };
        if (errors.Count > before) return null;

        // Столбцы
        var allowed = new[] { Label, Elem, Sect }.Concat(ValueColumns(fs.Kind)).ToArray();
        var columns = new List<string>();
        if (o["columns"] is not JsonArray cols)
        {
            errors.Add($"{path}.columns", o["columns"] is null ? Loc.S("CliModelErrRequired") : Loc.S("CliModelErrNotArray"));
            return null;
        }
        for (int k = 0; k < cols.Count; k++)
        {
            string cp = $"{path}.columns[{k}]";
            if (cols[k] is not JsonValue v || !v.TryGetValue(out string? name))
                errors.Add(cp, Loc.S("CliModelErrNotString"));
            else if (!allowed.Contains(name))
                errors.Add(cp, string.Format(Loc.S("CliForceSetErrColumn"), name, string.Join(", ", allowed)));
            else if (columns.Contains(name))
                errors.Add(cp, string.Format(Loc.S("CliModelErrDuplicateKey"), name));
            else columns.Add(name);
        }
        if (errors.Count > before) return null;
        if (!columns.Contains(Label)) errors.Add($"{path}.columns", Loc.S("CliForceSetErrNoLabel"));

        // Строки
        if (o["rows"] is not JsonArray rows)
        {
            errors.Add($"{path}.rows", o["rows"] is null ? Loc.S("CliModelErrRequired") : Loc.S("CliModelErrNotArray"));
            return null;
        }
        bool shell = fs.Kind == "shell";
        for (int r = 0; r < rows.Count; r++)
        {
            string rp = $"{path}.rows[{r}]";
            if (rows[r] is not JsonArray cells)
            {
                errors.Add(rp, Loc.S("CliModelErrNotArray"));
                continue;
            }
            if (cells.Count != columns.Count)
            {
                errors.Add(rp, string.Format(Loc.S("CliForceSetErrCells"), cells.Count, columns.Count));
                continue;
            }
            var bar = new LoadItem();
            var sh = new ShellLoadItem();
            for (int k = 0; k < columns.Count; k++)
            {
                string c = columns[k];
                string cp = $"{rp}[{k}]";
                var cell = cells[k];
                if (c == Label)
                {
                    string label = cell is JsonValue lv && lv.TryGetValue(out string? ls) ? ls
                                 : cell is JsonValue nv && nv.TryGetValue(out double ld) ? ld.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                 : cell is null ? "" : "\u0000";
                    if (label == "\u0000") errors.Add(cp, Loc.S("CliModelErrNotString"));
                    bar.Label = sh.Label = label;
                    continue;
                }
                if (c is Elem or Sect)
                {
                    int? n = null;
                    if (cell is not null)
                    {
                        if (cell is JsonValue iv && iv.TryGetValue(out int x)) n = x;
                        else { errors.Add(cp, Loc.S("CliModelErrNotInteger")); continue; }
                    }
                    if (c == Elem) bar.SourceElementNum = sh.SourceElementNum = n;
                    else bar.SourceSectionNum = sh.SourceSectionNum = n;
                    continue;
                }
                double? value = null;
                if (cell is null)
                {
                    if (!ForceSetAuthoring.StressColumns.Contains(c)) { errors.Add(cp, Loc.S("CliModelErrNotNumber")); continue; }
                }
                else if (cell is JsonValue dv && dv.TryGetValue(out double x)) value = x;
                else { errors.Add(cp, Loc.S("CliModelErrNotNumber")); continue; }
                if (shell) ForceSetAuthoring.SetShellValue(sh, c, value);
                else ForceSetAuthoring.SetBarValue(bar, c, value ?? 0);
            }
            if (shell) { sh.Num = fs.ShellItems.Count + 1; fs.ShellItems.Add(sh); }
            else { bar.Num = fs.Items.Count + 1; fs.Items.Add(bar); }
        }
        return errors.Count > before ? null : new SetDoc(path, fs);
    }

    public IReadOnlyList<ModelChange> Plan(JsonArray items, JsonObject doc, HeadlessProjectContext ctx, ModelErrors errors)
    {
        var db = ctx.Db;
        var changes = new List<ModelChange>();
        var seen = new HashSet<string>();
        for (int i = 0; i < items.Count; i++)
        {
            string path = $"forceSets[{i}]";
            if (items[i] is not JsonObject item)
            {
                errors.Add(path, Loc.S("CliModelErrNotObject"));
                continue;
            }
            var d = Parse(item, path, errors);
            if (d == null) continue;
            string key = d.Set.Tag;
            if (!seen.Add(key))
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), key));
                continue;
            }
            var existing = db.ForceSets.Where(f => f.Tag.Trim() == key).ToList();
            if (existing.Count > 1)
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrAmbiguousKey"), existing.Count, key));
                continue;
            }
            var target = existing.SingleOrDefault();
            if (target != null && Linked(target))
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliForceSetErrLinked"), key));
                continue;
            }
            if (target != null && target.Kind != d.Set.Kind)
            {
                errors.Add($"{path}.kind", string.Format(Loc.S("CliForceSetErrKindChange"), key, target.Kind, d.Set.Kind));
                continue;
            }
            int before = errors.Count;
            foreach (var e in ForceSetAuthoring.Validate(d.Set))
                errors.Add($"{path}.{e.Path}", e.Message);
            if (errors.Count > before) continue;

            // Строки, на которые ссылаются задачи, должны остаться (Id переходят по позиции).
            if (target != null)
            {
                var referenced = db.ForceSetReferences(target.Id).ItemIds;
                var oldIds = RowIds(target);
                int newCount = d.Set.Kind == "shell" ? d.Set.ShellItems.Count : d.Set.Items.Count;
                for (int r = newCount; r < oldIds.Count; r++)
                    if (referenced.Contains(oldIds[r]))
                        errors.Add($"{path}.rows", string.Format(Loc.S("CliForceSetErrRowInUse"), r + 1, oldIds.Count, newCount));
                if (errors.Count > before) continue;
            }
            changes.Add(new ModelChange(Name, key, target == null, c => Commit(c, d, target)));
        }
        return changes;
    }

    static List<int> RowIds(ForceSet fs) =>
        fs.Kind == "shell" ? fs.ShellItems.Select(r => r.Id).ToList() : fs.Items.Select(r => r.Id).ToList();

    /// <summary>Новый — добавить; существующий — заменить на месте (тот же Id набора, Id строк по позиции).</summary>
    static void Commit(HeadlessProjectContext ctx, SetDoc d, ForceSet? target)
    {
        var fs = target ?? new ForceSet();
        var oldIds = target != null ? RowIds(target) : [];
        ForceSetAuthoring.CopyInto(d.Set, fs);
        fs.SourceType = d.Set.SourceType;
        if (fs.Kind == "shell")
            for (int r = 0; r < fs.ShellItems.Count; r++) fs.ShellItems[r].Id = r < oldIds.Count ? oldIds[r] : 0;
        else
            for (int r = 0; r < fs.Items.Count; r++) fs.Items[r].Id = r < oldIds.Count ? oldIds[r] : 0;
        var errors = ForceSetAuthoring.Save(ctx.Db, fs);
        if (errors.Count > 0)   // не должно случиться: Plan проверил тем же Validate
            throw new CliException(CliExitCode.Failed, string.Join("; ", errors.Select(e => $"{d.Path}.{e.Path}: {e.Message}")));
    }
}
