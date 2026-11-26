using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CScore;
using CScore.Fem;
using CScore.Sp16;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Раздел «femChecks»: нормативные проверки по КЭ 1-й и 2-й ГПС — сталь (<c>steel</c>), ЖБ стержни (<c>rcBar</c>), ЖБ
/// пластины по прочности (<c>rcPlate</c>) и по трещинам (<c>rcPlateSls</c>). Ключ — имя проверки (<c>tag</c>) в пределах
/// схемы (<c>schema</c>). Найдено — обновить, нет — создать; удаления нет. Ссылки — по тегам: цель
/// (<c>{"group": имя}</c> или <c>{"member": имя}</c>), наборы усилий, набор NL, постановка расчётных длин стали.
/// Неуказанные поля существующей проверки не меняются, у новой — умолчания окна. Неизменённая проверка не
/// перезаписывается; смена цели, вида, наборов или параметров удаляет её результат (как правка в окне), имя — нет.
/// Проверка — та же служба, что у окон (<see cref="FemCheckAuthoring"/>). Продавливание — отдельным видом (7в).
/// </summary>
sealed class FemChecksKind : IModelKind
{
    public string Name => "femChecks";
    public string SchemaResource => "OpenCS.Cli.Model.Schemas.femChecks.schema.json";

    /// <summary>Вид документа ↔ код проверки базы.</summary>
    static readonly (string Doc, string Db)[] KindMap =
    [
        ("steel", FemCheckAuthoring.Steel), ("rcBar", FemCheckAuthoring.RcBar),
        ("rcPlate", FemCheckAuthoring.RcPlate), ("rcPlateSls", FemCheckAuthoring.RcPlate),
    ];

    static readonly string[] Common = ["schema", "tag", "kind", "target", "forceSets", "calcType"];
    static readonly Dictionary<string, string[]> TopKeys = new()
    {
        ["steel"] = [.. Common, "steel"],
        ["rcBar"] = [.. Common, "rebar", "eta"],
        ["rcPlate"] = [.. Common, "plate", "rebar", "eta"],
        ["rcPlateSls"] = [.. Common, "plate", "rebar"],
    };
    static readonly string[] SteelKeys = ["mesh", "auto"];
    static readonly string[] MeshKeys = ["muX", "muY", "muB"];
    static readonly string[] AutoKeys = ["analysis"];
    static readonly string[] EtaKeys = ["iterative", "lengthM", "muX", "muY", "psi", "psiX", "psiY", "slendernessThreshold"];
    static readonly string[] PlateEtaKeys = ["iterative", "lengthM", "muX", "psi", "psiX", "slendernessThreshold"];
    static readonly string[] PlateUlsKeys = ["method", "strip", "capriMode"];
    static readonly string[] PlateSlsKeys = ["method", "strip", "capriMode", "acrcLimMm", "phi1", "phi2", "nlForceSet", "ltFraction"];
    static readonly string[] CalcTypes = ["C", "CL", "N", "NL"];

    /// <summary>Метод документа ↔ вид пластин базы (1-я и 2-я ГПС).</summary>
    static readonly (string Doc, string Uls, string Sls)[] Methods =
    [
        ("woodArmer", "shell_simpl_wa_uls", "shell_simpl_wa_sls"),
        ("capri", "shell_simpl_capri_uls", "shell_simpl_capri_sls"),
        ("layered", "shell_layered", "shell_layered"),
    ];
    static readonly (string Json, StripModel Value)[] Strips = [("ndm", StripModel.Ndm), ("formulas", StripModel.Formulas)];
    static readonly (string Json, CapriCheckMode Value)[] CapriModes =
        [("direct", CapriCheckMode.Direct), ("constraints", CapriCheckMode.Constraints)];

    static double R(double v) => Math.Round(v, 9) + 0.0;

    /// <summary>Ссылки схемы по тегам: группы, конструктивные элементы, наборы усилий, постановки — из базы или документа.</summary>
    sealed class Refs
    {
        public required FemSchema Schema { get; init; }
        public required List<FemMember> Members { get; init; }
        public required List<ForceSet> ForceSets { get; init; }
        public required List<FemAnalysis> Analyses { get; init; }
        public required List<FemCheck> Checks { get; init; }

        public FemCheckRefs Service => new(Schema, Checks, Members, ForceSets, Analyses);
        public List<FemMemberGroup> Groups(string tag) =>
            Schema.MemberGroups.Where(g => string.Equals(g.Tag.Trim(), tag, StringComparison.CurrentCultureIgnoreCase)).ToList();
        public List<FemMember> MembersByTag(string tag) =>
            Members.Where(m => string.Equals(m.ElemTag.Trim(), tag, StringComparison.CurrentCultureIgnoreCase)).ToList();
        public List<ForceSet> Sets(string tag) =>
            ForceSets.Where(f => string.Equals(f.Tag.Trim(), tag, StringComparison.CurrentCultureIgnoreCase)).ToList();
        public List<FemAnalysis> AnalysesByTag(string tag) =>
            Analyses.Where(a => string.Equals(a.Tag.Trim(), tag, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    static Refs DbRefs(DatabaseService db, FemSchema schema) => new()
    {
        Schema = schema, Members = db.GetFemMembers(schema.Id), ForceSets = [.. db.ForceSets],
        Analyses = schema.Analyses.Count > 0 ? [.. schema.Analyses] : db.GetFemAnalyses(schema.Id), Checks = [.. db.FemChecks],
    };

    // ── Выгрузка ────────────────────────────────────────────────────────────────────────────

    public JsonArray Export(HeadlessProjectContext ctx, Action<string> warn)
    {
        var db = ctx.Db;
        var items = new JsonArray();
        var dupSchemas = db.FemSchemas.GroupBy(s => s.Tag.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        foreach (var schema in db.FemSchemas.OrderBy(s => s.Id))
        {
            var checks = db.FemChecks.Where(c => c.SchemaId == schema.Id && c.NormCode != PunchingFemCheckParams.NormCode)
                .OrderBy(c => c.Id).ToList();
            if (checks.Count == 0) continue;
            if (dupSchemas.Contains(schema.Tag.Trim()))
            {
                warn(string.Format(Loc.S("CliFemCheckSchemaAmbiguous"), schema.Tag.Trim()));
                continue;
            }
            var refs = DbRefs(db, schema);
            var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var c in checks)
            {
                if (KindMap.All(k => k.Db != c.NormCode))
                {
                    warn(string.Format(Loc.S("CliFemCheckSkippedKind"), schema.Tag.Trim(), c.DisplayTag, c.NormCode));
                    continue;
                }
                if (!seen.Add(c.Tag.Trim()))
                {
                    warn(string.Format(Loc.S("CliFemCheckDuplicateSkipped"), schema.Tag.Trim(), c.Tag.Trim()));
                    continue;
                }
                if (ToJson(c, refs, warn) is { } o) items.Add(o);
            }
        }
        return items;
    }

    JsonObject? ToJson(FemCheck c, Refs refs, Action<string> warn)
    {
        string schemaTag = refs.Schema.Tag.Trim();
        JsonObject target;
        if (c.TargetsElement)
        {
            var m = refs.Members.FirstOrDefault(x => x.Id == c.ElementId);
            if (m == null || refs.MembersByTag(m.ElemTag.Trim()).Count != 1)
            {
                warn(string.Format(Loc.S("CliFemCheckTargetSkipped"), schemaTag, c.Tag.Trim()));
                return null;
            }
            target = new JsonObject { ["member"] = m.ElemTag.Trim() };
        }
        else
        {
            var g = refs.Schema.MemberGroups.FirstOrDefault(x => x.Id == c.MemberId);
            if (g == null || refs.Groups(g.Tag.Trim()).Count != 1)
            {
                warn(string.Format(Loc.S("CliFemCheckTargetSkipped"), schemaTag, c.Tag.Trim()));
                return null;
            }
            target = new JsonObject { ["group"] = g.Tag.Trim() };
        }

        bool sls = FemCheckAuthoring.IsSls(c);
        string kind = c.NormCode switch
        {
            FemCheckAuthoring.Steel => "steel",
            FemCheckAuthoring.RcBar => "rcBar",
            _ => sls ? "rcPlateSls" : "rcPlate",
        };
        var o = new JsonObject { ["schema"] = schemaTag, ["tag"] = c.Tag.Trim(), ["kind"] = kind, ["target"] = target };
        if (c.IsAllSets) o["forceSets"] = "all";
        else
        {
            var sets = new JsonArray();
            foreach (int id in c.GetForceSetIds())
                if (refs.ForceSets.FirstOrDefault(f => f.Id == id) is { } fs) sets.Add(fs.Tag.Trim());
                else warn(string.Format(Loc.S("CliFemCheckSetMissing"), schemaTag, c.Tag.Trim(), id));
            o["forceSets"] = sets.Count > 0 ? sets : "all";
        }
        o["calcType"] = c.CalcTypeOverride;

        switch (kind)
        {
            case "steel":
            {
                var p = SteelFemCheckParams.TryParse(c.ParamsJson);
                var so = new JsonObject
                {
                    ["mesh"] = p?.MeshLef is { } m
                        ? new JsonObject { ["muX"] = R(m.MuX), ["muY"] = R(m.MuY), ["muB"] = R(m.MuB) } : null,
                };
                if (p?.AutoLef is { } a)
                {
                    if (refs.Analyses.FirstOrDefault(x => x.Id == a.AnalysisId) is { } an)
                        so["auto"] = new JsonObject { ["analysis"] = an.Tag.Trim() };
                    else
                    {
                        warn(string.Format(Loc.S("CliFemCheckAnalysisMissing"), schemaTag, c.Tag.Trim()));
                        so["auto"] = null;
                    }
                }
                else so["auto"] = null;
                o["steel"] = so;
                break;
            }
            case "rcBar":
            {
                var p = BarCheckParams.Parse(c.ParamsJson);
                o["rebar"] = Sources(p.RebarSources);
                o["eta"] = EtaJson(p.Eta, plate: false);
                break;
            }
            default:
            {
                var p = PlateCheckParams.Parse(c.ParamsJson);
                var method = Methods.FirstOrDefault(x => (sls ? x.Sls : x.Uls) == p.Kind);
                var po = new JsonObject { ["method"] = method.Doc ?? p.Kind };
                if (p.Kind.StartsWith("shell_simpl", StringComparison.Ordinal))
                    po["strip"] = Strips.First(x => x.Value == p.StripModel).Json;
                if (p.Kind.StartsWith("shell_simpl_capri", StringComparison.Ordinal))
                    po["capriMode"] = CapriModes.First(x => x.Value == p.CapriCheckMode).Json;
                if (sls)
                {
                    po["acrcLimMm"] = R(p.AcrcLimMm);
                    if (p.Kind == "shell_layered")
                    {
                        po["nlForceSet"] = p.NlForceSetId > 0
                            ? refs.ForceSets.FirstOrDefault(f => f.Id == p.NlForceSetId)?.Tag.Trim() : null;
                        po["ltFraction"] = R(p.LtFraction);
                    }
                    else po["phi1"] = p.Phi1Mode == "manual" ? JsonValue.Create(R(p.Phi1)) : JsonValue.Create("auto");
                    po["phi2"] = R(p.Phi2);
                }
                o["plate"] = po;
                o["rebar"] = Sources(p.GetRebarSources());
                if (!sls) o["eta"] = EtaJson(p.Eta, plate: true);
                break;
            }
        }
        return o;
    }

    static JsonArray Sources(IReadOnlyList<string> keys) =>
        new((keys.Count > 0 ? keys : [FemCheckRebarSource.Section]).Select(k => (JsonNode)JsonValue.Create(k)!).ToArray());

    static JsonObject? EtaJson(FemEtaParams? eta, bool plate)
    {
        if (eta is not { Enabled: true } e) return null;
        var o = new JsonObject
        {
            ["iterative"] = e.Iterative,
            ["lengthM"] = e.HasManualLength ? R(e.LengthM!.Value) : null,
            ["muX"] = R(e.MuX),
        };
        if (!plate) o["muY"] = R(e.MuY);
        o["psi"] = e.IsPsiAuto ? FemEtaParams.PsiAuto : FemEtaParams.PsiManual;
        o["psiX"] = R(e.PsiX);
        if (!plate) o["psiY"] = R(e.PsiY);
        o["slendernessThreshold"] = e.SlendernessThreshold is { } s ? R(s) : null;
        return o;
    }

    // ── План ────────────────────────────────────────────────────────────────────────────────

    public IReadOnlyList<ModelChange> Plan(JsonArray items, JsonObject doc, HeadlessProjectContext ctx, ModelErrors errors)
    {
        var db = ctx.Db;
        var changes = new List<ModelChange>();
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var virtualRefs = new Dictionary<string, Refs>(StringComparer.CurrentCultureIgnoreCase);
        for (int i = 0; i < items.Count; i++)
        {
            string path = $"femChecks[{i}]";
            if (items[i] is not JsonObject item)
            {
                errors.Add(path, Loc.S("CliModelErrNotObject"));
                continue;
            }
            string? schemaTag = SectionJson.Str(item, "schema", path, errors, required: true)?.Trim();
            string? tag = SectionJson.Str(item, "tag", path, errors, required: true)?.Trim();
            string? kind = SectionJson.Str(item, "kind", path, errors, required: true);
            if (schemaTag == null || tag == null || kind == null) continue;
            if (tag.Length == 0)
            {
                errors.Add($"{path}.tag", Loc.S("FemCheckTagEmpty"));
                continue;
            }
            if (!TopKeys.TryGetValue(kind, out var allowed))
            {
                errors.Add($"{path}.kind", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", TopKeys.Keys)));
                continue;
            }
            Unknown(item, allowed, path, errors);
            if (!seen.Add(schemaTag + "\u0001" + tag))
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), tag));
                continue;
            }

            var dbSchemas = db.FemSchemas.Where(s => string.Equals(s.Tag.Trim(), schemaTag, StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (dbSchemas.Count > 1)
            {
                errors.Add($"{path}.schema", string.Format(Loc.S("CliModelErrAmbiguousKey"), dbSchemas.Count, schemaTag));
                continue;
            }
            if (!virtualRefs.TryGetValue(schemaTag, out var refs))
            {
                var docSchema = DocSchema(doc, schemaTag);
                if (docSchema == null && dbSchemas.Count == 0)
                {
                    errors.Add($"{path}.schema", string.Format(Loc.S("CliFemAnalysisErrUnknownSchema"), schemaTag));
                    continue;
                }
                virtualRefs[schemaTag] = refs = PlanRefs(doc, docSchema, dbSchemas.SingleOrDefault(), schemaTag, db);
            }

            int before = errors.Count;
            var existing = Existing(refs, tag);
            var built = Build(item, kind, tag, refs, existing, path, errors);
            if (built == null || errors.Count > before) continue;
            foreach (var e in FemCheckAuthoring.Validate(refs.Service, built))
                errors.Add($"{path}.{e.Path}", e.Message);
            if (errors.Count > before) continue;

            var itemCopy = item;
            changes.Add(new ModelChange(Name, $"{schemaTag} / {tag}", existing == null, c => Commit(c, itemCopy, kind, schemaTag, tag, path)));
        }
        return changes;
    }

    /// <summary>Запись: ссылки — по сохранённой схеме и наборам (их мог только что записать этот же документ).</summary>
    void Commit(HeadlessProjectContext ctx, JsonObject item, string kind, string schemaTag, string tag, string path)
    {
        var db = ctx.Db;
        var schema = db.FemSchemas.Single(s => string.Equals(s.Tag.Trim(), schemaTag, StringComparison.CurrentCultureIgnoreCase));
        var refs = DbRefs(db, schema);
        var existing = Existing(refs, tag);
        var errors = new ModelErrors();
        var built = Build(item, kind, tag, refs, existing, path, errors);
        if (built != null)
            foreach (var e in FemCheckAuthoring.Validate(refs.Service, built))
                errors.Add($"{path}.{e.Path}", e.Message);
        errors.ThrowIfAny();
        var (_, reset) = FemCheckAuthoring.Save(db, existing, built!);
        if (reset) ctx.LogService.Warning(string.Format(Loc.S("FemCheckResultReset"), existing!.Tag));
    }

    static FemCheck? Existing(Refs refs, string tag) =>
        refs.Checks.FirstOrDefault(c => c.SchemaId == refs.Schema.Id && c.NormCode != PunchingFemCheckParams.NormCode
            && string.Equals(c.Tag.Trim(), tag, StringComparison.CurrentCultureIgnoreCase));

    static void Unknown(JsonObject o, string[] allowed, string path, ModelErrors errors)
    {
        foreach (var (key, _) in o)
            if (!allowed.Contains(key)) errors.Add($"{path}.{key}", Loc.S("CliModelErrUnknownField"));
    }

    static JsonObject? DocSchema(JsonObject doc, string tag) =>
        doc["femSchemas"] is JsonArray a
            ? a.OfType<JsonObject>().FirstOrDefault(o => o["tag"] is JsonValue v && v.TryGetValue(out string? t)
                && string.Equals(t?.Trim(), tag, StringComparison.CurrentCultureIgnoreCase))
            : null;

    static string? StrOf(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s.Trim() : null;

    /// <summary>
    /// Ссылки для плана: группы и конструктивные элементы — из описания схемы в документе там, где он их задаёт
    /// (условные Id), иначе из проекта; наборы усилий и постановки — проекта и документа.
    /// </summary>
    static Refs PlanRefs(JsonObject doc, JsonObject? docSchema, FemSchema? dbSchema, string schemaTag, DatabaseService db)
    {
        int fake = -1;
        var schema = new FemSchema { Id = dbSchema?.Id ?? -1, Tag = dbSchema?.Tag ?? schemaTag };
        List<FemMember> members = dbSchema != null ? db.GetFemMembers(dbSchema.Id) : [];
        if (docSchema?["members"] is JsonArray ma)
            members = ma.OfType<JsonObject>().Select(m => (Tag: StrOf(m["tag"]), Type: StrOf(m["type"])))
                .Where(m => m.Tag != null)
                .Select(m => members.FirstOrDefault(x => x.ElemTag.Trim() == m.Tag) is { } same
                    ? same : new FemMember { Id = fake--, SchemaId = schema.Id, ElemTag = m.Tag!, ElemType = m.Type == "shell" ? "shell" : "beam" })
                .ToList();
        if (docSchema?["groups"] is JsonArray ga)
        {
            if (dbSchema != null)
                foreach (var g in dbSchema.MemberGroups.Where(g => g.Kind != FemMemberGroup.KindMembers)) schema.MemberGroups.Add(g);
            foreach (var t in ga.OfType<JsonObject>().Select(g => StrOf(g["tag"])).OfType<string>())
                schema.MemberGroups.Add(dbSchema?.MemberGroups.FirstOrDefault(x => x.Kind == FemMemberGroup.KindMembers && x.Tag.Trim() == t)
                    ?? new FemMemberGroup { Id = fake--, SchemaId = schema.Id, Tag = t });
        }
        else if (dbSchema != null) foreach (var g in dbSchema.MemberGroups) schema.MemberGroups.Add(g);

        var sets = db.ForceSets.ToList();
        if (doc["forceSets"] is JsonArray fa)
            foreach (var f in fa.OfType<JsonObject>())
                if (StrOf(f["tag"]) is { } t && !sets.Any(x => x.Tag.Trim() == t))
                    sets.Add(new ForceSet { Id = fake--, Tag = t, Kind = StrOf(f["kind"]) ?? "bar" });

        var analyses = dbSchema != null ? (dbSchema.Analyses.Count > 0 ? [.. dbSchema.Analyses] : db.GetFemAnalyses(dbSchema.Id)) : new List<FemAnalysis>();
        if (doc["femAnalyses"] is JsonArray aa)
            foreach (var a in aa.OfType<JsonObject>())
                if (string.Equals(StrOf(a["schema"]), schemaTag, StringComparison.CurrentCultureIgnoreCase) && StrOf(a["tag"]) is { } t
                    && !analyses.Any(x => x.Tag.Trim() == t))
                    analyses.Add(new FemAnalysis { Id = fake--, SchemaId = schema.Id, Tag = t });

        return new Refs { Schema = schema, Members = members, ForceSets = sets, Analyses = analyses, Checks = [.. db.FemChecks] };
    }

    // ── Разбор в проверку ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Проверка по элементу документа поверх <paramref name="existing"/> (того же вида) или умолчаний окна. null — ошибки.
    /// </summary>
    static FemCheck? Build(JsonObject item, string kind, string tag, Refs refs, FemCheck? existing, string path, ModelErrors errors)
    {
        string normCode = KindMap.First(k => k.Doc == kind).Db;
        bool sls = kind == "rcPlateSls";
        bool sameKind = existing != null && existing.NormCode == normCode
            && (normCode != FemCheckAuthoring.RcPlate || FemCheckAuthoring.IsSls(existing) == sls);
        int before = errors.Count;
        var c = new FemCheck
        {
            Id = existing?.Id ?? 0, SchemaId = refs.Schema.Id, Tag = tag, NormCode = normCode, ResultId = existing?.ResultId,
            MemberId = existing?.MemberId ?? 0, ElementId = existing?.ElementId,
            ForceSetIdsJson = existing?.ForceSetIdsJson ?? "[]",
            CalcTypeOverride = sameKind ? existing!.CalcTypeOverride : null,
        };

        // Цель
        if (item["target"] is JsonObject to) ReadTarget(to, c, refs, $"{path}.target", errors);
        else if (item["target"] is not null) errors.Add($"{path}.target", Loc.S("CliModelErrNotObject"));
        else if (existing == null || existing.SchemaId != refs.Schema.Id) errors.Add($"{path}.target", Loc.S("CliModelErrRequired"));

        // Наборы
        if (item["forceSets"] is JsonNode fsn) ReadSets(fsn, c, refs, $"{path}.forceSets", errors);

        // Тип характеристик: null — по набору
        if (item.ContainsKey("calcType"))
        {
            var ct = item["calcType"];
            if (ct is null) c.CalcTypeOverride = null;
            else if (ct is JsonValue cv && cv.TryGetValue(out string? s) && CalcTypes.Contains(s)) c.CalcTypeOverride = s;
            else errors.Add($"{path}.calcType", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", CalcTypes)));
        }

        switch (kind)
        {
            case "steel":
            {
                var p = (sameKind ? SteelFemCheckParams.TryParse(existing!.ParamsJson) : null) ?? new SteelFemCheckParams();
                if (item["steel"] is JsonObject so) p = ReadSteel(so, p, refs, $"{path}.steel", errors);
                else if (item["steel"] is not null) errors.Add($"{path}.steel", Loc.S("CliModelErrNotObject"));
                c.ParamsJson = p.ToJson();
                break;
            }
            case "rcBar":
            {
                var p = sameKind ? BarCheckParams.Parse(existing!.ParamsJson) : new BarCheckParams { RebarSources = [FemCheckRebarSource.Section] };
                if (item["rebar"] is JsonNode rn) p = p with { RebarSources = ReadSources(rn, $"{path}.rebar", errors) };
                if (item.ContainsKey("eta")) p = p with { Eta = ReadEta(item["eta"], p.Eta, plate: false, $"{path}.eta", errors) };
                c.ParamsJson = p.ToJson();
                break;
            }
            default:
            {
                var p = sameKind ? PlateCheckParams.Parse(existing!.ParamsJson) : new PlateCheckParams
                {
                    Kind = sls ? FemCheckAuthoring.PlateSlsKinds[0] : FemCheckAuthoring.PlateUlsKinds[0],
                    StripModel = StripModel.Ndm, Phi1Mode = "auto", RebarSources = [FemCheckRebarSource.Section],
                };
                p = p with { CheckGroup = sls ? "sls" : "uls" };
                if (item["plate"] is JsonObject po) p = ReadPlate(po, p, sls, refs, $"{path}.plate", errors);
                else if (item["plate"] is not null) errors.Add($"{path}.plate", Loc.S("CliModelErrNotObject"));
                if (item["rebar"] is JsonNode rn) p = p with { RebarSources = ReadSources(rn, $"{path}.rebar", errors) };
                if (!sls && item.ContainsKey("eta")) p = p with { Eta = ReadEta(item["eta"], p.Eta, plate: true, $"{path}.eta", errors) };
                c.ParamsJson = p.ToJson();
                break;
            }
        }
        return errors.Count > before ? null : c;
    }

    static void ReadTarget(JsonObject o, FemCheck c, Refs refs, string path, ModelErrors errors)
    {
        Unknown(o, ["group", "member"], path, errors);
        string? group = SectionJson.Str(o, "group", path, errors), member = SectionJson.Str(o, "member", path, errors);
        if ((group == null) == (member == null))
        {
            errors.Add(path, Loc.S("CliFemCheckErrTarget"));
            return;
        }
        if (group != null)
        {
            var found = refs.Groups(group);
            if (found.Count == 1) { c.MemberId = found[0].Id; c.ElementId = null; }
            else errors.Add($"{path}.group", found.Count == 0
                ? string.Format(Loc.S("CliFemCheckErrUnknownGroup"), group)
                : string.Format(Loc.S("CliModelErrAmbiguousKey"), found.Count, group));
        }
        else
        {
            var found = refs.MembersByTag(member!);
            if (found.Count == 1) { c.MemberId = 0; c.ElementId = found[0].Id; }
            else errors.Add($"{path}.member", found.Count == 0
                ? string.Format(Loc.S("CliFemCheckErrUnknownMember"), member)
                : string.Format(Loc.S("CliModelErrAmbiguousKey"), found.Count, member));
        }
    }

    static void ReadSets(JsonNode n, FemCheck c, Refs refs, string path, ModelErrors errors)
    {
        if (n is JsonValue v && v.TryGetValue(out string? s) && s == "all")
        {
            c.ForceSetIdsJson = "[]";
            return;
        }
        if (n is not JsonArray a)
        {
            errors.Add(path, Loc.S("CliFemCheckErrSets"));
            return;
        }
        if (a.Count == 0)
        {
            errors.Add(path, Loc.S("CliFemCheckErrSetsEmpty"));
            return;
        }
        var ids = new List<int>();
        for (int i = 0; i < a.Count; i++)
        {
            if (StrOf(a[i]) is not { } t)
            {
                errors.Add($"{path}[{i}]", Loc.S("CliModelErrNotString"));
                continue;
            }
            var found = refs.Sets(t);
            if (found.Count == 1) ids.Add(found[0].Id);
            else errors.Add($"{path}[{i}]", found.Count == 0
                ? string.Format(Loc.S("CliFemCheckErrUnknownForceSet"), t)
                : string.Format(Loc.S("CliModelErrAmbiguousKey"), found.Count, t));
        }
        c.ForceSetIdsJson = JsonSerializer.Serialize(ids.Distinct().ToArray());
    }

    static string[] ReadSources(JsonNode n, string path, ModelErrors errors)
    {
        if (n is not JsonArray a)
        {
            errors.Add(path, Loc.S("CliModelErrNotArray"));
            return [];
        }
        var keys = new List<string>();
        for (int i = 0; i < a.Count; i++)
            if (StrOf(a[i]) is { } k) { if (!keys.Contains(k)) keys.Add(k); }
            else errors.Add($"{path}[{i}]", Loc.S("CliModelErrNotString"));
        return keys.Count > 0 ? [.. keys] : [FemCheckRebarSource.Section];
    }

    static SteelFemCheckParams ReadSteel(JsonObject o, SteelFemCheckParams p, Refs refs, string path, ModelErrors errors)
    {
        Unknown(o, SteelKeys, path, errors);
        if (o.ContainsKey("mesh"))
        {
            if (o["mesh"] is null) p = p with { MeshLef = null };
            else if (o["mesh"] is JsonObject mo)
            {
                Unknown(mo, MeshKeys, $"{path}.mesh", errors);
                var m = p.MeshLef ?? new SteelMeshLef();
                m = m with
                {
                    MuX = SectionJson.Num(mo, "muX", $"{path}.mesh", errors) ?? m.MuX,
                    MuY = SectionJson.Num(mo, "muY", $"{path}.mesh", errors) ?? m.MuY,
                    MuB = SectionJson.Num(mo, "muB", $"{path}.mesh", errors) ?? m.MuB,
                };
                p = p with { MeshLef = m };
            }
            else errors.Add($"{path}.mesh", Loc.S("CliModelErrNotObject"));
        }
        if (o.ContainsKey("auto"))
        {
            if (o["auto"] is null) p = p with { AutoLef = null };
            else if (o["auto"] is JsonObject ao)
            {
                Unknown(ao, AutoKeys, $"{path}.auto", errors);
                if (SectionJson.Str(ao, "analysis", $"{path}.auto", errors, required: true) is { } t)
                {
                    var found = refs.AnalysesByTag(t);
                    if (found.Count == 1) p = p with { AutoLef = new SteelAutoLef { AnalysisId = found[0].Id } };
                    else errors.Add($"{path}.auto.analysis", found.Count == 0
                        ? string.Format(Loc.S("CliFemCheckErrUnknownAnalysis"), t)
                        : string.Format(Loc.S("CliModelErrAmbiguousKey"), found.Count, t));
                }
            }
            else errors.Add($"{path}.auto", Loc.S("CliModelErrNotObject"));
        }
        return p;
    }

    static FemEtaParams? ReadEta(JsonNode? n, FemEtaParams? old, bool plate, string path, ModelErrors errors)
    {
        if (n is null) return null;
        if (n is not JsonObject o)
        {
            errors.Add(path, Loc.S("CliModelErrNotObject"));
            return old;
        }
        Unknown(o, plate ? PlateEtaKeys : EtaKeys, path, errors);
        var e = (old ?? new FemEtaParams()) with { Enabled = true };
        if (SectionJson.Bool(o, "iterative", path, errors) is bool it) e = e with { Iterative = it };
        if (o.ContainsKey("lengthM")) e = e with { LengthM = o["lengthM"] is null ? null : SectionJson.Num(o, "lengthM", path, errors) };
        if (SectionJson.Num(o, "muX", path, errors) is double mx) e = e with { MuX = mx };
        if (!plate && SectionJson.Num(o, "muY", path, errors) is double my) e = e with { MuY = my };
        if (SectionJson.Str(o, "psi", path, errors) is { } psi)
        {
            if (psi is FemEtaParams.PsiAuto or FemEtaParams.PsiManual) e = e with { PsiMode = psi };
            else errors.Add($"{path}.psi", string.Format(Loc.S("CliModelErrOneOf"), "auto, manual"));
        }
        if (SectionJson.Num(o, "psiX", path, errors) is double px) e = e with { PsiX = px };
        if (!plate && SectionJson.Num(o, "psiY", path, errors) is double py) e = e with { PsiY = py };
        if (o.ContainsKey("slendernessThreshold"))
            e = e with { SlendernessThreshold = o["slendernessThreshold"] is null ? null : SectionJson.Num(o, "slendernessThreshold", path, errors) };
        return e;
    }

    static PlateCheckParams ReadPlate(JsonObject o, PlateCheckParams p, bool sls, Refs refs, string path, ModelErrors errors)
    {
        Unknown(o, sls ? PlateSlsKeys : PlateUlsKeys, path, errors);
        if (SectionJson.Str(o, "method", path, errors) is { } m)
        {
            var found = Methods.FirstOrDefault(x => x.Doc == m);
            if (found.Doc == null)
                errors.Add($"{path}.method", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", Methods.Select(x => x.Doc))));
            else p = p with { Kind = sls ? found.Sls : found.Uls };
        }
        p = p with
        {
            StripModel = SectionJson.Pick(o, "strip", Strips, p.StripModel, path, errors),
            CapriCheckMode = SectionJson.Pick(o, "capriMode", CapriModes, p.CapriCheckMode, path, errors),
        };
        if (!sls) return p;
        if (SectionJson.Num(o, "acrcLimMm", path, errors) is double acrc) p = p with { AcrcLimMm = acrc };
        if (SectionJson.Num(o, "phi2", path, errors) is double phi2) p = p with { Phi2 = phi2 };
        if (o["phi1"] is JsonValue pv)
        {
            if (pv.TryGetValue(out string? s) && s == "auto") p = p with { Phi1Mode = "auto" };
            else if (pv.TryGetValue(out double d) && double.IsFinite(d)) p = p with { Phi1Mode = "manual", Phi1 = d };
            else errors.Add($"{path}.phi1", Loc.S("CliFemCheckErrPhi1"));
        }
        else if (o["phi1"] is not null) errors.Add($"{path}.phi1", Loc.S("CliFemCheckErrPhi1"));
        if (o.ContainsKey("nlForceSet"))
        {
            if (o["nlForceSet"] is null) p = p with { NlForceSetId = 0 };
            else if (StrOf(o["nlForceSet"]) is { } t)
            {
                var found = refs.Sets(t);
                if (found.Count == 1) p = p with { NlForceSetId = found[0].Id, LtFraction = 0 };
                else errors.Add($"{path}.nlForceSet", found.Count == 0
                    ? string.Format(Loc.S("CliFemCheckErrUnknownForceSet"), t)
                    : string.Format(Loc.S("CliModelErrAmbiguousKey"), found.Count, t));
            }
            else errors.Add($"{path}.nlForceSet", Loc.S("CliModelErrNotString"));
        }
        if (SectionJson.Num(o, "ltFraction", path, errors) is double lt) p = p with { LtFraction = lt };
        // С набором NL доля длительной не используется — как в окне, хранится 0.
        return p.NlForceSetId > 0 ? p with { LtFraction = 0 } : p;
    }
}
