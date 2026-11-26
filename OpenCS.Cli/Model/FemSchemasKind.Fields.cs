using System.Globalization;
using System.Text.Json.Nodes;
using CScore.Fem;
using CScore.Fem.Fields;
using CScore.Import;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Поля из таблиц точек (<c>fields</c> внутри <c>femSchemas</c>): величина, столбцы значений, точки, способ переноса,
/// допуски, совмещение и цели (группы, КонЭ, узлы сетки). Ключ — <c>tag</c> (имя поля). Точки — в единицах столбцов
/// таблицы (<c>[x, y, (z), значения…]</c>); <c>alignment.scale</c> переводит координаты таблицы в м. Указанный массив
/// заменяет поля схемы (без него — не трогается). Перенос на сетку — после записи, как «ОК» в окне.
/// </summary>
sealed partial class FemSchemasKind
{
    /// <summary>Столбец значений: заголовок, компонента, подпись единицы (множитель — по реестру величин).</summary>
    sealed record FieldColumnDoc(string Path, string Header, string Component, string Unit);

    /// <summary>Поле документа; точки — путь строки и её числа (в порядке <c>[x, y, (z), значения]</c>).</summary>
    sealed record FieldDoc(string Path, string Tag, string Quantity, bool HasZ, List<FieldColumnDoc> Columns,
        List<(string Path, double[] Row)> Points, FieldMethod Method, double MaxDistance, double PlaneTolerance,
        double MatchTolerance, double Scale, double Dx, double Dy, double Dz, double AngleDeg, bool MirrorX, bool MirrorY,
        List<string> Groups, List<string> Members, List<string> Elems, List<string> Nodes, bool OnlyNodesWithSprings,
        bool Enabled);

    static readonly string[] FieldKeys = ["tag", "quantity", "hasZ", "columns", "points", "method", "maxDistanceM",
        "planeToleranceM", "matchToleranceM", "alignment", "targets", "enabled"];
    static readonly string[] FieldColumnKeys = ["header", "component", "unit"];
    static readonly string[] FieldAlignKeys = ["scale", "dxM", "dyM", "dzM", "angleDeg", "mirrorX", "mirrorY"];
    static readonly string[] FieldTargetKeys = ["groups", "members", "elems", "nodes", "onlyNodesWithSprings"];

    /// <summary>Способы переноса в документе; у пружин (цели — узлы сетки) способ всегда «match».</summary>
    static readonly Dictionary<string, FieldMethod> FieldMethods = new()
    {
        ["interpolate"] = FieldMethod.Interpolate, ["nearest"] = FieldMethod.Nearest, ["idw"] = FieldMethod.Idw,
        ["match"] = FieldMethod.Match,
    };

    static string FieldMethodName(FieldMethod m) => FieldMethods.First(x => x.Value == m).Key;

    // ── Выгрузка ────────────────────────────────────────────────────────────────────────────

    static JsonArray FieldsJson(DatabaseService db, FemSchema schema)
    {
        var groupTags = schema.MemberGroups.ToDictionary(g => g.Id, g => g.Tag);
        var items = new JsonArray();
        foreach (var f in db.GetFemFields(schema.Id)) items.Add(FieldJson(f, groupTags));
        return items;
    }

    /// <summary>Поле в виде документа; им же сравнивается прежнее поле с документом (без изменений — не пишется).</summary>
    static JsonObject FieldJson(FemField f, IReadOnlyDictionary<int, string> groupTags)
    {
        var def = f.Definition;
        var points = new JsonArray();
        foreach (var p in f.Points)
        {
            var row = new JsonArray { R(p.P.X), R(p.P.Y) };
            if (f.HasZ) row.Add(R(p.P.Z));
            foreach (var v in p.Values) row.Add(R(v));
            points.Add(row);
        }
        var columns = new JsonArray();
        foreach (var c in def.Columns)
            columns.Add(new JsonObject { ["header"] = c.Header, ["component"] = c.Component, ["unit"] = c.UnitLabel });
        var a = def.Alignment;
        var t = def.Targets;
        var targets = new JsonObject
        {
            ["groups"] = new JsonArray(t.GroupIds.Where(groupTags.ContainsKey).Select(id => (JsonNode)groupTags[id]).ToArray()),
            ["members"] = new JsonArray(t.MemberTags.Select(x => (JsonNode)x).ToArray()),
            ["elems"] = new JsonArray(t.ElemTags.Select(x => (JsonNode)x).ToArray()),
            ["nodes"] = new JsonArray(t.NodeTags.Select(x => (JsonNode)x).ToArray()),
            ["onlyNodesWithSprings"] = t.OnlyNodesWithSprings,
        };
        return new JsonObject
        {
            ["tag"] = f.Name, ["quantity"] = f.Quantity, ["hasZ"] = f.HasZ, ["columns"] = columns, ["points"] = points,
            ["method"] = FieldMethodName(def.Settings.Method),
            ["maxDistanceM"] = R(def.Settings.MaxDistance), ["planeToleranceM"] = R(def.Settings.PlaneTolerance),
            ["matchToleranceM"] = R(def.Settings.MatchTolerance),
            ["alignment"] = new JsonObject
            {
                ["scale"] = R(a.Scale), ["dxM"] = R(a.Dx), ["dyM"] = R(a.Dy), ["dzM"] = R(a.Dz),
                ["angleDeg"] = R(a.AngleDeg), ["mirrorX"] = a.MirrorX, ["mirrorY"] = a.MirrorY,
            },
            ["targets"] = targets, ["enabled"] = f.Enabled,
        };
    }

    // ── Разбор ──────────────────────────────────────────────────────────────────────────────

    static List<FieldDoc>? ParseFields(JsonNode? node, string path, ModelErrors errors)
    {
        if (node is null) return null;
        if (node is not JsonArray arr) { errors.Add($"{path}.fields", Loc.S("CliModelErrNotArray")); return null; }
        var list = new List<FieldDoc>();
        for (int i = 0; i < arr.Count; i++)
        {
            string p = $"{path}.fields[{i}]";
            if (arr[i] is not JsonObject o) { errors.Add(p, Loc.S("CliModelErrNotObject")); continue; }
            Unknown(o, FieldKeys, p, errors);
            string? tag = SectionJson.Str(o, "tag", p, errors, required: true);
            string quantity = SectionJson.Str(o, "quantity", p, errors, required: true) ?? "";
            var known = FemFieldQuantities.All.FirstOrDefault(q => q.Code == quantity);
            if (quantity != "" && known == null)
                errors.Add($"{p}.quantity", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", FemFieldQuantities.All.Select(q => q.Code))));
            bool spring = quantity == FemFieldQuantities.Spring;
            bool hasZ = SectionJson.Bool(o, "hasZ", p, errors) ?? false;

            var columns = new List<FieldColumnDoc>();
            if (o["columns"] is JsonArray ca)
                for (int j = 0; j < ca.Count; j++)
                {
                    string cp = $"{p}.columns[{j}]";
                    if (ca[j] is not JsonObject co) { errors.Add(cp, Loc.S("CliModelErrNotObject")); continue; }
                    Unknown(co, FieldColumnKeys, cp, errors);
                    string header = SectionJson.Str(co, "header", cp, errors) ?? "";
                    string component = SectionJson.Str(co, "component", cp, errors, required: true) ?? "";
                    string unit = SectionJson.Str(co, "unit", cp, errors, required: true) ?? "";
                    if (known != null && !known.Components.Contains(component))
                        errors.Add($"{cp}.component", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", known.Components)));
                    else if (known != null && !FemFieldQuantities.Units(quantity, component, LiraImportSettings.Default.TonToKnFactor).Any(x => x.Label == unit))
                        errors.Add($"{cp}.unit", string.Format(Loc.S("CliFemFieldErrColumnUnit"), unit, component));
                    columns.Add(new FieldColumnDoc(cp, header, component, unit));
                }
            else errors.Add($"{p}.columns", o["columns"] is null ? Loc.S("CliModelErrRequired") : Loc.S("CliModelErrNotArray"));

            int width = (hasZ ? 3 : 2) + columns.Count;
            var points = new List<(string, double[])>();
            if (o["points"] is JsonArray pa)
                for (int k = 0; k < pa.Count; k++)
                {
                    string pp = $"{p}.points[{k}]";
                    if (pa[k] is not JsonArray row || row.Count != width
                        || !row.All(x => x is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d)))
                    {
                        errors.Add(pp, string.Format(Loc.S("FemFieldPointBad"), k + 1));
                        continue;
                    }
                    points.Add((pp, row.Select(x => x!.GetValue<double>()).ToArray()));
                }
            else errors.Add($"{p}.points", o["points"] is null ? Loc.S("CliModelErrRequired") : Loc.S("CliModelErrNotArray"));

            FieldMethod method = spring ? FieldMethod.Match : FieldMethod.Interpolate;
            string? methodName = SectionJson.Str(o, "method", p, errors);
            if (methodName != null)
            {
                if (!FieldMethods.TryGetValue(methodName, out method)) errors.Add($"{p}.method", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", FieldMethods.Keys)));
                else if (spring && method != FieldMethod.Match) errors.Add($"{p}.method", string.Format(Loc.S("CliModelErrOneOf"), "match"));
            }

            double maxDistance = SectionJson.Num(o, "maxDistanceM", p, errors) ?? 1;
            double planeTolerance = SectionJson.Num(o, "planeToleranceM", p, errors) ?? 0.5;
            double matchTolerance = SectionJson.Num(o, "matchToleranceM", p, errors) ?? 0.5;

            double scale = 1, dx = 0, dy = 0, dz = 0, angle = 0;
            bool mirrorX = false, mirrorY = false;
            if (o["alignment"] is JsonObject ao)
            {
                string ap = $"{p}.alignment";
                Unknown(ao, FieldAlignKeys, ap, errors);
                scale = SectionJson.Num(ao, "scale", ap, errors) ?? 1;
                dx = SectionJson.Num(ao, "dxM", ap, errors) ?? 0;
                dy = SectionJson.Num(ao, "dyM", ap, errors) ?? 0;
                dz = SectionJson.Num(ao, "dzM", ap, errors) ?? 0;
                angle = SectionJson.Num(ao, "angleDeg", ap, errors) ?? 0;
                mirrorX = SectionJson.Bool(ao, "mirrorX", ap, errors) ?? false;
                mirrorY = SectionJson.Bool(ao, "mirrorY", ap, errors) ?? false;
            }
            else if (o["alignment"] is not null) errors.Add($"{p}.alignment", Loc.S("CliModelErrNotObject"));

            var groups = new List<string>();
            var members = new List<string>();
            var elems = new List<string>();
            var nodes = new List<string>();
            bool onlyWithSprings = true;
            if (o["targets"] is JsonObject to)
            {
                string tp = $"{p}.targets";
                Unknown(to, FieldTargetKeys, tp, errors);
                groups = TagList(to["groups"], $"{tp}.groups", errors);
                members = TagList(to["members"], $"{tp}.members", errors);
                elems = TagList(to["elems"], $"{tp}.elems", errors);
                nodes = TagList(to["nodes"], $"{tp}.nodes", errors);
                onlyWithSprings = SectionJson.Bool(to, "onlyNodesWithSprings", tp, errors) ?? true;
            }
            else if (o["targets"] is not null) errors.Add($"{p}.targets", Loc.S("CliModelErrNotObject"));

            bool enabled = SectionJson.Bool(o, "enabled", p, errors) ?? true;
            if (tag != null)
                list.Add(new FieldDoc(p, tag, quantity, hasZ, columns, points, method, maxDistance, planeTolerance,
                    matchTolerance, scale, dx, dy, dz, angle, mirrorX, mirrorY, groups, members, elems, nodes,
                    onlyWithSprings, enabled));
        }
        return list;
    }

    /// <summary>Список тегов (строки или целые числа — как теги узлов в тексте документа).</summary>
    static List<string> TagList(JsonNode? node, string path, ModelErrors errors)
    {
        var list = new List<string>();
        if (node is null) return list;
        if (node is not JsonArray arr) { errors.Add(path, Loc.S("CliModelErrNotArray")); return list; }
        for (int k = 0; k < arr.Count; k++)
        {
            if (arr[k] is JsonValue v && (v.TryGetValue(out string? s) || v.TryGetValue(out long _)))
                list.Add(v.TryGetValue(out string? s2) ? s2!.Trim() : v.GetValue<long>().ToString(CultureInfo.InvariantCulture));
            else errors.Add($"{path}[{k}]", Loc.S("CliModelErrBadValue"));
        }
        return list;
    }

    // ── План и запись ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Проверки полей без сетки: служба <see cref="FemFieldAuthoring.Validate"/> с путями документа; группы и КонЭ
    /// целей — из документа или из схемы, если её массив не указан. Узлы сетки (пружины) — при записи, по сетке.
    /// </summary>
    static void CheckFields(HeadlessProjectContext ctx, SchemaDoc d, FemSchema? target, ModelErrors errors)
    {
        if (d.Fields == null) return;
        double tonToKn = ctx.Db.LoadLiraImportSettings()?.TonToKnFactor ?? LiraImportSettings.Default.TonToKnFactor;
        var groupTags = (d.Groups ?? []).Select(g => g.Tag).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var memberTags = (d.Members ?? []).Select(m => m.Tag).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (target != null)
        {
            var session = FemSchemaAuthoring.OpenSession(ctx.Db, target);
            if (d.Groups == null) groupTags.UnionWith(session.MemberGroups.Select(g => g.Tag));
            if (d.Members == null) memberTags.UnionWith(session.Members.Select(m => m.ElemTag));
        }
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var f in d.Fields)
        {
            if (!seen.Add(f.Tag)) errors.Add($"{f.Path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), f.Tag));
            foreach (var issue in FemFieldAuthoring.Validate(ToField(f, 0, new Dictionary<string, int>(), tonToKn)))
                errors.Add($"{f.Path}.{issue.Path}", issue.Message);
            for (int k = 0; k < f.Groups.Count; k++)
                if (!groupTags.Contains(f.Groups[k])) errors.Add($"{f.Path}.targets.groups[{k}]", string.Format(Loc.S("CliFemFieldErrUnknownGroup"), f.Groups[k]));
            for (int k = 0; k < f.Members.Count; k++)
                if (!memberTags.Contains(f.Members[k])) errors.Add($"{f.Path}.targets.members[{k}]", string.Format(Loc.S("CliFemFieldErrUnknownMember"), f.Members[k]));
        }
    }

    /// <summary>
    /// Поля схемы по документу: заменяет набор (отсутствующие удаляются, совпавшие по тегу обновляются с прежним Id),
    /// при изменении сбрасывает результаты постановок, при готовой сетке — пересчитывает поля и предупреждает о целях,
    /// которых нет на сетке.
    /// </summary>
    static void CommitFields(HeadlessProjectContext ctx, SchemaDoc d, FemSchema schema)
    {
        if (d.Fields == null) return;
        var db = ctx.Db;
        double tonToKn = db.LoadLiraImportSettings()?.TonToKnFactor ?? LiraImportSettings.Default.TonToKnFactor;
        var groups = FemSchemaAuthoring.OpenSession(db, schema).MemberGroups;
        var groupIds = groups
            .GroupBy(g => g.Tag, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.CurrentCultureIgnoreCase);
        var groupTags = groups.ToDictionary(g => g.Id, g => g.Tag);
        var existing = db.GetFemFields(schema.Id);
        var keep = d.Fields.Select(f => f.Tag).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        int removed = 0;
        foreach (var gone in existing.Where(f => !keep.Contains(f.Name)))
        {
            db.DeleteFemField(gone.Id);
            removed++;
        }
        // Поле без изменений (тот же вид документа) не перезаписывается: повторный apply того же документа не сбрасывает
        // ни кэш значений, ни результаты постановок. Изменённое пишется без подписи сетки — кэш его значений очищается.
        var changed = new List<int>();
        var order = new List<int>();
        foreach (var fd in d.Fields)
        {
            var field = ToField(fd, schema.Id, groupIds, tonToKn);
            var old = existing.FirstOrDefault(f => string.Equals(f.Name, fd.Tag, StringComparison.CurrentCultureIgnoreCase));
            if (old != null)
            {
                (field.Id, field.SourceFile) = (old.Id, old.SourceFile);
                if (JsonNode.DeepEquals(FieldJson(old, groupTags), FieldJson(field, groupTags)))
                {
                    order.Add(old.Id);
                    continue;
                }
            }
            db.SaveFemField(field);
            changed.Add(field.Id);
            order.Add(field.Id);
        }
        // Порядок наложения — порядок документа.
        bool reordered = !db.GetFemFields(schema.Id).Select(f => f.Id).SequenceEqual(order);
        if (reordered) db.SetFemFieldOrder(order);
        if (removed > 0) ctx.LogService.Warning(string.Format(Loc.S("CliFemFieldsRemoved"), d.Tag, removed));
        if (removed + changed.Count > 0 || reordered)
        {
            int invalidated = FemFieldAuthoring.InvalidateAnalyses(db, schema);
            if (invalidated > 0) ctx.LogService.Info(string.Format(Loc.S("ScadBoundaryAnalysesInvalidated"), invalidated));
        }
        if (changed.Count == 0 || db.GetFemMeshElements(schema.Id).Count == 0) return;
        var fieldCtx = FemFieldService.LoadContext(db, schema.Id);
        foreach (var field in db.GetFemFields(schema.Id).Where(f => changed.Contains(f.Id)))
        {
            FemFieldService.Recompute(db, field, fieldCtx);
            foreach (var issue in FemFieldAuthoring.ValidateTargets(field, fieldCtx))
                ctx.LogService.Warning(string.Format(Loc.S("CliFemFieldWarnTargets"), field.Name, issue.Message));
        }
    }

    /// <summary>Поле по документу; <paramref name="groupIds"/> — Id групп схемы по тегам (для проверки достаточно пустого).</summary>
    static FemField ToField(FieldDoc fd, int schemaId, IReadOnlyDictionary<string, int> groupIds, double tonToKn)
    {
        var columns = fd.Columns.Select(c => new FemFieldColumn(c.Header, c.Component, c.Unit,
            FemFieldQuantities.Units(fd.Quantity, c.Component, tonToKn)
                .FirstOrDefault(u => u.Label == c.Unit)?.ToSi ?? 1)).ToList();
        var targets = new FemFieldTargets
        {
            GroupIds = fd.Groups.Where(groupIds.ContainsKey).Select(g => groupIds[g]).ToList(),
            MemberTags = fd.Members, ElemTags = fd.Elems, NodeTags = fd.Nodes, OnlyNodesWithSprings = fd.OnlyNodesWithSprings,
        };
        var points = fd.Points.Select(p => new FieldPoint(
            new FieldVec3(p.Row[0], p.Row[1], fd.HasZ ? p.Row[2] : 0),
            p.Row.Skip(fd.HasZ ? 3 : 2).ToArray())).ToList();
        return new FemField
        {
            SchemaId = schemaId, Name = fd.Tag, Quantity = fd.Quantity, HasZ = fd.HasZ, Points = points, Enabled = fd.Enabled,
            Definition = new FemFieldDefinition
            {
                Columns = columns,
                Alignment = new FieldAlignment(fd.Scale, fd.MirrorX, fd.MirrorY, fd.AngleDeg, fd.Dx, fd.Dy, fd.Dz),
                Targets = targets,
                Settings = new FieldSettings(fd.Method, fd.MaxDistance, fd.PlaneTolerance, MatchTolerance: fd.MatchTolerance),
            },
        };
    }
}
