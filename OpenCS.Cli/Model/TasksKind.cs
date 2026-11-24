using System.Text.Json.Nodes;
using CScore;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Раздел «tasks»: расчётные задачи сечений (стержни и пластины), ключ — имя (tag). Сечение — <c>section</c> (ключ из
/// <c>sections</c> или <c>plateSections</c>, по виду задачи), набор — <c>forceSet</c> (имя), строка — <c>row</c>
/// (номер строки в наборе), тип расчёта — <c>calcType</c>. Параметры — объект <c>params</c>, как <c>params_json</c>
/// задачи, единицы — базы (как в окне задачи; продавливание — м, кН, кПа); ссылки по Id внутри заменены ключами:
/// <c>ForceSet</c>/<c>ForceItem</c> (этапы), <c>ForceSetLong</c>/<c>ForceItemLong</c> (длительная часть ширины
/// раскрытия), <c>concreteMaterial</c>/<c>material</c> (продавливание), <c>Area</c> (группы потерь),
/// <c>batchSource.task</c>/<c>surfaceSource.task</c>. Огневые задачи и задачи на
/// наборах, привязанных к схеме МКЭ, не выгружаются и через <c>apply</c> не правятся. Изменение входных данных
/// существующей задачи удаляет её результаты (решение пользователя 24.11) — с предупреждением в журнале.
/// </summary>
sealed class TasksKind : IModelKind
{
    public string Name => "tasks";
    public string SchemaResource => "OpenCS.Cli.Model.Schemas.tasks.schema.json";

    /// <summary>Пары «поле Id параметров → поле ключа документа» (регистр сохраняется: отбрасывается «Id»).</summary>
    static readonly string[] SetKeys = ["ForceSetId", "ForceSetLongId"];
    static readonly Dictionary<string, string> ItemOfSet = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ForceSetId"] = "ForceItemId", ["ForceSetLongId"] = "ForceItemLongId",
    };
    static readonly string[] MaterialKeys = ["ConcreteMaterialId", "MaterialId"];
    const string AreaKey = "AreaId", SourceTaskKey = "taskId";

    static bool Is(string key, string name) => string.Equals(key, name, StringComparison.OrdinalIgnoreCase);
    static string DocKey(string idKey) => idKey[..^2];

    /// <summary>Задача не переносится документом: огневая, неизвестный вид.</summary>
    static string? Excluded(CalcTask t)
    {
        var spec = CalcTaskKinds.Get(t.Kind);
        if (spec == null) return "kind";
        if (spec.Section == CalcTaskSectionKind.Fire) return "fire";
        return null;
    }

    static bool LinkedSet(ForceSet fs) => fs.SourceSchemaId != null || fs.SourceMemberId != null || fs.SourceElementId != null;

    // ── Выгрузка ────────────────────────────────────────────────────────────────────────────

    public JsonArray Export(HeadlessProjectContext ctx, Action<string> warn)
    {
        var db = ctx.Db;
        var items = new JsonArray();
        var seen = new HashSet<string>();
        int fire = 0, linked = 0;
        var taskTags = db.CalcTasks.GroupBy(t => t.Tag.Trim()).Where(g => g.Count() == 1).ToDictionary(g => g.Single().Id, g => g.Key);
        foreach (var t in db.CalcTasks.OrderBy(t => t.Num))
        {
            switch (Excluded(t))
            {
                case "fire": fire++; continue;
                case "kind": warn(string.Format(Loc.S("CliTaskSkipped"), t.Tag, string.Format(Loc.S("CalcTaskErrKind"), t.Kind))); continue;
            }
            string key = t.Tag.Trim();
            if (!seen.Add(key))
            {
                warn(string.Format(Loc.S("CliTaskDuplicateSkipped"), key));
                continue;
            }
            try
            {
                items.Add(ToJson(t, ctx, taskTags));
            }
            catch (LinkedSetException)
            {
                linked++;
            }
            catch (InvalidOperationException e)
            {
                warn(string.Format(Loc.S("CliTaskSkipped"), key, e.Message));
            }
        }
        if (fire > 0) warn(string.Format(Loc.S("CliTaskFireSkipped"), fire));
        if (linked > 0) warn(string.Format(Loc.S("CliTaskLinkedSkipped"), linked));
        return items;
    }

    sealed class LinkedSetException : Exception;

    static string UniqueTag<T>(IEnumerable<T> items, int id, Func<T, int> idOf, Func<T, string> tagOf, string kindKey)
    {
        var obj = items.FirstOrDefault(i => idOf(i) == id)
                  ?? throw new InvalidOperationException(string.Format(Loc.S("CliTaskRefMissing"), Loc.S(kindKey), id));
        string tag = tagOf(obj).Trim();
        if (items.Count(i => tagOf(i).Trim() == tag) > 1)
            throw new InvalidOperationException(string.Format(Loc.S("CliModelErrAmbiguousKey"), items.Count(i => tagOf(i).Trim() == tag), tag));
        return tag;
    }

    static string SetTag(DatabaseService db, int setId)
    {
        var fs = db.ForceSets.FirstOrDefault(f => f.Id == setId);
        if (fs != null && LinkedSet(fs)) throw new LinkedSetException();
        return UniqueTag(db.ForceSets, setId, f => f.Id, f => f.Tag, "CliKindForceSet");
    }

    static int RowNum(DatabaseService db, int setId, int itemId)
    {
        var fs = db.ForceSets.First(f => f.Id == setId);
        bool shell = fs.Kind == "shell";
        var row = db.ForceItemRef(itemId, shell);
        if (row?.SetId != setId) throw new InvalidOperationException(string.Format(Loc.S("CalcTaskErrRowNotInSet"), fs.Tag));
        if (db.ForceItemIdsByNum(setId, row.Value.Num, shell).Count != 1)
            throw new InvalidOperationException(string.Format(Loc.S("CliTaskRowNumAmbiguous"), row.Value.Num, fs.Tag));
        return row.Value.Num;
    }

    static JsonObject ToJson(CalcTask t, HeadlessProjectContext ctx, Dictionary<int, string> taskTags)
    {
        var db = ctx.Db;
        var spec = CalcTaskKinds.Get(t.Kind)!;
        var o = new JsonObject { ["tag"] = t.Tag.Trim(), ["kind"] = t.Kind, ["calcType"] = t.CalcType.ToString() };
        if (t.SectionId != 0)
            o["section"] = spec.Section == CalcTaskSectionKind.Plate
                ? UniqueTag(db.PlateSections, t.SectionId, s => s.Id, s => s.Tag, "CliKindPlateSection")
                : UniqueTag(db.CrossSections, t.SectionId, s => s.Id, s => s.Tag, "CliKindSection");
        if (t.ForceSetId != 0)
        {
            o["forceSet"] = SetTag(db, t.ForceSetId);
            if (t.ForceItemId != 0) o["row"] = RowNum(db, t.ForceSetId, t.ForceItemId);
        }
        if (!string.IsNullOrWhiteSpace(t.ParamsJson) && JsonNode.Parse(t.ParamsJson) is JsonObject p && p.Count > 0)
        {
            var section = db.CrossSections.FirstOrDefault(s => s.Id == t.SectionId);
            ParamsToDoc(p, db, section, taskTags);
            if (p.Count > 0) o["params"] = p;
        }
        return o;
    }

    /// <summary>Id внутри параметров → ключи документа (на месте).</summary>
    static void ParamsToDoc(JsonNode? node, DatabaseService db, CrossSection? section, Dictionary<int, string> taskTags)
    {
        switch (node)
        {
            case JsonArray a:
                foreach (var n in a) ParamsToDoc(n, db, section, taskTags);
                break;
            case JsonObject o:
                // Служебные ссылки на проверки МКЭ (источник одиночной задачи, справочный источник продавливания)
                // между проектами не переносятся; при обновлении задачи они берутся из базы (KeepFemSource).
                o.Remove("femCheckSource");
                if (o["source"] is JsonObject ps) ps.Remove("checkId");
                foreach (var key in o.Select(kv => kv.Key).ToList())
                {
                    var value = o[key];
                    if (key is "batchSource" or "surfaceSource" && value is JsonObject src
                        && src.FirstOrDefault(kv => Is(kv.Key, SourceTaskKey)) is { Key: { } tk, Value: JsonValue tv }
                        && tv.TryGetValue(out int taskId))
                    {
                        src.Remove(tk);
                        if (taskTags.TryGetValue(taskId, out var tag)) src["task"] = tag;
                        continue;
                    }
                    if (SetKeys.Any(k => Is(key, k)))
                    {
                        string itemKey = o.Select(kv => kv.Key).FirstOrDefault(k => Is(k, ItemOfSet[key])) ?? ItemOfSet[key];
                        int setId = value is JsonValue sv && sv.TryGetValue(out int sid) ? sid : 0;
                        int itemId = o[itemKey] is JsonValue iv && iv.TryGetValue(out int iid) ? iid : 0;
                        o.Remove(key);
                        o.Remove(itemKey);
                        if (setId == 0) continue;
                        o[DocKey(key)] = SetTag(db, setId);
                        if (itemId != 0) o[DocKey(itemKey)] = RowNum(db, setId, itemId);
                        continue;
                    }
                    if (MaterialKeys.Any(k => Is(key, k)) && value is JsonValue mv && mv.TryGetValue(out int mid))
                    {
                        o.Remove(key);
                        if (mid != 0) o[DocKey(key)] = UniqueTag(db.Materials, mid, m => m.Id, m => m.Tag, "CliKindMaterial");
                        continue;
                    }
                    if (Is(key, AreaKey) && value is JsonValue av && av.TryGetValue(out int aid))
                    {
                        o.Remove(key);
                        var areas = section?.Areas ?? [];
                        o[DocKey(key)] = UniqueTag(areas, aid, a => a.Id, a => a.Tag, "CliKindArea");
                        continue;
                    }
                    ParamsToDoc(value, db, section, taskTags);
                }
                break;
        }
    }

    // ── Разбор и план ───────────────────────────────────────────────────────────────────────

    /// <summary>Задача документа: поля и параметры с ключами вместо Id.</summary>
    sealed record TaskDoc(string Path, string Tag, string Kind, CalcType CalcType, string? Section, string? ForceSet, int? Row,
        JsonObject Params);

    static TaskDoc? Parse(JsonObject o, string path, ModelErrors errors)
    {
        int before = errors.Count;
        foreach (var (key, _) in o)
            if (key is not ("tag" or "kind" or "calcType" or "section" or "forceSet" or "row" or "params"))
                errors.Add($"{path}.{key}", Loc.S("CliModelErrUnknownField"));
        string? tag = SectionJson.Str(o, "tag", path, errors, required: true);
        if (tag != null && tag.Trim().Length == 0) errors.Add($"{path}.tag", Loc.S("CalcTaskErrTagEmpty"));
        string? kind = SectionJson.Str(o, "kind", path, errors, required: true);
        var spec = CalcTaskKinds.Get(kind);
        if (kind != null && spec == null)
            errors.Add($"{path}.kind", string.Format(Loc.S("CalcTaskErrKind"), kind));
        else if (spec?.Section == CalcTaskSectionKind.Fire)
            errors.Add($"{path}.kind", Loc.S("CliTaskErrFire"));
        string calc = SectionJson.Str(o, "calcType", path, errors) ?? spec?.CalcTypes[0].ToString() ?? "C";
        if (!Enum.TryParse<CalcType>(calc, out var calcType) || !Enum.IsDefined(calcType))
        {
            errors.Add($"{path}.calcType", string.Format(Loc.S("CliModelErrOneOf"), "C, CL, N, NL"));
            calcType = CalcType.C;
        }
        string? section = SectionJson.Str(o, "section", path, errors);
        string? set = SectionJson.Str(o, "forceSet", path, errors);
        int? row = SectionJson.Int(o, "row", path, errors);
        JsonObject p = new();
        if (o["params"] is JsonObject po) p = (JsonObject)po.DeepClone();
        else if (o["params"] != null) errors.Add($"{path}.params", Loc.S("CliModelErrNotObject"));
        if (errors.Count > before) return null;
        return new TaskDoc(path, tag!.Trim(), kind!, calcType, section?.Trim(), set?.Trim(), row, p);
    }

    /// <summary>Разрешение ключей: в плане — объекты проекта и документа (Id документа — −1), при записи — проекта.</summary>
    interface IRefs
    {
        /// <summary>Id сечения нужного вида; null — нет; ошибка вида — <paramref name="error"/>.</summary>
        int? Section(string tag, CalcTaskSectionKind kind, out string? error);
        /// <summary>Набор: Id и вид; null — нет.</summary>
        (int Id, string Kind)? Set(string tag, out string? error);
        int? Row(string setTag, int num, out string? error);
        int? Material(string tag);
        int? Area(string sectionTag, string areaTag, out string? error);
        int? Task(string tag);
    }

    /// <summary>Ключи → Id в параметрах (на месте); ошибки — с путём.</summary>
    static void ParamsFromDoc(JsonNode? node, IRefs refs, string? sectionTag, string path, ModelErrors errors)
    {
        switch (node)
        {
            case JsonArray a:
                for (int i = 0; i < a.Count; i++) ParamsFromDoc(a[i], refs, sectionTag, $"{path}[{i}]", errors);
                break;
            case JsonObject o:
                foreach (var key in o.Select(kv => kv.Key).ToList())
                {
                    var value = o[key];
                    string kp = $"{path}.{key}";
                    if (key is "batchSource" or "surfaceSource" && value is JsonObject src && src["task"] is JsonValue tv
                        && tv.TryGetValue(out string? taskTag))
                    {
                        src.Remove("task");
                        if (refs.Task(taskTag) is int tid) src[SourceTaskKey] = tid;
                        else errors.Add($"{kp}.task", string.Format(Loc.S("CliTaskErrUnknownTask"), taskTag));
                        continue;
                    }
                    var setKey = SetKeys.FirstOrDefault(k => Is(key, DocKey(k)));
                    if (setKey != null && value is JsonValue sv && sv.TryGetValue(out string? setTag))
                    {
                        string itemDocKey = DocKey(ItemOfSet[setKey]);
                        string? itemKey = o.Select(kv => kv.Key).FirstOrDefault(k => Is(k, itemDocKey));
                        o.Remove(key);
                        var set = refs.Set(setTag, out var setError);
                        if (set == null) { errors.Add(kp, setError ?? ""); continue; }
                        o[key + "Id"] = set.Value.Id;
                        if (itemKey != null)
                        {
                            var iv = o[itemKey];
                            o.Remove(itemKey);
                            if (iv is not JsonValue nv || !nv.TryGetValue(out int num))
                                errors.Add($"{path}.{itemKey}", Loc.S("CliModelErrNotInteger"));
                            else if (refs.Row(setTag, num, out var rowError) is int rid) o[itemKey + "Id"] = rid;
                            else errors.Add($"{path}.{itemKey}", rowError ?? "");
                        }
                        continue;
                    }
                    if (MaterialKeys.Any(k => Is(key, DocKey(k))) && value is JsonValue mv && mv.TryGetValue(out string? matTag))
                    {
                        o.Remove(key);
                        if (refs.Material(matTag) is int mid) o[key + "Id"] = mid;
                        else errors.Add(kp, string.Format(Loc.S("CliSectionErrUnknownMaterial"), matTag));
                        continue;
                    }
                    if (Is(key, DocKey(AreaKey)) && value is JsonValue avv && avv.TryGetValue(out string? areaTag))
                    {
                        o.Remove(key);
                        if (sectionTag != null && refs.Area(sectionTag, areaTag, out var areaError) is int aid) o[key + "Id"] = aid;
                        else errors.Add(kp, string.Format(Loc.S("CliTaskErrUnknownArea"), areaTag));
                        continue;
                    }
                    ParamsFromDoc(value, refs, sectionTag, kp, errors);
                }
                break;
        }
    }

    /// <summary>Ключи документа: сечения, наборы (с числом строк), материалы, задачи — как их увидит запись.</summary>
    sealed class PlanRefs(HeadlessProjectContext ctx, JsonObject doc, IReadOnlySet<string> docTasks) : IRefs
    {
        readonly DatabaseService _db = ctx.Db;
        readonly Dictionary<string, Material> _materials = SectionsKind.PlanMaterials(doc, ctx);

        static IEnumerable<JsonObject> Items(JsonObject doc, string kind) =>
            doc[kind] is JsonArray a ? a.OfType<JsonObject>() : [];

        static string? TagOf(JsonObject o) => o["tag"] is JsonValue v && v.TryGetValue(out string? s) ? s.Trim() : null;

        JsonObject? DocItem(string kind, string tag) => Items(doc, kind).FirstOrDefault(o => TagOf(o) == tag);

        public int? Section(string tag, CalcTaskSectionKind kind, out string? error)
        {
            error = null;
            if (kind == CalcTaskSectionKind.Plate)
            {
                if (DocItem("plateSections", tag) != null) return -1;
                var ps = _db.PlateSections.Where(s => s.Tag.Trim() == tag).ToList();
                if (ps.Count == 1) return ps[0].Id;
                error = ps.Count > 1 ? string.Format(Loc.S("CliModelErrAmbiguousKey"), ps.Count, tag)
                      : string.Format(Loc.S("CliTaskErrUnknownSection"), tag);
                return null;
            }
            if (DocItem("sections", tag) is { } d)
            {
                if (kind == CalcTaskSectionKind.Staged && d["parts"] is not JsonArray) { error = Loc.S("CalcTaskErrSectionNotStaged"); return null; }
                return -1;
            }
            var cs = _db.CrossSections.Where(s => s.Tag.Trim() == tag).ToList();
            if (cs.Count == 1)
            {
                if (kind == CalcTaskSectionKind.Staged && cs[0] is not StagedSection) { error = Loc.S("CalcTaskErrSectionNotStaged"); return null; }
                return cs[0].Id;
            }
            error = cs.Count > 1 ? string.Format(Loc.S("CliModelErrAmbiguousKey"), cs.Count, tag)
                  : string.Format(Loc.S("CliTaskErrUnknownSection"), tag);
            return null;
        }

        public (int Id, string Kind)? Set(string tag, out string? error)
        {
            error = null;
            if (DocItem("forceSets", tag) is { } d)
                return (-1, d["kind"] is JsonValue k && k.TryGetValue(out string? kind) ? kind : "bar");
            var sets = _db.ForceSets.Where(f => f.Tag.Trim() == tag).ToList();
            if (sets.Count == 1)
            {
                if (LinkedSet(sets[0])) { error = string.Format(Loc.S("CliTaskErrLinkedSet"), tag); return null; }
                return (sets[0].Id, sets[0].Kind);
            }
            error = sets.Count > 1 ? string.Format(Loc.S("CliModelErrAmbiguousKey"), sets.Count, tag)
                  : string.Format(Loc.S("CliTaskErrUnknownSet"), tag);
            return null;
        }

        public int? Row(string setTag, int num, out string? error)
        {
            error = null;
            if (DocItem("forceSets", setTag) is { } d)
            {
                int count = d["rows"] is JsonArray rows ? rows.Count : 0;
                if (num >= 1 && num <= count) return -1;
                error = string.Format(Loc.S("CliTaskErrRowRange"), num, setTag, count);
                return null;
            }
            var fs = _db.ForceSets.Single(f => f.Tag.Trim() == setTag);
            var ids = _db.ForceItemIdsByNum(fs.Id, num, fs.Kind == "shell");
            if (ids.Count == 1) return ids[0];
            error = ids.Count == 0 ? string.Format(Loc.S("CliTaskErrRowRange"), num, setTag, fs.RowCount)
                  : string.Format(Loc.S("CliTaskRowNumAmbiguous"), num, setTag);
            return null;
        }

        public int? Material(string tag) => _materials.TryGetValue(tag.Trim(), out var m) ? m.Id : null;

        public int? Area(string sectionTag, string areaTag, out string? error)
        {
            error = null;
            if (DocItem("sections", sectionTag) is { } d)
            {
                var areas = (d["areas"] as JsonArray ?? [])
                    .Concat((d["parts"] as JsonArray ?? []).OfType<JsonObject>().SelectMany(p => p["areas"] as JsonArray ?? []))
                    .OfType<JsonObject>().Count(a => TagOf(a) == areaTag.Trim());
                return areas == 1 ? -1 : null;
            }
            var section = _db.CrossSections.FirstOrDefault(s => s.Tag.Trim() == sectionTag);
            var found = section?.Areas.Where(a => a.Tag.Trim() == areaTag.Trim()).ToList() ?? [];
            return found.Count == 1 ? found[0].Id : null;
        }

        public int? Task(string tag)
        {
            if (docTasks.Contains(tag)) return -1;
            var t = _db.CalcTasks.Where(x => x.Tag.Trim() == tag).ToList();
            return t.Count == 1 ? t[0].Id : null;
        }
    }

    /// <summary>Ключи → Id проекта при записи (всё из документа уже записано предыдущими видами и задачами).</summary>
    sealed class DbRefs(DatabaseService db) : IRefs
    {
        public int? Section(string tag, CalcTaskSectionKind kind, out string? error)
        {
            error = null;
            return kind == CalcTaskSectionKind.Plate
                ? db.PlateSections.SingleOrDefault(s => s.Tag.Trim() == tag)?.Id
                : db.CrossSections.SingleOrDefault(s => s.Tag.Trim() == tag)?.Id;
        }

        public (int Id, string Kind)? Set(string tag, out string? error)
        {
            error = null;
            var fs = db.ForceSets.SingleOrDefault(f => f.Tag.Trim() == tag);
            return fs == null ? null : (fs.Id, fs.Kind);
        }

        public int? Row(string setTag, int num, out string? error)
        {
            error = null;
            var fs = db.ForceSets.Single(f => f.Tag.Trim() == setTag);
            var ids = db.ForceItemIdsByNum(fs.Id, num, fs.Kind == "shell");
            return ids.Count == 1 ? ids[0] : null;
        }

        public int? Material(string tag) => db.Materials.SingleOrDefault(m => m.Tag.Trim() == tag.Trim())?.Id;

        public int? Area(string sectionTag, string areaTag, out string? error)
        {
            error = null;
            return db.CrossSections.SingleOrDefault(s => s.Tag.Trim() == sectionTag)?.Areas
                .SingleOrDefault(a => a.Tag.Trim() == areaTag.Trim())?.Id;
        }

        public int? Task(string tag) => db.CalcTasks.SingleOrDefault(t => t.Tag.Trim() == tag)?.Id;
    }

    /// <summary>Задача документа с Id по ключам; ошибки ключей — в <paramref name="errors"/>.</summary>
    static CalcTask? Resolve(TaskDoc d, IRefs refs, ModelErrors errors)
    {
        int before = errors.Count;
        var spec = CalcTaskKinds.Get(d.Kind)!;
        var t = new CalcTask { Tag = d.Tag, Kind = d.Kind, CalcType = d.CalcType };
        if (d.Section != null)
        {
            if (spec.Section == CalcTaskSectionKind.None) errors.Add($"{d.Path}.section", Loc.S("CalcTaskErrSectionNotUsed"));
            else if (refs.Section(d.Section, spec.Section, out var e) is int sid) t.SectionId = sid;
            else errors.Add($"{d.Path}.section", e ?? "");
        }
        if (d.ForceSet != null)
        {
            var set = refs.Set(d.ForceSet, out var e);
            if (set == null) errors.Add($"{d.Path}.forceSet", e ?? "");
            else
            {
                t.ForceSetId = set.Value.Id;
                if ((set.Value.Kind == "shell") != spec.ShellSet)
                    errors.Add($"{d.Path}.forceSet", string.Format(Loc.S("CalcTaskErrSetKind"), d.ForceSet,
                        Loc.S(spec.ShellSet ? "ForceSetKindShell" : "ForceSetKindBar")));
                else if (d.Row is int num)
                {
                    if (refs.Row(d.ForceSet, num, out var re) is int rid) t.ForceItemId = rid;
                    else errors.Add($"{d.Path}.row", re ?? "");
                }
            }
        }
        else if (d.Row != null) errors.Add($"{d.Path}.row", Loc.S("CalcTaskErrRowNotUsed"));
        var p = (JsonObject)d.Params.DeepClone();
        ParamsFromDoc(p, refs, d.Section, $"{d.Path}.params", errors);
        t.ParamsJson = p.ToJsonString();
        return errors.Count > before ? null : t;
    }

    public IReadOnlyList<ModelChange> Plan(JsonArray items, JsonObject doc, HeadlessProjectContext ctx, ModelErrors errors)
    {
        var db = ctx.Db;
        var docs = new List<TaskDoc>();
        var seen = new HashSet<string>();
        for (int i = 0; i < items.Count; i++)
        {
            string path = $"tasks[{i}]";
            if (items[i] is not JsonObject item)
            {
                errors.Add(path, Loc.S("CliModelErrNotObject"));
                continue;
            }
            var d = Parse(item, path, errors);
            if (d == null) continue;
            if (!seen.Add(d.Tag))
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), d.Tag));
                continue;
            }
            docs.Add(d);
        }

        var refs = new PlanRefs(ctx, doc, seen);
        var changes = new List<ModelChange>();
        // Одиночные задачи по строке пакета/точке поверхности ссылаются на задачу-источник — записываются после неё.
        static bool HasSource(JsonObject p) => p["batchSource"] is JsonObject || p["surfaceSource"] is JsonObject;
        foreach (var d in docs.OrderBy(x => HasSource(x.Params) ? 1 : 0))
        {
            var existing = db.CalcTasks.Where(t => t.Tag.Trim() == d.Tag).ToList();
            if (existing.Count > 1)
            {
                errors.Add($"{d.Path}.tag", string.Format(Loc.S("CliModelErrAmbiguousKey"), existing.Count, d.Tag));
                continue;
            }
            var target = existing.SingleOrDefault();
            if (target != null && Excluded(target) is { } why)
            {
                errors.Add($"{d.Path}.tag", why == "fire" ? Loc.S("CliTaskErrFire") : string.Format(Loc.S("CalcTaskErrKind"), target.Kind));
                continue;
            }
            int before = errors.Count;
            var draft = Resolve(d, refs, errors);
            if (draft == null) continue;
            // Документ задаёт параметры целиком: служебный источник проверки МКЭ — из базы.
            foreach (var e in CalcTaskAuthoring.ValidateFields(draft))
                errors.Add($"{d.Path}.{e.Path}", e.Message);
            if (errors.Count > before) continue;
            changes.Add(new ModelChange(Name, d.Tag, target == null, c => Commit(c, d, target)));
        }
        return changes;
    }

    /// <summary>Новая — добавить с номером; существующая — правка на месте (тот же Id и номер), при смене входных данных — без результатов.</summary>
    static void Commit(HeadlessProjectContext ctx, TaskDoc d, CalcTask? target)
    {
        var db = ctx.Db;
        var errors = new ModelErrors();
        var t = Resolve(d, new DbRefs(db), errors);
        if (t == null) throw new CliException(CliExitCode.Failed, string.Join("; ", errors.Items));
        List<FieldError> problems;
        if (target == null)
            problems = CalcTaskAuthoring.Save(db, t);
        else
        {
            KeepFemSource(target, t);
            bool changed = CalcTaskAuthoring.InputsChanged(target, t);
            int results = CalcTaskAuthoring.ResultCount(db, target);
            problems = CalcTaskAuthoring.Update(db, target, t, deleteResults: true);
            if (problems.Count == 0 && changed && results > 0)
                ctx.LogService.Warning(string.Format(Loc.S("CliTaskResultsDeleted"), d.Tag, results));
        }
        if (problems.Count > 0)   // не должно случиться: Plan проверил тем же ValidateFields, ключи — при разрешении
            throw new CliException(CliExitCode.Failed, string.Join("; ", problems.Select(e => $"{d.Path}.{e.Path}: {e.Message}")));
    }

    /// <summary>Ссылки на проверки МКЭ документ не переносит — у существующей задачи они остаются из базы.</summary>
    static void KeepFemSource(CalcTask target, CalcTask t)
    {
        if (JsonNode.Parse(string.IsNullOrWhiteSpace(target.ParamsJson) ? "{}" : target.ParamsJson) is not JsonObject old) return;
        var p = JsonNode.Parse(t.ParamsJson) as JsonObject ?? new JsonObject();
        if (old["femCheckSource"] is { } src && p["femCheckSource"] == null) p["femCheckSource"] = src.DeepClone();
        if (old["source"] is JsonObject os && os["checkId"] is { } check && p["source"] is JsonObject ns && ns["checkId"] == null)
            ns["checkId"] = check.DeepClone();
        t.ParamsJson = p.ToJsonString();
    }
}
