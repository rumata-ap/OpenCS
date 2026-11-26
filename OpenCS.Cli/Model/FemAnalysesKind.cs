using System.Globalization;
using System.Text.Json.Nodes;
using CScore;
using CScore.Fem;
using CScore.Fem.Combinations;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Раздел «femAnalyses»: постановки расчёта МКЭ-схем. Ключ — имя постановки (<c>tag</c>) в пределах схемы
/// (<c>schema</c> — имя схемы проекта или схемы, описанной тем же документом). Найдено — обновить, нет — создать;
/// удаления нет. Ссылки — по тегам: загружения, комбинации, узлы КонЭ, постановка-источник жёсткостей. Нагрузка
/// (<c>load</c>) — <c>{"combination": имя}</c> (копия формулы комбинации на момент apply), <c>{"loadCase": имя}</c>,
/// <c>{"all": true}</c> или своя формула <c>{"terms": [{"loadCase", "k"}]}</c>. Вложенные массивы (стадии, варианты
/// пакета, загружения линейного, сочетания расчётных длин) заменяют состав; неуказанные поля у существующей постановки
/// не меняются, у новой — умолчания окна. Неизменённая постановка не перезаписывается; смена входа сбрасывает
/// результат (как правка в окне), имя — нет. Проверка — та же служба, что у окна (<see cref="FemAnalysisAuthoring"/>).
/// </summary>
sealed class FemAnalysesKind : IModelKind
{
    public string Name => "femAnalyses";
    public string SchemaResource => "OpenCS.Cli.Model.Schemas.femAnalyses.schema.json";

    /// <summary>Вид документа ↔ вид базы, в порядке окна.</summary>
    static readonly (string Doc, string Db)[] KindMap =
    [
        ("csfea", FemCsfeaRunner.AnalysisKind), ("csfeaLinear", FemCsfeaRunner.LinearAnalysisKind),
        ("csfeaBuckling", FemBucklingRunner.AnalysisKind), ("openseesLinear", FemAnalysisAuthoring.OpenSeesLinear),
        ("openseesNonlinear", FemAnalysisAuthoring.OpenSeesNonlinear),
    ];

    static readonly string[] Dofs = ["ux", "uy", "uz", "rx", "ry", "rz"];
    static readonly string[] CalcTypes = ["C", "CL", "N", "NL"];

    static readonly Dictionary<string, string[]> TopKeys = new()
    {
        ["csfea"] = ["schema", "tag", "kind", "calcType", "stages", "batch", "csfea"],
        ["csfeaLinear"] = ["schema", "tag", "kind", "calcType", "loadCases", "csfea", "stiffness"],
        ["csfeaBuckling"] = ["schema", "tag", "kind", "combinations", "buckling"],
        ["openseesLinear"] = ["schema", "tag", "kind", "load"],
        ["openseesNonlinear"] = ["schema", "tag", "kind", "calcType", "stages", "opensees"],
    };
    static readonly string[] LoadKeys = ["combination", "loadCase", "all", "terms", "type"];
    static readonly string[] TermKeys = ["loadCase", "k", "role", "mode"];
    static readonly string[] StageKeys = ["tag", "load", "step", "max", "pathControl", "continueWith"];
    static readonly string[] PathKeys = ["mode", "node", "dof", "initial", "min", "max", "target", "s", "alpha", "minS", "maxSteps"];
    static readonly string[] CsfeaKeys =
    [
        "plateRebar", "concreteTension", "psi", "crackRule", "beamShear", "importedFactors", "poisson", "physical", "geom",
        "shellInPlanePDelta", "largeDisplacements", "localPDeltaOnly", "fragmentSecondOrder", "sourcePDelta", "iterations",
        "recording", "controlNode", "controlDof",
    ];
    static readonly string[] IterKeys = ["max", "bisections", "tolU", "tolK", "omega0"];
    static readonly string[] RecKeys = ["mode", "steps", "stageEnds", "strains"];
    static readonly string[] BatchKeys = ["items", "step", "max", "stopAtLimit", "recording", "longTerm", "longTermDeflection", "imperfections"];
    static readonly string[] ImpKeys = ["loadCase", "bothSigns"];
    static readonly string[] StiffKeys = ["analysis", "batchIndex", "longTerm"];
    static readonly string[] BucklingKeys = ["modes", "forces", "method", "sway", "swayX", "swayY", "swayStiffness", "gFixed", "gPinned"];
    static readonly string[] OpenSeesKeys = ["physical", "concreteTension", "materials", "mainModel", "steelModel", "hardeningMpa", "element"];
    static readonly string[] PlateRebar = [FemCheckRebarSource.Section, FemCheckRebarSource.Assigned, FemCheckRebarSource.Selected, FemCheckRebarSource.Layout];

    static double R(double v) => Math.Round(v, 9) + 0.0;

    /// <summary>Ссылки схемы по тегам: загружения, комбинации, узлы, постановки — из базы или из документа (план).</summary>
    sealed class Refs
    {
        public required FemSchema Schema { get; init; }
        public required List<FemNode> Nodes { get; init; }
        public FemLoadCase? Case(string tag) => Schema.LoadCases.FirstOrDefault(c => c.Tag.Trim() == tag);
        public FemLoadDefinition? Combination(string tag) => Schema.LoadDefinitions.FirstOrDefault(d => d.Tag.Trim() == tag);
        public FemNode? Node(string tag) => Nodes.FirstOrDefault(n => n.NodeTag == tag);
        public string CaseTag(int id) => Schema.LoadCases.FirstOrDefault(c => c.Id == id)?.Tag.Trim() ?? id.ToString(CultureInfo.InvariantCulture);
        public string NodeTag(int id) => Nodes.FirstOrDefault(n => n.Id == id)?.NodeTag ?? id.ToString(CultureInfo.InvariantCulture);
        /// <summary>План: схема или её загружения описаны документом — Id ещё нет, источник жёсткостей не проверить.</summary>
        public bool Virtual { get; init; }
    }

    // ── Выгрузка ────────────────────────────────────────────────────────────────────────────

    public JsonArray Export(HeadlessProjectContext ctx, Action<string> warn)
    {
        var db = ctx.Db;
        var items = new JsonArray();
        var dupSchemas = db.FemSchemas.GroupBy(s => s.Tag.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        foreach (var schema in db.FemSchemas.OrderBy(s => s.Id))
        {
            if (schema.Analyses.Count == 0) continue;
            if (dupSchemas.Contains(schema.Tag.Trim()))
            {
                warn(string.Format(Loc.S("CliFemAnalysisSchemaAmbiguous"), schema.Tag.Trim()));
                continue;
            }
            var refs = new Refs { Schema = schema, Nodes = db.GetFemNodes(schema.Id) };
            var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var a in schema.Analyses.OrderBy(a => a.Id))
            {
                if (KindMap.FirstOrDefault(k => k.Db == a.Kind).Doc is not { } kind)
                {
                    warn(string.Format(Loc.S("CliFemAnalysisSkippedKind"), schema.Tag.Trim(), a.Tag, a.Kind));
                    continue;
                }
                if (!seen.Add(a.Tag.Trim()))
                {
                    warn(string.Format(Loc.S("CliFemAnalysisDuplicateSkipped"), schema.Tag.Trim(), a.Tag.Trim()));
                    continue;
                }
                if (ToJson(a, kind, refs, warn) is { } o) items.Add(o);
            }
        }
        return items;
    }

    JsonObject? ToJson(FemAnalysis a, string kind, Refs refs, Action<string> warn)
    {
        var p = FemAnalysisParams.Parse(a.ParamsJson);
        var o = new JsonObject { ["schema"] = refs.Schema.Tag.Trim(), ["tag"] = a.Tag.Trim(), ["kind"] = kind };
        switch (kind)
        {
            case "csfea":
                o["calcType"] = (p.CalcType ?? CalcType.N).ToString();
                if (p.Batch is { } batch) o["batch"] = BatchJson(batch, refs);
                else o["stages"] = new JsonArray(p.ResolveStages(a).Select(s => (JsonNode)StageJson(s, refs, withPath: false)).ToArray());
                o["csfea"] = CsfeaJson(p.Csfea ?? new(), refs);
                break;
            case "csfeaLinear":
                o["calcType"] = (p.CalcType ?? CalcType.N).ToString();
                var cases = new JsonArray();
                foreach (var s in p.Stages)
                {
                    var e = FemLoadExpression.Parse(s.LoadExpressionJson);
                    if (e is not { Mode: FemLoadExpressionMode.Single, LoadCaseIds.Count: 1, Terms.Count: 0 })
                    {
                        warn(string.Format(Loc.S("CliFemAnalysisLinearNotCases"), refs.Schema.Tag.Trim(), a.Tag.Trim()));
                        return null;
                    }
                    cases.Add(refs.CaseTag(e.LoadCaseIds[0]));
                }
                o["loadCases"] = cases;
                o["csfea"] = CsfeaJson(p.Csfea ?? new(), refs);
                if (p.Csfea?.StiffnessSource is { } src)
                {
                    var so = new JsonObject
                    {
                        ["analysis"] = refs.Schema.Analyses.FirstOrDefault(x => x.Id == src.AnalysisId)?.Tag.Trim()
                            ?? src.AnalysisId.ToString(CultureInfo.InvariantCulture),
                    };
                    if (src.BatchIndex is int bi) so["batchIndex"] = bi;
                    if (src.LongKind != null) so["longTerm"] = src.LongKind;
                    o["stiffness"] = so;
                }
                break;
            case "csfeaBuckling":
                o["combinations"] = new JsonArray(p.Stages.Select(s => LoadJson(s.LoadExpressionJson, refs)).ToArray());
                var b = p.Buckling ?? new FemBucklingParams();
                o["buckling"] = new JsonObject
                {
                    ["modes"] = b.Modes, ["forces"] = b.Forces, ["method"] = b.Method, ["sway"] = b.Sway, ["swayX"] = b.SwayX,
                    ["swayY"] = b.SwayY, ["swayStiffness"] = R(b.SwayStiffness), ["gFixed"] = R(b.GFixed), ["gPinned"] = R(b.GPinned),
                };
                break;
            case "openseesLinear":
                o["load"] = LoadJson(a.LoadExpressionJson, refs);
                break;
            case "openseesNonlinear":
                o["calcType"] = (p.CalcType ?? CalcType.C).ToString();
                o["stages"] = new JsonArray(p.ResolveStages(a).Select(s => (JsonNode)StageJson(s, refs, withPath: true)).ToArray());
                var os = new JsonObject
                {
                    ["physical"] = p.ConsiderPhysicalNonlinearity, ["concreteTension"] = p.ConsiderConcreteTension,
                    ["materials"] = p.MaterialSource == "Native" ? "native" : "translated", ["mainModel"] = p.MainMaterialModel,
                    ["steelModel"] = p.SteelModel, ["element"] = p.ElementFormulation,
                };
                if (p.SteelHardeningModulusMpa is double h) os["hardeningMpa"] = R(h);
                o["opensees"] = os;
                break;
        }
        return o;
    }

    /// <summary>Нагрузка: ссылка на комбинацию при совпадении формулы, одно загружение, «все» или своя формула.</summary>
    static JsonNode LoadJson(string? json, Refs refs)
    {
        var e = FemLoadExpression.Parse(json);
        string canonical = e.ToJson();
        var same = refs.Schema.LoadDefinitions.Where(d => d.GetExpression().ToJson() == canonical).ToList();
        if (same.Count == 1) return new JsonObject { ["combination"] = same[0].Tag.Trim() };
        if (e is { Mode: FemLoadExpressionMode.Single, LoadCaseIds.Count: 1, Terms.Count: 0, CombinationType: null })
            return new JsonObject { ["loadCase"] = refs.CaseTag(e.LoadCaseIds[0]) };
        var o = new JsonObject();
        if (e.CombinationType != null) o["type"] = e.CombinationType;
        if (e.Mode == FemLoadExpressionMode.All && e.Terms.Count == 0)
        {
            o["all"] = true;
            return o;
        }
        var terms = new JsonArray();
        foreach (var id in e.LoadCaseIds) terms.Add(new JsonObject { ["loadCase"] = refs.CaseTag(id), ["k"] = 1 });
        foreach (var t in e.Terms)
        {
            var to = new JsonObject { ["loadCase"] = refs.CaseTag(t.LoadCaseId), ["k"] = R(t.Coefficient) };
            if (t.Role != null) to["role"] = t.Role;
            if (t.Mode != null) to["mode"] = t.Mode;
            terms.Add(to);
        }
        o["terms"] = terms;
        return o;
    }

    static JsonObject StageJson(FemAnalysisStage s, Refs refs, bool withPath)
    {
        var o = new JsonObject { ["tag"] = s.Tag, ["load"] = LoadJson(s.LoadExpressionJson, refs) };
        if (s.LoadFactorStep is double st) o["step"] = R(st);
        if (s.MaxLoadFactor is double mx) o["max"] = R(mx);
        if (withPath)
        {
            if (PathJson(s.PathControl, refs) is { } pc) o["pathControl"] = pc;
            if (PathJson(s.ContinueWith, refs) is { } cw) o["continueWith"] = cw;
        }
        return o;
    }

    static JsonObject? PathJson(FemAnalysisPathControl? pc, Refs refs)
    {
        if (pc == null || pc.Mode == "LoadControl") return null;
        var o = new JsonObject();
        if (pc.Mode == "DisplacementControl")
        {
            o["mode"] = "displacement";
            if (pc.ControlNodeId is int n) o["node"] = refs.NodeTag(n);
            if (pc.ControlDof is int d and >= 1 and <= 6) o["dof"] = Dofs[d - 1];
            if (pc.InitialIncrement is double i) o["initial"] = R(i);
            if (pc.MinIncrement is double mn) o["min"] = R(mn);
            if (pc.MaxIncrement is double mx) o["max"] = R(mx);
            if (pc.TargetDisplacement is double t) o["target"] = R(t);
        }
        else
        {
            o["mode"] = "arcLength";
            if (pc.MonitorNodeId is int n) o["node"] = refs.NodeTag(n);
            if (pc.MonitorDof is int d and >= 1 and <= 6) o["dof"] = Dofs[d - 1];
            if (pc.ArcLengthS is double s) o["s"] = R(s);
            if (pc.ArcLengthAlpha is double al) o["alpha"] = R(al);
            if (pc.ArcLengthMinS is double ms) o["minS"] = R(ms);
        }
        if (pc.MaxSteps is int steps) o["maxSteps"] = steps;
        return o;
    }

    static JsonObject CsfeaJson(FemCsfeaParams c, Refs refs)
    {
        var o = new JsonObject
        {
            ["plateRebar"] = c.PlateRebarSource,
            ["concreteTension"] = c.TensionConcrete is bool t ? JsonValue.Create(t) : null,
            ["psi"] = c.Psi,
            ["crackRule"] = c.PlateCrackRule == CSfea.CScoreBridge.Structural.PlateCrackRule.Section ? "section" : "layer",
            ["beamShear"] = c.BeamShear,
            ["importedFactors"] = c.ImportedStiffnessFactors,
            ["poisson"] = c.PoissonUncracked is double nu ? JsonValue.Create(R(nu)) : null,
            ["physical"] = c.PhysicalNonlinearity,
            ["geom"] = c.GeomNonlinear,
            ["shellInPlanePDelta"] = c.ShellInPlanePDelta,
            ["largeDisplacements"] = c.GeomLargeDisplacements,
            ["localPDeltaOnly"] = c.SecondOrderScope == FemCsfeaSecondOrderScope.LocalOnly,
            ["fragmentSecondOrder"] = c.FragmentSecondOrderOnFirstOrderMasks,
            ["sourcePDelta"] = c.SourcePDelta,
            ["iterations"] = new JsonObject
            {
                ["max"] = c.MaxIterations, ["bisections"] = c.MaxBisections, ["tolU"] = c.TolDisplacement, ["tolK"] = c.TolStiffness,
                ["omega0"] = R(c.Omega0),
            },
            ["recording"] = new JsonObject
            {
                ["mode"] = c.ResultRecording, ["steps"] = c.RecordSteps, ["stageEnds"] = c.RecordStageEnds, ["strains"] = c.RecordSectionStrains,
            },
            ["controlNode"] = c.ControlNodeTag,
            ["controlDof"] = c.ControlDof is >= 0 and <= 5 ? Dofs[c.ControlDof] : Dofs[2],
        };
        return o;
    }

    static JsonObject BatchJson(FemCsfeaBatch b, Refs refs)
    {
        var o = new JsonObject
        {
            ["items"] = new JsonArray(b.Items.Select(i => LoadJson(i.LoadExpressionJson, refs)).ToArray()),
            ["step"] = R(b.LoadFactorStep), ["max"] = R(b.MaxLoadFactor), ["stopAtLimit"] = b.StopAtLimit, ["recording"] = b.Recording,
            ["longTerm"] = new JsonArray(b.LongTermPairs.Select(t => (JsonNode)t).ToArray()),
            ["longTermDeflection"] = b.LongTermDeflection,
        };
        if (b.Imperfections.Count > 0)
            o["imperfections"] = new JsonArray(b.Imperfections.Select(i => (JsonNode)new JsonObject
            {
                ["loadCase"] = refs.CaseTag(i.LoadCaseId), ["bothSigns"] = i.BothSigns,
            }).ToArray());
        return o;
    }

    // ── План ────────────────────────────────────────────────────────────────────────────────

    public IReadOnlyList<ModelChange> Plan(JsonArray items, JsonObject doc, HeadlessProjectContext ctx, ModelErrors errors)
    {
        var db = ctx.Db;
        var changes = new List<ModelChange>();
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var virtualSchemas = new Dictionary<string, Refs>(StringComparer.CurrentCultureIgnoreCase);
        for (int i = 0; i < items.Count; i++)
        {
            string path = $"femAnalyses[{i}]";
            if (items[i] is not JsonObject item)
            {
                errors.Add(path, Loc.S("CliModelErrNotObject"));
                continue;
            }
            string? schemaTag = SectionJson.Str(item, "schema", path, errors, required: true)?.Trim();
            string? tag = SectionJson.Str(item, "tag", path, errors, required: true)?.Trim();
            string? kind = SectionJson.Str(item, "kind", path, errors, required: true);
            if (schemaTag == null || tag == null || kind == null) continue;
            if (!TopKeys.TryGetValue(kind, out var allowed))
            {
                errors.Add($"{path}.kind", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", KindMap.Select(k => k.Doc))));
                continue;
            }
            Unknown(item, allowed, path, errors);
            if (!seen.Add(schemaTag + "\u0001" + tag))
            {
                errors.Add($"{path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), tag));
                continue;
            }

            // Схема: из проекта; если её описывает документ — план идёт по описанию документа (Id появятся при записи).
            var dbSchemas = db.FemSchemas.Where(s => string.Equals(s.Tag.Trim(), schemaTag, StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (dbSchemas.Count > 1)
            {
                errors.Add($"{path}.schema", string.Format(Loc.S("CliModelErrAmbiguousKey"), dbSchemas.Count, schemaTag));
                continue;
            }
            var docSchema = DocSchema(doc, schemaTag);
            Refs refs;
            if (docSchema != null)
            {
                if (!virtualSchemas.TryGetValue(schemaTag, out refs!))
                    virtualSchemas[schemaTag] = refs = VirtualRefs(docSchema, dbSchemas.SingleOrDefault(), db);
            }
            else if (dbSchemas.SingleOrDefault() is { } s)
                refs = new Refs { Schema = s, Nodes = db.GetFemNodes(s.Id) };
            else
            {
                errors.Add($"{path}.schema", string.Format(Loc.S("CliFemAnalysisErrUnknownSchema"), schemaTag));
                continue;
            }

            int before = errors.Count;
            var existing = Existing(refs.Schema, tag);
            var built = Build(item, kind, tag, refs, existing, path, errors, ctx);
            if (built == null || errors.Count > before) continue;
            foreach (var e in FemAnalysisAuthoring.Validate(refs.Schema, built, refs.Nodes.Select(n => n.Id).ToHashSet()))
                errors.Add($"{path}.{e.Path}", e.Message);
            if (errors.Count > before) continue;

            bool create = existing == null;
            var itemCopy = item;
            changes.Add(new ModelChange(Name, $"{schemaTag} / {tag}", create, c => Commit(c, itemCopy, kind, schemaTag, tag, path)));
        }
        return changes;
    }

    /// <summary>Запись: ссылки разрешаются по сохранённой схеме (её мог только что записать вид femSchemas).</summary>
    void Commit(HeadlessProjectContext ctx, JsonObject item, string kind, string schemaTag, string tag, string path)
    {
        var db = ctx.Db;
        var schema = db.FemSchemas.Single(s => string.Equals(s.Tag.Trim(), schemaTag, StringComparison.CurrentCultureIgnoreCase));
        var refs = new Refs { Schema = schema, Nodes = db.GetFemNodes(schema.Id) };
        var existing = Existing(schema, tag);
        var errors = new ModelErrors();
        var built = Build(item, kind, tag, refs, existing, path, errors, ctx);
        if (built != null)
            foreach (var e in FemAnalysisAuthoring.Validate(schema, built, refs.Nodes.Select(n => n.Id).ToHashSet()))
                errors.Add($"{path}.{e.Path}", e.Message);
        errors.ThrowIfAny();
        if (existing == null)
        {
            db.SaveFemAnalysis(built!);
            return;
        }
        var (changed, reset) = FemAnalysisAuthoring.ApplyEdit(existing, built!);
        if (!changed) return;
        db.SaveFemAnalysis(existing);
        if (reset) ctx.LogService.Warning(string.Format(Loc.S("FemAnalysisResultReset"), existing.Tag));
    }

    static FemAnalysis? Existing(FemSchema schema, string tag) =>
        schema.Analyses.FirstOrDefault(a => string.Equals(a.Tag.Trim(), tag, StringComparison.CurrentCultureIgnoreCase));

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

    static string? NodeKey(JsonNode? n) => n is JsonValue v
        ? v.TryGetValue(out string? s) ? s?.Trim() : v.TryGetValue(out long l) ? l.ToString(CultureInfo.InvariantCulture) : null
        : null;

    /// <summary>
    /// Схема, описанная документом, для плана: загружения, комбинации и узлы — из документа там, где он их задаёт
    /// (условные Id), иначе — из проекта; постановки — из проекта (повтор имени и ключ обновления).
    /// </summary>
    static Refs VirtualRefs(JsonObject docSchema, FemSchema? dbSchema, DatabaseService db)
    {
        var schema = new FemSchema { Id = dbSchema?.Id ?? -1, Tag = dbSchema?.Tag ?? "" };
        if (dbSchema != null) foreach (var a in dbSchema.Analyses) schema.Analyses.Add(a);
        int fake = -1;
        if (docSchema["loadCases"] is JsonArray cases)
        {
            foreach (var c in cases.OfType<JsonObject>())
                if (c["tag"] is JsonValue tv && tv.TryGetValue(out string? t))
                    schema.LoadCases.Add(new FemLoadCase
                    {
                        Id = fake--, Tag = t.Trim(),
                        Sp20Type = c["type"] is JsonValue yv && yv.TryGetValue(out string? y) ? y : "permanent",
                    });
        }
        else if (dbSchema != null) foreach (var c in dbSchema.LoadCases) schema.LoadCases.Add(c);
        if (docSchema["combinations"] is JsonArray combos)
        {
            foreach (var c in combos.OfType<JsonObject>())
            {
                if (c["tag"] is not JsonValue tv || !tv.TryGetValue(out string? t)) continue;
                string? type = c["type"] is JsonValue yv && yv.TryGetValue(out string? y) ? y : null;
                var ids = (c["terms"] as JsonArray ?? []).OfType<JsonObject>()
                    .Select(x => x["loadCase"] is JsonValue lv && lv.TryGetValue(out string? lt) ? lt.Trim() : null)
                    .Select(lt => schema.LoadCases.FirstOrDefault(lc => lc.Tag == lt)?.Id).OfType<int>().ToList();
                bool all = c["all"] is JsonValue av && av.TryGetValue(out bool ab) && ab;
                var e = all ? new FemLoadExpression { Mode = FemLoadExpressionMode.All, CombinationType = type }
                    : new FemLoadExpression
                    {
                        Mode = ids.Count == 1 ? FemLoadExpressionMode.Single : FemLoadExpressionMode.Sum,
                        Terms = ids.Select(id => new FemLoadTerm { LoadCaseId = id, Coefficient = 1 }).ToList(), CombinationType = type,
                    };
                schema.LoadDefinitions.Add(new FemLoadDefinition { Tag = t.Trim(), ExpressionJson = e.ToJson(), CombinationType = type });
            }
        }
        else if (dbSchema != null) foreach (var d in dbSchema.LoadDefinitions) schema.LoadDefinitions.Add(d);
        List<FemNode> nodes;
        if (docSchema["nodes"] is JsonArray ns)
            nodes = ns.OfType<JsonObject>().Select(n => NodeKey(n["tag"])).OfType<string>()
                .Select(t => new FemNode { Id = fake--, NodeTag = t }).ToList();
        else nodes = dbSchema != null ? db.GetFemNodes(dbSchema.Id) : [];
        return new Refs { Schema = schema, Nodes = nodes, Virtual = true };
    }

    // ── Разбор в постановку ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Постановка по элементу документа поверх <paramref name="existing"/> (того же вида) или умолчаний окна; ссылки —
    /// через <paramref name="refs"/>. null — ошибки (в <paramref name="errors"/>).
    /// </summary>
    static FemAnalysis? Build(JsonObject item, string kind, string tag, Refs refs, FemAnalysis? existing, string path,
        ModelErrors errors, HeadlessProjectContext ctx)
    {
        string dbKind = KindMap.First(k => k.Doc == kind).Db;
        bool sameKind = existing?.Kind == dbKind;
        var p = sameKind ? FemAnalysisParams.Parse(existing!.ParamsJson) : new FemAnalysisParams();
        var oldStages = sameKind ? p.ResolveStages(existing!).ToList() : [];
        string loadJson = sameKind ? existing!.LoadExpressionJson : "{}";
        int before = errors.Count;

        if (item["calcType"] is JsonNode ct)
        {
            if (ct is JsonValue cv && cv.TryGetValue(out string? c) && CalcTypes.Contains(c)) p.CalcType = Enum.Parse<CalcType>(c);
            else errors.Add($"{path}.calcType", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", CalcTypes)));
        }
        else if (!sameKind) p.CalcType = kind is "csfea" or "csfeaLinear" ? CalcType.N : kind == "openseesNonlinear" ? CalcType.C : null;

        switch (kind)
        {
            case "csfea":
            {
                p.Csfea ??= new FemCsfeaParams();
                if (item["csfea"] is JsonObject co) ReadCsfea(co, p.Csfea, $"{path}.csfea", errors);
                bool hasBatch = item["batch"] is not null, hasStages = item["stages"] is not null;
                if (hasBatch && hasStages) errors.Add($"{path}.batch", Loc.S("CliFemAnalysisErrBatchAndStages"));
                if (item["batch"] is JsonObject bo)
                {
                    p.Batch = ReadBatch(bo, p.Batch ?? new FemCsfeaBatch(), refs, $"{path}.batch", errors);
                    p.Stages = [];
                    if (p.Batch?.Items.Count > 0) loadJson = p.Batch.Items[0].LoadExpressionJson;
                }
                else if (hasBatch) errors.Add($"{path}.batch", Loc.S("CliModelErrNotObject"));
                else if (hasStages)
                {
                    p.Batch = null;
                    p.Stages = ReadStages(item["stages"], oldStages, refs, csfea: true, $"{path}.stages", errors);
                }
                else if (!sameKind) errors.Add($"{path}.stages", Loc.S("CliModelErrRequired"));
                break;
            }
            case "csfeaLinear":
            {
                p.Csfea ??= new FemCsfeaParams();
                if (item["csfea"] is JsonObject co) ReadCsfea(co, p.Csfea, $"{path}.csfea", errors);
                if (item["loadCases"] is JsonNode lcs) p.Stages = ReadLinearCases(lcs, oldStages, refs, $"{path}.loadCases", errors);
                else if (!sameKind) errors.Add($"{path}.loadCases", Loc.S("CliModelErrRequired"));
                if (item.ContainsKey("stiffness"))
                    p.Csfea.StiffnessSource = ReadStiffness(item["stiffness"], p.Csfea.StiffnessSource, refs, ctx, $"{path}.stiffness", errors);
                if (p.Csfea.StiffnessSource == null) p.Csfea.SourcePDelta = false;
                break;
            }
            case "csfeaBuckling":
            {
                if (item["combinations"] is JsonArray ca)
                {
                    var stages = new List<FemAnalysisStage>();
                    for (int k = 0; k < ca.Count; k++)
                        if (ReadLoad(ca[k], refs, $"{path}.combinations[{k}]", errors) is { } l)
                            stages.Add(new FemAnalysisStage { Tag = l.Tag, LoadExpressionJson = l.Json, LoadFactorStep = 1.0, MaxLoadFactor = 1.0 });
                    p.Stages = stages;
                }
                else if (item["combinations"] is not null) errors.Add($"{path}.combinations", Loc.S("CliModelErrNotArray"));
                else if (!sameKind) errors.Add($"{path}.combinations", Loc.S("CliModelErrRequired"));
                p.Buckling ??= new FemBucklingParams();
                if (item["buckling"] is JsonObject bo) ReadBuckling(bo, p.Buckling, $"{path}.buckling", errors);
                break;
            }
            case "openseesLinear":
                if (item["load"] is JsonNode ln) { if (ReadLoad(ln, refs, $"{path}.load", errors) is { } l) loadJson = l.Json; }
                else if (!sameKind) errors.Add($"{path}.load", Loc.S("CliModelErrRequired"));
                break;
            case "openseesNonlinear":
                if (item["stages"] is JsonNode sn) p.Stages = ReadStages(sn, oldStages, refs, csfea: false, $"{path}.stages", errors);
                else if (!sameKind) errors.Add($"{path}.stages", Loc.S("CliModelErrRequired"));
                if (item["opensees"] is JsonObject oo) ReadOpenSees(oo, p, $"{path}.opensees", errors);
                break;
        }
        if (errors.Count > before) return null;
        if (kind != "openseesLinear" && p.Batch == null)
            loadJson = p.Stages.Count > 0 ? p.Stages[0].LoadExpressionJson : loadJson;
        string paramsJson = p.ToJson();
        loadJson = FemLoadExpression.Parse(loadJson).ToJson();
        // То же по содержанию, что сохранено, — сохранённый текст как есть: неизменённая постановка не считается
        // изменённой из-за порядка полей или старой записи.
        if (sameKind)
        {
            if (FemAnalysisParams.Parse(existing!.ParamsJson).ToJson() == paramsJson) paramsJson = existing.ParamsJson;
            if (FemLoadExpression.Parse(existing.LoadExpressionJson).ToJson() == loadJson) loadJson = existing.LoadExpressionJson;
        }
        return new FemAnalysis
        {
            Id = existing?.Id ?? 0, SchemaId = refs.Schema.Id, Tag = tag, Kind = dbKind,
            LoadExpressionJson = loadJson, ParamsJson = paramsJson,
        };
    }

    /// <summary>Нагрузка документа → (имя для стадии/варианта, формула, вариант пакета).</summary>
    sealed record LoadRef(string Tag, string Json, FemCsfeaBatchItem Item);

    static LoadRef? ReadLoad(JsonNode? node, Refs refs, string path, ModelErrors errors)
    {
        if (node is not JsonObject o) { errors.Add(path, Loc.S("CliModelErrNotObject")); return null; }
        Unknown(o, LoadKeys, path, errors);
        if (SectionJson.Str(o, "combination", path, errors) is { } ct)
        {
            if (refs.Combination(ct.Trim()) is not { } d)
            {
                errors.Add($"{path}.combination", string.Format(Loc.S("CliFemAnalysisErrUnknownCombination"), ct));
                return null;
            }
            var item = FemAnalysisAuthoring.BatchItem(d);
            return new(d.Tag.Trim(), item.LoadExpressionJson, item);
        }
        if (SectionJson.Str(o, "loadCase", path, errors) is { } lt)
        {
            if (refs.Case(lt.Trim()) is not { } lc)
            {
                errors.Add($"{path}.loadCase", string.Format(Loc.S("CliFemErrUnknownLoadCase"), lt));
                return null;
            }
            var item = FemAnalysisAuthoring.BatchItem(lc);
            return new(lc.Tag.Trim(), item.LoadExpressionJson, item);
        }
        string? type = SectionJson.Str(o, "type", path, errors);
        FemLoadExpression e;
        if (SectionJson.Bool(o, "all", path, errors) == true)
            e = new FemLoadExpression { Mode = FemLoadExpressionMode.All, CombinationType = type };
        else if (o["terms"] is JsonArray ta && ta.Count > 0)
        {
            var terms = new List<FemLoadTerm>();
            for (int k = 0; k < ta.Count; k++)
            {
                string tp = $"{path}.terms[{k}]";
                if (ta[k] is not JsonObject to) { errors.Add(tp, Loc.S("CliModelErrNotObject")); continue; }
                Unknown(to, TermKeys, tp, errors);
                string? caseTag = SectionJson.Str(to, "loadCase", tp, errors, required: true);
                double? kk = SectionJson.Num(to, "k", tp, errors, required: true);
                string? role = SectionJson.Str(to, "role", tp, errors);
                string? mode = SectionJson.Str(to, "mode", tp, errors);
                if (caseTag == null || kk == null) continue;
                if (refs.Case(caseTag.Trim()) is not { } lc) { errors.Add($"{tp}.loadCase", string.Format(Loc.S("CliFemErrUnknownLoadCase"), caseTag)); continue; }
                terms.Add(new FemLoadTerm { LoadCaseId = lc.Id, Coefficient = kk.Value, Role = role, Mode = mode });
            }
            e = new FemLoadExpression { Mode = terms.Count == 1 ? FemLoadExpressionMode.Single : FemLoadExpressionMode.Sum, Terms = terms, CombinationType = type };
        }
        else
        {
            errors.Add(path, Loc.S("CliFemAnalysisErrLoadEmpty"));
            return null;
        }
        string json = e.ToJson();
        string tag = Loc.S("FemLoadSourceUnresolved");
        return new(tag, json, new FemCsfeaBatchItem
        {
            Tag = tag, LoadExpressionJson = json, Kind = FemCsfeaBatchItemKind.Combination,
            Type = FemCombinationTypes.Normalize(type),
        });
    }

    static List<FemAnalysisStage> ReadStages(JsonNode? node, List<FemAnalysisStage> old, Refs refs, bool csfea, string path, ModelErrors errors)
    {
        var list = new List<FemAnalysisStage>();
        if (node is not JsonArray a) { errors.Add(path, Loc.S("CliModelErrNotArray")); return list; }
        for (int k = 0; k < a.Count; k++)
        {
            string sp = $"{path}[{k}]";
            if (a[k] is not JsonObject so) { errors.Add(sp, Loc.S("CliModelErrNotObject")); continue; }
            Unknown(so, csfea ? StageKeys[..4] : StageKeys, sp, errors);
            var load = ReadLoad(so["load"], refs, $"{sp}.load", errors);
            if (load == null) continue;
            var prev = k < old.Count ? old[k] : null;
            var stage = new FemAnalysisStage
            {
                // Имя без указания — прежнее у той же нагрузки, иначе имя нагрузки (как окно при выборе нагрузки).
                Tag = SectionJson.Str(so, "tag", sp, errors)
                    ?? (prev != null && prev.LoadExpressionJson == load.Json ? prev.Tag : load.Tag),
                LoadExpressionJson = load.Json,
                LoadFactorStep = SectionJson.Num(so, "step", sp, errors) ?? prev?.LoadFactorStep ?? 0.1,
                MaxLoadFactor = SectionJson.Num(so, "max", sp, errors) ?? prev?.MaxLoadFactor ?? (csfea ? 1.0 : 10.0),
            };
            if (!csfea)
            {
                // Как окно: у стадии OpenSees всегда есть путь (по умолчанию — по нагрузке).
                stage.PathControl = so.ContainsKey("pathControl") ? ReadPath(so["pathControl"], refs, $"{sp}.pathControl", errors)
                    ?? new FemAnalysisPathControl() : prev?.PathControl ?? new FemAnalysisPathControl();
                stage.ContinueWith = so.ContainsKey("continueWith") ? ReadPath(so["continueWith"], refs, $"{sp}.continueWith", errors)
                    : prev?.ContinueWith;
            }
            list.Add(stage);
        }
        return list;
    }

    static FemAnalysisPathControl? ReadPath(JsonNode? node, Refs refs, string path, ModelErrors errors)
    {
        if (node == null) return null;
        if (node is not JsonObject o) { errors.Add(path, Loc.S("CliModelErrNotObject")); return null; }
        Unknown(o, PathKeys, path, errors);
        string? mode = SectionJson.Str(o, "mode", path, errors, required: true);
        if (mode is "load") return new FemAnalysisPathControl();
        if (mode is not ("displacement" or "arcLength"))
        {
            if (mode != null) errors.Add($"{path}.mode", string.Format(Loc.S("CliModelErrOneOf"), "load, displacement, arcLength"));
            return null;
        }
        int? nodeId = null;
        if (NodeKey(o["node"]) is { } nt)
        {
            if (refs.Node(nt) is { } n) nodeId = n.Id;
            else errors.Add($"{path}.node", string.Format(Loc.S("CliFemAnalysisErrUnknownNode"), nt));
        }
        else errors.Add($"{path}.node", Loc.S("CliModelErrRequired"));
        int? dof = null;
        if (SectionJson.Str(o, "dof", path, errors, required: true) is { } ds)
        {
            int idx = Array.IndexOf(Dofs, ds);
            if (idx >= 0) dof = idx + 1;
            else errors.Add($"{path}.dof", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", Dofs)));
        }
        int? steps = SectionJson.Int(o, "maxSteps", path, errors);
        return mode == "displacement"
            ? new FemAnalysisPathControl
            {
                Mode = "DisplacementControl", ControlNodeId = nodeId, ControlDof = dof,
                InitialIncrement = SectionJson.Num(o, "initial", path, errors, required: true),
                MinIncrement = SectionJson.Num(o, "min", path, errors, required: true),
                MaxIncrement = SectionJson.Num(o, "max", path, errors, required: true),
                TargetDisplacement = SectionJson.Num(o, "target", path, errors, required: true), MaxSteps = steps,
            }
            : new FemAnalysisPathControl
            {
                Mode = "ArcLength", MonitorNodeId = nodeId, MonitorDof = dof,
                ArcLengthS = SectionJson.Num(o, "s", path, errors, required: true),
                ArcLengthAlpha = SectionJson.Num(o, "alpha", path, errors, required: true),
                ArcLengthMinS = SectionJson.Num(o, "minS", path, errors, required: true), MaxSteps = steps,
            };
    }

    static List<FemAnalysisStage> ReadLinearCases(JsonNode node, List<FemAnalysisStage> old, Refs refs, string path, ModelErrors errors)
    {
        IEnumerable<FemLoadCase> cases;
        if (node is JsonValue v && v.TryGetValue(out string? s) && s == "all") cases = refs.Schema.LoadCases;
        else if (node is JsonArray a)
        {
            var list = new List<FemLoadCase>();
            for (int k = 0; k < a.Count; k++)
            {
                string? t = a[k] is JsonValue tv && tv.TryGetValue(out string? ts) ? ts.Trim() : null;
                if (t == null) { errors.Add($"{path}[{k}]", Loc.S("CliModelErrNotString")); continue; }
                if (refs.Case(t) is not { } lc) { errors.Add($"{path}[{k}]", string.Format(Loc.S("CliFemErrUnknownLoadCase"), t)); continue; }
                if (list.Contains(lc)) { errors.Add($"{path}[{k}]", string.Format(Loc.S("CliModelErrDuplicateKey"), t)); continue; }
                list.Add(lc);
            }
            cases = list;
        }
        else { errors.Add(path, Loc.S("CliFemAnalysisErrLoadCases")); return []; }
        return cases.Select(lc =>
        {
            string json = FemAnalysisAuthoring.SingleCase(lc.Id);
            // Имя стадии — прежнее у того же загружения (переименованное загружение не сбрасывает результат).
            string tag = old.FirstOrDefault(o => o.LoadExpressionJson == json)?.Tag ?? lc.Tag;
            return new FemAnalysisStage { Tag = tag, LoadExpressionJson = json, LoadFactorStep = 1.0, MaxLoadFactor = 1.0 };
        }).ToList();
    }

    static FemCsfeaBatch? ReadBatch(JsonObject o, FemCsfeaBatch b, Refs refs, string path, ModelErrors errors)
    {
        Unknown(o, BatchKeys, path, errors);
        if (o["items"] is JsonArray ia)
        {
            var items = new List<FemCsfeaBatchItem>();
            for (int k = 0; k < ia.Count; k++)
                if (ReadLoad(ia[k], refs, $"{path}.items[{k}]", errors) is { } l)
                {
                    // Тот же вариант — с прежним именем и бейджем (неизменённый пакет не перезаписывается).
                    var same = b.Items.FirstOrDefault(x => x.LoadExpressionJson == l.Json);
                    items.Add(same ?? l.Item);
                }
            b.Items = items;
        }
        else if (o["items"] is not null) errors.Add($"{path}.items", Loc.S("CliModelErrNotArray"));
        if (SectionJson.Num(o, "step", path, errors) is double st) b.LoadFactorStep = st;
        if (SectionJson.Num(o, "max", path, errors) is double mx) b.MaxLoadFactor = mx;
        if (SectionJson.Bool(o, "stopAtLimit", path, errors) is bool sl) b.StopAtLimit = sl;
        if (SectionJson.Str(o, "recording", path, errors) is { } rec) b.Recording = rec;
        if (o["longTerm"] is JsonArray la)
        {
            var pairs = new List<string>();
            for (int k = 0; k < la.Count; k++)
                if (la[k] is JsonValue lv && lv.TryGetValue(out string? t) && t is FemCombinationTypes.N or FemCombinationTypes.C)
                    pairs.Add(t);
                else errors.Add($"{path}.longTerm[{k}]", string.Format(Loc.S("CliModelErrOneOf"), "N, C"));
            b.LongTermPairs = pairs;
        }
        if (SectionJson.Bool(o, "longTermDeflection", path, errors) is bool ld) b.LongTermDeflection = ld;
        if (o["imperfections"] is JsonArray ma)
        {
            var list = new List<FemCsfeaBatchImperfection>();
            for (int k = 0; k < ma.Count; k++)
            {
                string ip = $"{path}.imperfections[{k}]";
                if (ma[k] is not JsonObject io) { errors.Add(ip, Loc.S("CliModelErrNotObject")); continue; }
                Unknown(io, ImpKeys, ip, errors);
                string? t = SectionJson.Str(io, "loadCase", ip, errors, required: true);
                if (t == null) continue;
                if (refs.Case(t.Trim()) is not { } lc) { errors.Add($"{ip}.loadCase", string.Format(Loc.S("CliFemErrUnknownLoadCase"), t)); continue; }
                list.Add(new FemCsfeaBatchImperfection { LoadCaseId = lc.Id, BothSigns = SectionJson.Bool(io, "bothSigns", ip, errors) ?? true });
            }
            b.Imperfections = list;
        }
        else if (o["imperfections"] is not null) errors.Add($"{path}.imperfections", Loc.S("CliModelErrNotArray"));
        // Как окно: флажок длительных частей типа, которого нет в пакете, не сохраняется.
        b.LongTermPairs.RemoveAll(t => !FemCsfeaBatchLongTerm.HasCombinations(b.Items, t));
        return b;
    }

    static void ReadCsfea(JsonObject o, FemCsfeaParams c, string path, ModelErrors errors)
    {
        Unknown(o, CsfeaKeys, path, errors);
        if (SectionJson.Str(o, "plateRebar", path, errors) is { } pr)
        {
            if (PlateRebar.Contains(pr)) c.PlateRebarSource = pr;
            else errors.Add($"{path}.plateRebar", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", PlateRebar)));
        }
        if (o.ContainsKey("concreteTension")) c.TensionConcrete = o["concreteTension"] is null ? null : SectionJson.Bool(o, "concreteTension", path, errors);
        if (SectionJson.Bool(o, "psi", path, errors) is bool psi) c.Psi = psi;
        if (SectionJson.Str(o, "crackRule", path, errors) is { } cr)
        {
            if (cr is "layer" or "section")
                c.PlateCrackRule = cr == "section" ? CSfea.CScoreBridge.Structural.PlateCrackRule.Section : CSfea.CScoreBridge.Structural.PlateCrackRule.Layer;
            else errors.Add($"{path}.crackRule", string.Format(Loc.S("CliModelErrOneOf"), "layer, section"));
        }
        if (SectionJson.Bool(o, "beamShear", path, errors) is bool bs) c.BeamShear = bs;
        if (SectionJson.Bool(o, "importedFactors", path, errors) is bool imf) c.ImportedStiffnessFactors = imf;
        if (o.ContainsKey("poisson")) c.PoissonUncracked = o["poisson"] is null ? null : SectionJson.Num(o, "poisson", path, errors);
        if (SectionJson.Bool(o, "physical", path, errors) is bool ph) c.PhysicalNonlinearity = ph;
        if (SectionJson.Bool(o, "geom", path, errors) is bool g) c.GeomNonlinear = g;
        if (SectionJson.Bool(o, "shellInPlanePDelta", path, errors) is bool sp) c.ShellInPlanePDelta = sp;
        if (SectionJson.Bool(o, "largeDisplacements", path, errors) is bool ld) c.GeomLargeDisplacements = ld;
        if (SectionJson.Bool(o, "localPDeltaOnly", path, errors) is bool lp)
            c.SecondOrderScope = lp ? FemCsfeaSecondOrderScope.LocalOnly : FemCsfeaSecondOrderScope.Full;
        if (SectionJson.Bool(o, "fragmentSecondOrder", path, errors) is bool fs) c.FragmentSecondOrderOnFirstOrderMasks = fs;
        if (SectionJson.Bool(o, "sourcePDelta", path, errors) is bool spd) c.SourcePDelta = spd;
        if (o["iterations"] is JsonObject it)
        {
            string ip = $"{path}.iterations";
            Unknown(it, IterKeys, ip, errors);
            if (SectionJson.Int(it, "max", ip, errors) is int mx) c.MaxIterations = mx;
            if (SectionJson.Int(it, "bisections", ip, errors) is int bi) c.MaxBisections = bi;
            if (SectionJson.Num(it, "tolU", ip, errors) is double tu) c.TolDisplacement = tu;
            if (SectionJson.Num(it, "tolK", ip, errors) is double tk) c.TolStiffness = tk;
            if (SectionJson.Num(it, "omega0", ip, errors) is double om) c.Omega0 = om;
        }
        if (o["recording"] is JsonObject rc)
        {
            string rp = $"{path}.recording";
            Unknown(rc, RecKeys, rp, errors);
            if (SectionJson.Str(rc, "mode", rp, errors) is { } m) c.ResultRecording = m;
            if (SectionJson.Str(rc, "steps", rp, errors) is { } st) c.RecordSteps = st.Trim();
            if (SectionJson.Bool(rc, "stageEnds", rp, errors) is bool se) c.RecordStageEnds = se;
            if (SectionJson.Bool(rc, "strains", rp, errors) is bool sr) c.RecordSectionStrains = sr;
        }
        if (o.ContainsKey("controlNode"))
        {
            if (o["controlNode"] is null) c.ControlNodeTag = "";
            else if (NodeKey(o["controlNode"]) is { } cn) c.ControlNodeTag = cn;
            else errors.Add($"{path}.controlNode", Loc.S("CliModelErrBadValue"));
        }
        if (SectionJson.Str(o, "controlDof", path, errors) is { } cd)
        {
            int idx = Array.IndexOf(Dofs, cd);
            if (idx >= 0) c.ControlDof = idx;
            else errors.Add($"{path}.controlDof", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", Dofs)));
        }
    }

    /// <summary>
    /// Источник жёсткостей линейного: секущая постановка схемы по имени (вариант пакета, длительная часть). Тот же, что
    /// сохранён, — с прежним отпечатком; новый — отпечаток текущего результата (нет результата — ошибка).
    /// </summary>
    static FemCsfeaStiffnessSource? ReadStiffness(JsonNode? node, FemCsfeaStiffnessSource? current, Refs refs,
        HeadlessProjectContext ctx, string path, ModelErrors errors)
    {
        if (node == null) return null;
        if (node is not JsonObject o) { errors.Add(path, Loc.S("CliModelErrNotObject")); return null; }
        Unknown(o, StiffKeys, path, errors);
        string? at = SectionJson.Str(o, "analysis", path, errors, required: true);
        int? batchIndex = SectionJson.Int(o, "batchIndex", path, errors);
        string? longKind = SectionJson.Str(o, "longTerm", path, errors);
        if (at == null) return null;
        var source = refs.Schema.Analyses.FirstOrDefault(a => string.Equals(a.Tag.Trim(), at.Trim(), StringComparison.CurrentCultureIgnoreCase)
            && a.Kind == FemCsfeaRunner.AnalysisKind);
        if (source == null)
        {
            errors.Add($"{path}.analysis", string.Format(Loc.S("CliFemAnalysisErrStiffnessSource"), at));
            return null;
        }
        if (current != null && current.AnalysisId == source.Id && current.BatchIndex == batchIndex && current.LongKind == longKind)
            return current;
        if (refs.Virtual && refs.Schema.Id < 0) { errors.Add($"{path}.analysis", Loc.S("CliFemAnalysisErrStiffnessNoResult")); return null; }
        var candidate = FemCsfeaStiffnessSources.List(ctx.Db, refs.Schema.Id)
            .FirstOrDefault(c => c.Analysis.Id == source.Id && c.BatchIndex == batchIndex && c.LongKind == longKind);
        if (candidate == null)
        {
            errors.Add($"{path}.analysis", Loc.S("CliFemAnalysisErrStiffnessNoResult"));
            return null;
        }
        var fp = FemCsfeaStiffnessSources.Fingerprint(ctx.Db, candidate, out var error);
        if (fp == null) errors.Add($"{path}.analysis", error ?? Loc.S("CliFemAnalysisErrStiffnessNoResult"));
        return fp;
    }

    static void ReadBuckling(JsonObject o, FemBucklingParams b, string path, ModelErrors errors)
    {
        Unknown(o, BucklingKeys, path, errors);
        if (SectionJson.Int(o, "modes", path, errors) is int m) b.Modes = m;
        if (SectionJson.Str(o, "forces", path, errors) is { } f)
        {
            if (f is FemBucklingParams.ForcesEnvelope or FemBucklingParams.ForcesGoverning) b.Forces = f;
            else errors.Add($"{path}.forces", string.Format(Loc.S("CliModelErrOneOf"), $"{FemBucklingParams.ForcesEnvelope}, {FemBucklingParams.ForcesGoverning}"));
        }
        if (SectionJson.Str(o, "method", path, errors) is { } me) b.Method = me;
        if (SectionJson.Str(o, "sway", path, errors) is { } sw) b.Sway = sw;
        if (SectionJson.Bool(o, "swayX", path, errors) is bool sx) b.SwayX = sx;
        if (SectionJson.Bool(o, "swayY", path, errors) is bool sy) b.SwayY = sy;
        if (SectionJson.Num(o, "swayStiffness", path, errors) is double ss) b.SwayStiffness = ss;
        if (SectionJson.Num(o, "gFixed", path, errors) is double gf) b.GFixed = gf;
        if (SectionJson.Num(o, "gPinned", path, errors) is double gp) b.GPinned = gp;
    }

    static void ReadOpenSees(JsonObject o, FemAnalysisParams p, string path, ModelErrors errors)
    {
        Unknown(o, OpenSeesKeys, path, errors);
        if (SectionJson.Bool(o, "physical", path, errors) is bool ph) p.ConsiderPhysicalNonlinearity = ph;
        if (SectionJson.Bool(o, "concreteTension", path, errors) is bool ct) p.ConsiderConcreteTension = ct;
        if (SectionJson.Str(o, "materials", path, errors) is { } m)
        {
            if (m is "translated" or "native") p.MaterialSource = m == "native" ? "Native" : "Translated";
            else errors.Add($"{path}.materials", string.Format(Loc.S("CliModelErrOneOf"), "translated, native"));
        }
        static void One(JsonObject o, string key, string[] options, Action<string> set, string path, ModelErrors errors)
        {
            if (SectionJson.Str(o, key, path, errors) is not { } v) return;
            if (options.Contains(v)) set(v);
            else errors.Add($"{path}.{key}", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", options)));
        }
        One(o, "mainModel", ["Concrete0102", "Concrete04", "Steel01", "Steel02"], v => p.MainMaterialModel = v, path, errors);
        One(o, "steelModel", ["Steel01", "Steel02"], v => p.SteelModel = v, path, errors);
        One(o, "element", ["forceBeamColumn", "dispBeamColumn"], v => p.ElementFormulation = v, path, errors);
        if (o.ContainsKey("hardeningMpa")) p.SteelHardeningModulusMpa = o["hardeningMpa"] is null ? null : SectionJson.Num(o, "hardeningMpa", path, errors);
    }
}
