using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Раздел «femFragments»: фрагменты МКЭ-схем (аудит GUI + CLI, раздел «Фрагменты»), ключ — имя (<c>tag</c>). Документ
/// описывает **рецепт** фрагмента — родителя, полосу (<c>storey</c>) или призму (<c>prism</c>), «сохранять сетку родителя» — и
/// ГУ разрезов (<c>cutBc</c>). Всё остальное (узлы, КонЭ, загружения, нагрузки, сохранённая сетка, рассечения, опорная
/// сетка, отпечаток, маски) — производное: его пересоздаёт сборка. Нет схемы — фрагмент собирается (на этапе проверки);
/// есть фрагмент того же родителя по тому же рецепту — меняются только <c>cutBc</c>; другой рецепт — ошибка.
/// </summary>
sealed class FemFragmentsKind : IModelKind
{
    public string Name => "femFragments";
    public string SchemaResource => "OpenCS.Cli.Model.Schemas.femFragments.schema.json";

    static readonly string[] TopKeys = ["tag", "parent", "storey", "prism", "keepParentMesh", "cutBc"];
    static readonly string[] StoreyKeys = ["z1", "z2", "floor"];
    static readonly string[] PrismKeys = ["members", "offsetM", "floorFrom", "floorTo", "fullHeight"];
    static readonly string[] CutBcKeys = ["preset", "lateralPreset", "overrides"];
    static readonly string[] OverrideKeys = ["member", "side", "node", "face", "modes"];
    /// <summary>Направления режимов разреза: общие оси Ux Uy Uz Rx Ry Rz.</summary>
    static readonly string[] Dofs = ["ux", "uy", "uz", "rx", "ry", "rz"];

    static readonly (string Json, FragmentBcPreset Value)[] Presets =
    [
        ("kinematic", FragmentBcPreset.Kinematic), ("kinematicTranslations", FragmentBcPreset.KinematicTranslations),
        ("static", FragmentBcPreset.Static), ("isolation", FragmentBcPreset.Isolation),
    ];

    static readonly (string Json, FragmentDofMode Value)[] Modes =
    [
        ("fixed", FragmentDofMode.Fixed), ("free", FragmentDofMode.Free),
        ("maskDisplacement", FragmentDofMode.MaskDisplacement), ("maskForce", FragmentDofMode.MaskForce),
    ];

    static readonly (string Json, FragmentCutSide Value)[] Sides =
        [("bottom", FragmentCutSide.Bottom), ("top", FragmentCutSide.Top), ("lateral", FragmentCutSide.Lateral)];

    /// <summary>Допуск сверки отметок рецепта, м (как допуск запроса фрагмента).</summary>
    const double LevelTol = 1e-3;

    static double R(double v) => Math.Round(v, 9) + 0.0;

    // ── Фрагменты, собираемые этим планом (для femAnalyses и femChecks того же документа) ──────────────

    /// <summary>Фрагмент плана: черновик сборки (ещё не записан) или пропуск (родитель без сетки).</summary>
    internal sealed record Planned(string Tag, StoreyFragmentDraft? Draft, bool Skipped);

    static readonly ConditionalWeakTable<HeadlessProjectContext, Dictionary<string, Planned>> PlannedByContext = new();

    /// <summary>Фрагмент с именем <paramref name="tag"/>, который собирает или пропускает этот документ; null — нет.</summary>
    internal static Planned? PlannedFor(JsonObject doc, HeadlessProjectContext ctx, string tag) =>
        doc["femFragments"] is JsonArray && PlannedByContext.TryGetValue(ctx, out var map) && map.TryGetValue(tag.Trim(), out var p)
            ? p : null;

    /// <summary>Служебные постановки родителя «Маски фрагмента» — их пересоздаёт <c>opencs fragment masks</c>.</summary>
    internal static bool IsMaskSupplyAnalysis(FemAnalysis a) =>
        a.Tag.Trim() is StoreyFragmentMaskSupply.AnalysisTag or StoreyFragmentMaskSupply.SecantAnalysisTag;

    // ── Выгрузка ────────────────────────────────────────────────────────────────────────────

    public JsonArray Export(HeadlessProjectContext ctx, Action<string> warn)
    {
        var db = ctx.Db;
        var items = new JsonArray();
        var dup = db.FemSchemas.GroupBy(s => s.Tag.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        foreach (var schema in db.FemSchemas.Where(s => s.ParentSchemaId != null).OrderBy(s => s.Id))
        {
            string key = schema.Tag.Trim();
            if (dup.Contains(key))
            {
                warn(string.Format(Loc.S("CliFemDuplicateSkipped"), key));
                continue;
            }
            var parent = db.FemSchemas.FirstOrDefault(s => s.Id == schema.ParentSchemaId);
            if (parent == null)
            {
                warn(string.Format(Loc.S("CliFragSkippedParentDeleted"), key));
                continue;
            }
            if (dup.Contains(parent.Tag.Trim()))
            {
                warn(string.Format(Loc.S("CliFragSkippedParentAmbiguous"), key, parent.Tag.Trim()));
                continue;
            }
            FragmentPrismInfo? prism;
            try { prism = FragmentPrismCodec.Decode(schema.FragmentPrismJson); }
            catch (InvalidDataException e)
            {
                warn($"{key}: {e.Message}");
                continue;
            }
            var o = new JsonObject { ["tag"] = key, ["parent"] = parent.Tag.Trim() };
            if (prism == null)
            {
                if (schema.FragmentZ1 is not double z1 || schema.FragmentZ2 is not double z2)
                {
                    warn(string.Format(Loc.S("CliFragSkippedNoRecipe"), key));
                    continue;
                }
                o["storey"] = new JsonObject { ["z1"] = R(z1), ["z2"] = R(z2) };
            }
            else
            {
                if (prism.MemberTags is not { Count: > 0 } tags)
                {
                    warn(string.Format(Loc.S("CliFragSkippedNoRecipe"), key));
                    continue;
                }
                var p = new JsonObject
                {
                    ["members"] = new JsonArray(tags.Select(t => (JsonNode)t).ToArray()),
                    ["offsetM"] = R(prism.OffsetM ?? ElementFragmentService.DefaultOffsetM),
                };
                if (prism.FullHeight) p["fullHeight"] = true;
                else
                {
                    if (prism.FloorFromZ is double zi) p["floorFrom"] = R(zi);
                    if (prism.FloorToZ is double zk) p["floorTo"] = R(zk);
                }
                o["prism"] = p;
            }
            o["keepParentMesh"] = schema.FragmentKeepParentMesh;
            if (CutBcJson(db, schema) is { } bc) o["cutBc"] = bc;
            items.Add(o);
        }
        return items;
    }

    /// <summary>ГУ разрезов: пресеты не по умолчанию и правки поверх пресета; ничего — null.</summary>
    static JsonObject? CutBcJson(DatabaseService db, FemSchema schema)
    {
        var o = new JsonObject();
        if (schema.FragmentBcPreset != null) o["preset"] = PresetJson(FragmentCutBc.ParsePreset(schema.FragmentBcPreset));
        if (schema.FragmentLateralBcPreset != null)
            o["lateralPreset"] = PresetJson(FragmentCutBc.ParseLateralPreset(schema.FragmentLateralBcPreset));
        var stored = db.GetFragmentCutBc(schema.Id);
        if (stored.Count > 0)
        {
            var cuts = db.GetFragmentCutsWithIds(schema.Id);
            var list = new JsonArray();
            foreach (var (id, cut) in cuts)
            {
                if (!stored.TryGetValue(id, out var bytes)) continue;
                var modes = new JsonObject();
                for (int d = 0; d < 6 && d < bytes.Length; d++)
                    if (bytes[d] != FragmentCutBc.ByPreset)
                        modes[Dofs[d]] = Modes.First(m => m.Value == FragmentCutBc.Decode(bytes[d])).Json;
                if (modes.Count == 0) continue;
                var item = new JsonObject { ["member"] = cut.MemberTag, ["side"] = Sides.First(s => s.Value == cut.Side).Json };
                if (cut.NodeTag != null) item["node"] = cut.NodeTag;
                if (cut.Side == FragmentCutSide.Lateral) item["face"] = Face(cut);
                item["modes"] = modes;
                list.Add(item);
            }
            if (list.Count > 0) o["overrides"] = list;
        }
        return o.Count == 0 ? null : o;
    }

    static string PresetJson(FragmentBcPreset p) => Presets.First(x => x.Value == p).Json;

    /// <summary>Грань бокового разреза для документа: «+x», «-y» или азимут нормали «30».</summary>
    static string Face(FragmentCut cut) => NormalizeFace(FragmentCutLabel.FaceCode(cut.Outward, out _));

    static string NormalizeFace(string code) =>
        code.Trim().Replace('−', '-').Replace("∠", "").Replace("°", "").ToLowerInvariant();

    // ── Разбор и план ───────────────────────────────────────────────────────────────────────

    /// <summary>Разобранный элемент документа (рецепт и ГУ разрезов).</summary>
    sealed class Doc
    {
        public required string Tag, Parent, Path;
        public bool IsPrism;
        public double? Z1, Z2, Floor;
        public List<string> Members = [];
        public double OffsetM = ElementFragmentService.DefaultOffsetM;
        public double? FloorFrom, FloorTo;
        public bool FullHeight, KeepParentMesh = true;
        public bool HasCutBc, HasOverrides;
        public FragmentBcPreset? Preset, LateralPreset;
        public List<OverrideDoc> Overrides = [];
    }

    sealed record OverrideDoc(string Member, FragmentCutSide Side, string? Node, string? Face, FragmentDofMode?[] Modes, string Path);

    public IReadOnlyList<ModelChange> Plan(JsonArray items, JsonObject doc, HeadlessProjectContext ctx, ModelErrors errors)
    {
        var db = ctx.Db;
        var planned = new Dictionary<string, Planned>(StringComparer.CurrentCultureIgnoreCase);
        PlannedByContext.AddOrUpdate(ctx, planned);
        var changes = new List<ModelChange>();
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var docSchemaTags = (doc["femSchemas"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(o => o["tag"] is JsonValue v && v.TryGetValue(out string? t) ? t.Trim() : null).OfType<string>()
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        for (int i = 0; i < items.Count; i++)
        {
            string path = $"femFragments[{i}]";
            if (items[i] is not JsonObject item)
            {
                errors.Add(path, Loc.S("CliModelErrNotObject"));
                continue;
            }
            int before = errors.Count;
            var d = Parse(item, path, errors);
            if (d == null || errors.Count > before) continue;
            if (!seen.Add(d.Tag))
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), d.Tag));
                continue;
            }

            // Родитель — схема проекта; создаваемый тем же документом (или фрагмент того же документа) сетки ещё не
            // имеет: фрагмент пропускается с предупреждением, повторный apply после run mesh его соберёт.
            var parents = db.FemSchemas.Where(s => string.Equals(s.Tag.Trim(), d.Parent, StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (parents.Count > 1)
            {
                errors.Add($"{path}.parent", string.Format(Loc.S("CliModelErrAmbiguousKey"), parents.Count, d.Parent));
                continue;
            }
            var existing = db.FemSchemas.Where(s => string.Equals(s.Tag.Trim(), d.Tag, StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (existing.Count > 1)
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrAmbiguousKey"), existing.Count, d.Tag));
                continue;
            }
            var parent = parents.SingleOrDefault();
            if (parent == null)
            {
                bool inDoc = docSchemaTags.Contains(d.Parent) || items.OfType<JsonObject>().Any(o =>
                    o["tag"] is JsonValue v && v.TryGetValue(out string? t) && string.Equals(t.Trim(), d.Parent, StringComparison.CurrentCultureIgnoreCase));
                if (!inDoc)
                {
                    errors.Add($"{path}.parent", string.Format(Loc.S("CliFragErrUnknownParent"), d.Parent));
                    continue;
                }
                if (existing.Count == 0)
                {
                    ctx.LogService.Warning(string.Format(Loc.S("CliFragSkippedParentInDoc"), d.Tag, d.Parent));
                    planned[d.Tag] = new Planned(d.Tag, null, Skipped: true);
                    continue;
                }
                errors.Add($"{path}.parent", string.Format(Loc.S("CliFragErrUnknownParent"), d.Parent));
                continue;
            }
            if (string.Equals(parent.Tag.Trim(), d.Tag, StringComparison.CurrentCultureIgnoreCase))
            {
                errors.Add($"{path}.parent", Loc.S("CliFragErrParentSelf"));
                continue;
            }
            if (docSchemaTags.Contains(d.Parent))
                ctx.LogService.Warning(string.Format(Loc.S("CliFragParentChangedByDoc"), d.Tag, d.Parent));

            if (existing.SingleOrDefault() is { } target)
            {
                if (target.ParentSchemaId != parent.Id)
                {
                    errors.Add($"{path}.tag", string.Format(Loc.S(target.ParentSchemaId == null ? "CliFragErrNotFragment" : "CliFragErrOtherParent"),
                        d.Tag, d.Parent));
                    continue;
                }
                if (RecipeDiff(db, target, parent, d) is { } diff)
                {
                    errors.Add(path, string.Format(Loc.S("CliFragErrRecipeChanged"), d.Tag, diff));
                    continue;
                }
                var cuts = db.GetFragmentCutsWithIds(target.Id);
                var resolved = ResolveOverrides(d, cuts.Select(c => c.Cut).ToList(), errors);
                if (errors.Count > before) continue;
                if (!d.HasCutBc) continue;
                var dc = d;
                changes.Add(new ModelChange(Name, d.Tag, false, c => ApplyCutBc(c.Db, c.Db.FemSchemas.First(s => s.Id == target.Id), dc, resolved)));
                continue;
            }

            if (parent.SourceType == "internal" && MeshProblem(ctx, parent) is { } meshProblem)
            {
                errors.Add($"{path}.parent", meshProblem);
                continue;
            }
            var request = BuildRequest(db, parent, d, path, errors);
            if (request == null || errors.Count > before) continue;
            StoreyFragmentPrepared prepared;
            try { prepared = StoreyFragmentService.Prepare(db, parent.Id, request); }
            catch (Exception e) when (e is InvalidOperationException or InvalidDataException)
            {
                errors.Add(path, e.Message);
                continue;
            }
            if (prepared.Draft.HasErrors)
            {
                var first = prepared.Draft.Diagnostics.Where(x => x.IsError).Take(3).Select(x => x.Message);
                errors.Add(path, string.Format(Loc.S("CliFragErrRefused"), string.Join("; ", first)));
                continue;
            }
            var newResolved = ResolveOverrides(d, prepared.Draft.Cuts, errors);
            if (errors.Count > before) continue;
            planned[d.Tag] = new Planned(d.Tag, prepared.Draft, Skipped: false);
            var dn = d;
            changes.Add(new ModelChange(Name, d.Tag, true, c => CommitNew(c, parent.Id, request, prepared, dn, newResolved)));
        }
        return changes;
    }

    static Doc? Parse(JsonObject item, string path, ModelErrors errors)
    {
        foreach (var (key, _) in item)
            if (!TopKeys.Contains(key)) errors.Add($"{path}.{key}", Loc.S("CliModelErrUnknownField"));
        string? tag = SectionJson.Str(item, "tag", path, errors, required: true)?.Trim();
        string? parent = SectionJson.Str(item, "parent", path, errors, required: true)?.Trim();
        if (tag is { Length: 0 }) errors.Add($"{path}.tag", Loc.S("StoreyFragmentNameEmpty"));
        if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(parent)) return null;
        var d = new Doc { Tag = tag, Parent = parent, Path = path };
        bool hasStorey = item["storey"] is not null, hasPrism = item["prism"] is not null;
        if (hasStorey == hasPrism)
        {
            errors.Add(path, Loc.S("CliFragErrStoreyOrPrism"));
            return null;
        }
        if (hasStorey)
        {
            string p = $"{path}.storey";
            if (item["storey"] is not JsonObject s) { errors.Add(p, Loc.S("CliModelErrNotObject")); return null; }
            foreach (var (key, _) in s)
                if (!StoreyKeys.Contains(key)) errors.Add($"{p}.{key}", Loc.S("CliModelErrUnknownField"));
            d.Z1 = SectionJson.Num(s, "z1", p, errors);
            d.Z2 = SectionJson.Num(s, "z2", p, errors);
            d.Floor = SectionJson.Num(s, "floor", p, errors);
            if (d.Floor != null && (d.Z1 != null || d.Z2 != null))
                errors.Add(p, Loc.S("CliFragErrFloorOrLevels"));
            else if (d.Floor == null && (d.Z1 == null || d.Z2 == null))
                errors.Add(p, Loc.S("CliFragErrFloorOrLevels"));
            else if (d.Z1 is double z1 && d.Z2 is double z2 && z1 >= z2)
                errors.Add($"{p}.z2", Loc.S("StoreyFragmentLevelsOrder"));
        }
        else
        {
            d.IsPrism = true;
            string p = $"{path}.prism";
            if (item["prism"] is not JsonObject s) { errors.Add(p, Loc.S("CliModelErrNotObject")); return null; }
            foreach (var (key, _) in s)
                if (!PrismKeys.Contains(key)) errors.Add($"{p}.{key}", Loc.S("CliModelErrUnknownField"));
            if (s["members"] is not JsonArray ma || ma.Count == 0)
                errors.Add($"{p}.members", Loc.S("CliFragErrMembers"));
            else
                for (int k = 0; k < ma.Count; k++)
                {
                    if (ma[k] is JsonValue v && v.TryGetValue(out string? t) && t.Trim().Length > 0) d.Members.Add(t.Trim());
                    else errors.Add($"{p}.members[{k}]", Loc.S("CliModelErrBadValue"));
                }
            if (SectionJson.Num(s, "offsetM", p, errors) is double off)
            {
                if (off > 0) d.OffsetM = off;
                else errors.Add($"{p}.offsetM", Loc.S("ElementFragmentOffsetInvalid"));
            }
            d.FloorFrom = SectionJson.Num(s, "floorFrom", p, errors);
            d.FloorTo = SectionJson.Num(s, "floorTo", p, errors);
            d.FullHeight = SectionJson.Bool(s, "fullHeight", p, errors) ?? false;
            if (d.FullHeight && (d.FloorFrom != null || d.FloorTo != null))
                errors.Add(p, Loc.S("CliFragErrFullHeightAndFloors"));
            else if ((d.FloorFrom == null) != (d.FloorTo == null))
                errors.Add(p, Loc.S("CliFragErrFloorPair"));
            else if (d.FloorFrom is double a && d.FloorTo is double b && a > b)
                errors.Add($"{p}.floorTo", Loc.S("CliFragErrFloorOrder"));
        }
        d.KeepParentMesh = SectionJson.Bool(item, "keepParentMesh", path, errors) ?? true;
        if (item["cutBc"] is not null) ParseCutBc(item["cutBc"], d, $"{path}.cutBc", errors);
        return d;
    }

    static void ParseCutBc(JsonNode? node, Doc d, string path, ModelErrors errors)
    {
        if (node is not JsonObject o) { errors.Add(path, Loc.S("CliModelErrNotObject")); return; }
        d.HasCutBc = true;
        foreach (var (key, _) in o)
            if (!CutBcKeys.Contains(key)) errors.Add($"{path}.{key}", Loc.S("CliModelErrUnknownField"));
        if (o["preset"] is not null) d.Preset = Word(o, "preset", Presets, path, errors);
        if (o["lateralPreset"] is not null) d.LateralPreset = Word(o, "lateralPreset", Presets, path, errors);
        if (o["overrides"] is null) return;
        if (o["overrides"] is not JsonArray arr) { errors.Add($"{path}.overrides", Loc.S("CliModelErrNotArray")); return; }
        d.HasOverrides = true;
        for (int i = 0; i < arr.Count; i++)
        {
            string p = $"{path}.overrides[{i}]";
            if (arr[i] is not JsonObject x) { errors.Add(p, Loc.S("CliModelErrNotObject")); continue; }
            foreach (var (key, _) in x)
                if (!OverrideKeys.Contains(key)) errors.Add($"{p}.{key}", Loc.S("CliModelErrUnknownField"));
            string? member = SectionJson.Str(x, "member", p, errors, required: true)?.Trim();
            FragmentCutSide? side = x["side"] is null ? null : Word(x, "side", Sides, p, errors);
            if (x["side"] is null) errors.Add($"{p}.side", Loc.S("CliModelErrRequired"));
            string? nodeTag = SectionJson.Str(x, "node", p, errors)?.Trim();
            string? face = SectionJson.Str(x, "face", p, errors) is { } f ? NormalizeFace(f) : null;
            var modes = new FragmentDofMode?[6];
            if (x["modes"] is not JsonObject mo || mo.Count == 0)
                errors.Add($"{p}.modes", Loc.S("CliFragErrModes"));
            else
                foreach (var (key, _) in mo)
                {
                    int dof = Array.IndexOf(Dofs, key);
                    if (dof < 0) { errors.Add($"{p}.modes.{key}", Loc.S("CliModelErrUnknownField")); continue; }
                    modes[dof] = Word(mo, key, Modes, $"{p}.modes", errors);
                }
            if (member != null && side is { } sd) d.Overrides.Add(new(member, sd, nodeTag, face, modes, p));
        }
    }

    /// <summary>Слово из списка; иначе ошибка «одно из …» и null.</summary>
    static T? Word<T>(JsonObject o, string key, (string Json, T Value)[] options, string path, ModelErrors errors) where T : struct
    {
        if (o[key] is JsonValue v && v.TryGetValue(out string? s))
            foreach (var (json, value) in options)
                if (json == s) return value;
        errors.Add($"{path}.{key}", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", options.Select(x => x.Json))));
        return null;
    }

    /// <summary>Правки документа → Id-независимые индексы разрезов (порядок разрезов схемы); ошибки — с путём.</summary>
    static List<(int CutIndex, byte[] Modes)> ResolveOverrides(Doc d, IReadOnlyList<FragmentCut> cuts, ModelErrors errors)
    {
        var result = new List<(int, byte[])>();
        var used = new HashSet<int>();
        foreach (var o in d.Overrides)
        {
            var found = cuts.Select((c, i) => (c, i))
                .Where(x => x.c.MemberTag == o.Member && x.c.Side == o.Side
                    && (o.Node == null || x.c.NodeTag == o.Node)
                    && (o.Face == null || x.c.Side == FragmentCutSide.Lateral && Face(x.c) == o.Face))
                .ToList();
            if (found.Count == 0) { errors.Add(o.Path, string.Format(Loc.S("CliFragErrCutNotFound"), o.Member)); continue; }
            if (found.Count > 1) { errors.Add(o.Path, string.Format(Loc.S("CliFragErrCutAmbiguous"), o.Member, found.Count)); continue; }
            if (!used.Add(found[0].i)) { errors.Add(o.Path, string.Format(Loc.S("CliFragErrCutRepeated"), o.Member)); continue; }
            result.Add((found[0].i, FragmentCutBc.Encode(o.Modes)));
        }
        return result;
    }

    /// <summary>
    /// Сетка родной схемы-родителя не построена — текст ошибки с подсказкой; null — есть. Свежесть сетки не сверяется — как в
    /// окне: фрагмент снимается с сохранённой сетки (сверка требует Gmsh и при каждом apply пересчитывала бы разбиение).
    /// </summary>
    static string? MeshProblem(HeadlessProjectContext ctx, FemSchema parent) =>
        ctx.Db.GetFemMeshElements(parent.Id).Count == 0 ? string.Format(Loc.S("CliFragErrParentNoMesh"), parent.Tag.Trim()) : null;

    /// <summary>Запрос сборки по рецепту: полоса по отметкам или перекрытию, призма — той же службой, что окно.</summary>
    static StoreyFragmentRequest? BuildRequest(DatabaseService db, FemSchema parent, Doc d, string path, ModelErrors errors)
    {
        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        if (!d.IsPrism)
        {
            double z1, z2;
            if (d.Floor is double floor)
            {
                var context = StoreyFloorService.Read(db, parent.Id);
                if (StoreyFloorLevels.LevelAt(context.Levels, floor) is not int index)
                {
                    errors.Add($"{path}.storey.floor", string.Format(Loc.S("CliFragErrFloorNotFound"), F(floor),
                        LevelList(context.Levels.Select(l => l.Z))));
                    return null;
                }
                var band = StoreyFloorLevels.Band(context.Levels, index, context.ZMin, context.ZMax);
                (z1, z2) = (band.Z1, band.Z2);
            }
            else (z1, z2) = (d.Z1!.Value, d.Z2!.Value);
            return new StoreyFragmentRequest(z1, z2, d.Tag, KeepParentMesh: d.KeepParentMesh);
        }

        var members = db.GetFemMembers(parent.Id);
        var known = members.Select(m => m.ElemTag).ToHashSet(StringComparer.Ordinal);
        for (int k = 0; k < d.Members.Count; k++)
            if (!known.Contains(d.Members[k]))
                errors.Add($"{path}.prism.members[{k}]", string.Format(Loc.S("CliFragErrMemberNotFound"), d.Members[k], parent.Tag.Trim()));
        if (errors.Count > 0 && d.Members.Any(t => !known.Contains(t))) return null;
        var (vertical, rejected) = ElementFragmentService.SplitVertical(db.GetFemNodes(parent.Id), members, db.GetPlanarRegions(parent.Id), d.Members);
        if (rejected.Count > 0)
        {
            errors.Add($"{path}.prism.members", string.Format(Loc.S("CliFragErrNotVertical"), string.Join(", ", rejected)));
            return null;
        }
        var floors = StoreyFloorService.Read(db, parent.Id);
        var selection = ElementFragmentService.PrismFromSelection(db, parent.Id, vertical, d.OffsetM, floors);
        if (selection.HasErrors)
        {
            var msgs = selection.Diagnostics.Where(x => x.IsError).Take(3).Select(x => x.Message).ToList();
            errors.Add($"{path}.prism", string.Format(Loc.S("CliFragErrRefused"),
                msgs.Count > 0 ? string.Join("; ", msgs) : Loc.S("ElementFragmentRefused")));
            return null;
        }
        if (d.FullHeight) return ElementFragmentService.FullHeightRequest(selection, d.Tag) with { KeepParentMesh = d.KeepParentMesh };
        int? Find(double z) => selection.Height.Available.Cast<int?>()
            .FirstOrDefault(i => Math.Abs(selection.Levels[i!.Value].Z - z) <= StoreyFloorLevels.LevelToleranceM);
        int? from = d.FloorFrom is double a ? Find(a) : selection.Height.DefaultFrom;
        int? to = d.FloorTo is double b ? Find(b) : selection.Height.DefaultTo;
        string available = LevelList(selection.Height.Available.Select(i => selection.Levels[i].Z));
        if (from is null)
        {
            errors.Add($"{path}.prism.floorFrom", string.Format(Loc.S("CliFragErrFloorNotFound"), d.FloorFrom is double x ? F(x) : "—", available));
            return null;
        }
        if (to is null)
        {
            errors.Add($"{path}.prism.floorTo", string.Format(Loc.S("CliFragErrFloorNotFound"), d.FloorTo is double y ? F(y) : "—", available));
            return null;
        }
        return ElementFragmentService.Request(selection, from.Value, to.Value, d.Tag) with { KeepParentMesh = d.KeepParentMesh };
    }

    static string LevelList(IEnumerable<double> z)
    {
        var list = z.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)).ToList();
        return list.Count == 0 ? "—" : string.Join(", ", list);
    }

    /// <summary>Отличие рецепта документа от собранного фрагмента (текст для ошибки); null — тот же рецепт.</summary>
    static string? RecipeDiff(DatabaseService db, FemSchema target, FemSchema parent, Doc d)
    {
        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        if (target.FragmentKeepParentMesh != d.KeepParentMesh) return "keepParentMesh";
        FragmentPrismInfo? info;
        try { info = FragmentPrismCodec.Decode(target.FragmentPrismJson); }
        catch (InvalidDataException) { return "prism"; }
        if (!d.IsPrism)
        {
            if (info != null) return "prism → storey";
            double z1, z2;
            if (d.Floor is double floor)
            {
                var context = StoreyFloorService.Read(db, parent.Id);
                if (StoreyFloorLevels.LevelAt(context.Levels, floor) is not int index) return $"storey.floor {F(floor)}";
                var band = StoreyFloorLevels.Band(context.Levels, index, context.ZMin, context.ZMax);
                (z1, z2) = (band.Z1, band.Z2);
            }
            else (z1, z2) = (d.Z1!.Value, d.Z2!.Value);
            if (target.FragmentZ1 is not double t1 || target.FragmentZ2 is not double t2
                || Math.Abs(t1 - z1) > LevelTol || Math.Abs(t2 - z2) > LevelTol)
                return $"storey: {F(target.FragmentZ1 ?? double.NaN)}…{F(target.FragmentZ2 ?? double.NaN)} → {F(z1)}…{F(z2)}";
            return null;
        }
        if (info == null) return "storey → prism";
        if (info.MemberTags is not { } tags || !tags.OrderBy(t => t, StringComparer.Ordinal).SequenceEqual(d.Members.OrderBy(t => t, StringComparer.Ordinal)))
            return "prism.members";
        if (Math.Abs((info.OffsetM ?? ElementFragmentService.DefaultOffsetM) - d.OffsetM) > 1e-9) return "prism.offsetM";
        if (info.FullHeight != d.FullHeight) return "prism.fullHeight";
        if (d.FloorFrom is double a && (info.FloorFromZ is not double fa || Math.Abs(fa - a) > LevelTol)) return "prism.floorFrom";
        if (d.FloorTo is double b && (info.FloorToZ is not double fb || Math.Abs(fb - b) > LevelTol)) return "prism.floorTo";
        return null;
    }

    // ── Запись ──────────────────────────────────────────────────────────────────────────────

    static void CommitNew(HeadlessProjectContext ctx, int parentId, StoreyFragmentRequest request, StoreyFragmentPrepared prepared,
        Doc d, List<(int CutIndex, byte[] Modes)> overrides)
    {
        var db = ctx.Db;
        var parent = db.FemSchemas.First(s => s.Id == parentId);
        var result = FragmentAuthoring.Commit(db, parent, request, prepared);
        if (result.Schema is not { } schema)
            throw new CliException(CliExitCode.Usage, $"{d.Path}: " + string.Join("; ", result.Rows.Where(r => r.IsError).Select(r => r.Message)));
        ctx.LogService.Info(result.LogMessage);
        ApplyCutBc(db, schema, d, overrides);
    }

    /// <summary>Пресеты (только указанные) и правки (указанный список заменяет все) по индексам разрезов схемы.</summary>
    static void ApplyCutBc(DatabaseService db, FemSchema schema, Doc d, List<(int CutIndex, byte[] Modes)> overrides)
    {
        if (!d.HasCutBc) return;
        if (d.Preset is { } p)
        {
            string? stored = p == FragmentBcPreset.Kinematic ? null : p.ToString();
            if (stored != schema.FragmentBcPreset) db.SetFragmentBcPreset(schema, stored);
        }
        if (d.LateralPreset is { } lp)
        {
            string? stored = lp == FragmentBcPreset.Static ? null : lp.ToString();
            if (stored != schema.FragmentLateralBcPreset) db.SetFragmentLateralBcPreset(schema, stored);
        }
        if (!d.HasOverrides) return;
        var cuts = db.GetFragmentCutsWithIds(schema.Id);
        var modes = overrides.ToDictionary(o => cuts[o.CutIndex].Id, o => o.Modes);
        var current = db.GetFragmentCutBc(schema.Id);
        bool same = current.Count == modes.Count && modes.All(m => current.TryGetValue(m.Key, out var b) && b.SequenceEqual(m.Value));
        if (!same) db.ReplaceFragmentCutBc(schema.Id, modes);
    }
}
