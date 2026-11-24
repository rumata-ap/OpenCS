using System.Text.Json.Nodes;
using CScore;
using CScore.Fire.Entities;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Раздел «fireSections»: огневые сечения, ключ — обозначение (tag). Сечение — <c>section</c> (ключ из <c>sections</c>,
/// в том числе создаваемого тем же документом). Группы <c>fire</c> (мин), <c>mesh</c> (шаг — мм), <c>time</c> (с, мин,
/// °C), <c>bc</c> (шаблон, отверстия, ручные ГУ рёбер по индексам контура основной бетонной области). Тепловой
/// расчёт в документе не описывается: при изменении входных данных он остаётся и становится устаревшим (решение
/// пользователя 24.11) — с предупреждением; смена сечения переводит огневые задачи на новое сечение и удаляет их
/// результаты. Тип элемента сетки — только T3 (в документе не задаётся).
/// </summary>
sealed class FireSectionsKind : IModelKind
{
    public string Name => "fireSections";
    public string SchemaResource => "OpenCS.Cli.Model.Schemas.fireSections.schema.json";

    static readonly string[] TopKeys = ["tag", "section", "fire", "mesh", "time", "bc"];
    static readonly string[] FireKeys = ["durationMin", "curve", "aggregate"];
    static readonly string[] MeshKeys = ["stepMm", "algorithm", "smoothing"];
    static readonly string[] TimeKeys = ["stepS", "snapshotStepMin", "theta", "picardTol", "picardMaxIter"];
    static readonly string[] BcKeys = ["preset", "holes", "edges"];
    static readonly string[] EdgeKeys = ["contour", "hole", "edge", "type", "alpha", "emissivity", "tAmbient"];

    // ── Выгрузка ────────────────────────────────────────────────────────────────────────────

    public JsonArray Export(HeadlessProjectContext ctx, Action<string> warn)
    {
        var db = ctx.Db;
        var items = new JsonArray();
        var seen = new HashSet<string>();
        foreach (var f in db.FireSections.OrderBy(f => f.Num))
        {
            string key = f.Tag.Trim();
            if (!seen.Add(key))
            {
                warn(string.Format(Loc.S("CliFireDuplicateSkipped"), key));
                continue;
            }
            var section = db.CrossSections.FirstOrDefault(s => s.Id == f.SectionId);
            string? sectionTag = section?.Tag.Trim();
            if (section == null || db.CrossSections.Count(s => s.Tag.Trim() == sectionTag) != 1)
            {
                warn(string.Format(Loc.S("CliFireSkipped"), key, Loc.S("FireSection_SectionNotFound")));
                continue;
            }
            items.Add(ToJson(f, sectionTag!));
        }
        return items;
    }

    static JsonObject ToJson(FireSectionDef f, string sectionTag)
    {
        var fire = new JsonObject { ["durationMin"] = f.FireDurationMin, ["curve"] = f.FireCurve };
        if (!string.IsNullOrWhiteSpace(f.AggregateType)) fire["aggregate"] = f.AggregateType;
        var edges = new JsonArray();
        foreach (var g in f.Edges.OrderBy(e => e.ContourType == "hole" ? 1 : 0).ThenBy(e => e.HoleIndex ?? -1).ThenBy(e => e.EdgeIndex))
        {
            var eo = new JsonObject { ["contour"] = g.ContourType };
            if (g.ContourType == "hole") eo["hole"] = g.HoleIndex ?? 0;
            eo["edge"] = g.EdgeIndex;
            eo["type"] = g.BcType;
            eo["alpha"] = g.AlphaConv;
            eo["emissivity"] = g.Emissivity;
            eo["tAmbient"] = g.TAmbientCelsius;
            edges.Add(eo);
        }
        return new JsonObject
        {
            ["tag"] = f.Tag.Trim(),
            ["section"] = sectionTag,
            ["fire"] = fire,
            ["mesh"] = new JsonObject
            {
                ["stepMm"] = ContourJson.Mm(f.MeshStepM), ["algorithm"] = f.Algorithm, ["smoothing"] = f.SmoothIterTri,
            },
            ["time"] = new JsonObject
            {
                ["stepS"] = f.TimeStepS, ["snapshotStepMin"] = f.SnapshotStepMin, ["theta"] = f.Theta,
                ["picardTol"] = f.PicardTolCelsius, ["picardMaxIter"] = f.PicardMaxIter,
            },
            ["bc"] = new JsonObject { ["preset"] = f.BcPreset, ["holes"] = f.HoleBcPreset, ["edges"] = edges },
        };
    }

    // ── Разбор ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Огневое сечение документа: поля в единицах базы, сечение — ключом.</summary>
    sealed record FireDoc(string Path, FireSectionDef Def, string Section);

    static void Unknown(JsonObject o, string[] allowed, string path, ModelErrors errors)
    {
        foreach (var (key, _) in o)
            if (!allowed.Contains(key)) errors.Add($"{path}.{key}", Loc.S("CliModelErrUnknownField"));
    }

    static JsonObject Group(JsonObject o, string key, string[] allowed, string path, ModelErrors errors)
    {
        if (o[key] is null) return new JsonObject();
        if (o[key] is JsonObject g)
        {
            Unknown(g, allowed, $"{path}.{key}", errors);
            return g;
        }
        errors.Add($"{path}.{key}", Loc.S("CliModelErrNotObject"));
        return new JsonObject();
    }

    static (string Json, string Value)[] Same(string[] values) => values.Select(v => (v, v)).ToArray();

    static FireDoc? Parse(JsonObject o, string path, ModelErrors errors)
    {
        int before = errors.Count;
        Unknown(o, TopKeys, path, errors);
        string? tag = SectionJson.Str(o, "tag", path, errors, required: true);
        string? section = SectionJson.Str(o, "section", path, errors, required: true);
        var fire = Group(o, "fire", FireKeys, path, errors);
        var mesh = Group(o, "mesh", MeshKeys, path, errors);
        var time = Group(o, "time", TimeKeys, path, errors);
        var bc = Group(o, "bc", BcKeys, path, errors);
        string fp = $"{path}.fire", mp = $"{path}.mesh", tp = $"{path}.time", bp = $"{path}.bc";

        var d = new FireSectionDef
        {
            Tag = tag ?? "",
            FireDurationMin = SectionJson.Num(fire, "durationMin", fp, errors) ?? 60,
            FireCurve = SectionJson.Pick(fire, "curve", Same(FireSectionAuthoring.Curves), "iso834", fp, errors),
            AggregateType = fire["aggregate"] is null ? ""
                : SectionJson.Pick(fire, "aggregate", Same(FireSectionAuthoring.Aggregates), "", fp, errors),
            MeshStepM = (SectionJson.Num(mesh, "stepMm", mp, errors) ?? 10) / 1000.0,
            Algorithm = SectionJson.Pick(mesh, "algorithm", Same(FireSectionAuthoring.Algorithms), "ruppert", mp, errors),
            SmoothIterTri = SectionJson.Int(mesh, "smoothing", mp, errors) ?? 5,
            MeshElementType = "linear",
            TimeStepS = SectionJson.Num(time, "stepS", tp, errors) ?? 5,
            SnapshotStepMin = SectionJson.Num(time, "snapshotStepMin", tp, errors) ?? 5,
            Theta = SectionJson.Num(time, "theta", tp, errors) ?? 1,
            PicardTolCelsius = SectionJson.Num(time, "picardTol", tp, errors) ?? 0.5,
            PicardMaxIter = SectionJson.Int(time, "picardMaxIter", tp, errors) ?? 20,
            BcPreset = SectionJson.Pick(bc, "preset", Same(FireSectionAuthoring.BcPresets), "manual", bp, errors),
            HoleBcPreset = SectionJson.Pick(bc, "holes", Same(FireSectionAuthoring.HolePresets), "ambient", bp, errors),
        };
        if (bc["edges"] is JsonArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                string ep = $"{bp}.edges[{i}]";
                if (arr[i] is not JsonObject eo)
                {
                    errors.Add(ep, Loc.S("CliModelErrNotObject"));
                    continue;
                }
                Unknown(eo, EdgeKeys, ep, errors);
                string contour = SectionJson.Pick(eo, "contour", Same(["outer", "hole"]), "outer", ep, errors);
                int? edge = SectionJson.Int(eo, "edge", ep, errors);
                if (eo["edge"] is null) errors.Add($"{ep}.edge", Loc.S("CliModelErrRequired"));
                string? type = SectionJson.Str(eo, "type", ep, errors, required: true);
                if (type != null && !FireSectionAuthoring.EdgeTypes.Contains(type))
                    errors.Add($"{ep}.type", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", FireSectionAuthoring.EdgeTypes)));
                int? hole = SectionJson.Int(eo, "hole", ep, errors);
                if (contour == "hole" && hole == null) errors.Add($"{ep}.hole", Loc.S("FireErrEdgeHole"));
                if (contour == "outer" && eo["hole"] != null) errors.Add($"{ep}.hole", Loc.S("CliModelErrUnknownField"));
                var (alpha, eps) = type switch { "fire" => (25.0, 0.7), "ambient" => (9.0, 0.8), _ => (0.0, 0.0) };
                d.Edges.Add(new FireBoundaryEdgeDef
                {
                    ContourType = contour, HoleIndex = contour == "hole" ? hole : null, EdgeIndex = edge ?? 0,
                    BcType = type ?? "adiabatic",
                    AlphaConv = SectionJson.Num(eo, "alpha", ep, errors) ?? alpha,
                    Emissivity = SectionJson.Num(eo, "emissivity", ep, errors) ?? eps,
                    TAmbientCelsius = SectionJson.Num(eo, "tAmbient", ep, errors) ?? 20,
                });
            }
        }
        else if (bc["edges"] is not null)
            errors.Add($"{bp}.edges", Loc.S("CliModelErrNotArray"));
        return errors.Count > before ? null : new FireDoc(path, d, section!);
    }

    // ── План и запись ───────────────────────────────────────────────────────────────────────

    /// <summary>Сечения документа (ключи раздела sections).</summary>
    static HashSet<string> DocSections(JsonObject doc) =>
        (doc["sections"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(o => o["tag"] is JsonValue v && v.TryGetValue(out string? s) ? s.Trim() : null)
            .OfType<string>().ToHashSet();

    public IReadOnlyList<ModelChange> Plan(JsonArray items, JsonObject doc, HeadlessProjectContext ctx, ModelErrors errors)
    {
        var db = ctx.Db;
        var docSections = DocSections(doc);
        var changes = new List<ModelChange>();
        var seen = new HashSet<string>();
        for (int i = 0; i < items.Count; i++)
        {
            string path = $"fireSections[{i}]";
            if (items[i] is not JsonObject item)
            {
                errors.Add(path, Loc.S("CliModelErrNotObject"));
                continue;
            }
            var d = Parse(item, path, errors);
            if (d == null) continue;
            string key = d.Def.Tag.Trim();
            if (key.Length > 0 && !seen.Add(key))
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), key));
                continue;
            }
            var existing = db.FireSections.Where(f => f.Tag.Trim() == key).ToList();
            if (existing.Count > 1)
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrAmbiguousKey"), existing.Count, key));
                continue;
            }
            var target = existing.SingleOrDefault();

            int before = errors.Count;
            if (docSections.Contains(d.Section))
            {
                // Сечение создаётся тем же документом: поля проверяются сейчас, контур — при записи.
                foreach (var e in FireSectionAuthoring.ValidateFields(d.Def))
                    errors.Add(DocPath(path, e.Path), e.Message);
            }
            else
            {
                var found = db.CrossSections.Where(s => s.Tag.Trim() == d.Section).ToList();
                if (found.Count != 1)
                {
                    errors.Add($"{path}.section", found.Count > 1
                        ? string.Format(Loc.S("CliModelErrAmbiguousKey"), found.Count, d.Section)
                        : string.Format(Loc.S("CliTaskErrUnknownSection"), d.Section));
                    continue;
                }
                var probe = FireSectionAuthoring.Clone(d.Def);
                probe.SectionId = found[0].Id;
                foreach (var e in FireSectionAuthoring.Validate(db, probe))
                    errors.Add(DocPath(path, e.Path), e.Message);
            }
            if (errors.Count > before) continue;
            changes.Add(new ModelChange(Name, key, target == null, c => Commit(c, d, target)));
        }
        return changes;
    }

    /// <summary>Путь службы → путь документа (у шага сетки — тот же «mesh.stepMm»).</summary>
    static string DocPath(string path, string servicePath) => $"{path}.{servicePath}";

    /// <summary>Новое — добавить с номером; существующее — правка на месте (тот же Id, ссылки задач целы).</summary>
    static void Commit(HeadlessProjectContext ctx, FireDoc d, FireSectionDef? target)
    {
        var db = ctx.Db;
        var section = db.CrossSections.Single(s => s.Tag.Trim() == d.Section);
        var made = FireSectionAuthoring.Clone(d.Def);
        made.SectionId = section.Id;
        List<FieldError> problems;
        if (target == null)
        {
            made.Id = 0;
            made.Num = 0;
            problems = FireSectionAuthoring.Save(db, made);
        }
        else
        {
            bool sectionChanged = target.SectionId != made.SectionId;
            var (tasks, results) = sectionChanged ? FireSectionAuthoring.SectionChangeImpact(db, target)
                : (new List<CalcTask>(), 0);
            bool inputsChanged = FireSectionAuthoring.InputsChanged(target, made);
            problems = FireSectionAuthoring.Update(db, target, made);
            if (problems.Count == 0 && tasks.Count > 0)
                ctx.LogService.Warning(string.Format(Loc.S("CliFireTasksMoved"), d.Def.Tag, tasks.Count, section.Tag, results));
            if (problems.Count == 0 && inputsChanged
                && FireThermalRunner.State(db, target, section) is (true, { } stale))
                ctx.LogService.Warning(string.Format(Loc.S("CliFireThermalStale"), d.Def.Tag, stale));
        }
        if (problems.Count > 0)
            throw new CliException(CliExitCode.Usage, string.Join("; ", problems.Select(e => $"{d.Path}.{e.Path}: {e.Message}")));
    }
}
