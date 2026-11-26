using System.Text.Json;
using System.Text.Json.Nodes;
using CScore.Fem;
using CScore.Fem.Combinations;
using CScore.Fem.Editing;
using CScore.Fem.Imperfections;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Загружения (<c>loadCases</c>) с нагрузками (<c>loads</c>) и элементами несовершенства (<c>imperfections</c>),
/// комбинации (<c>combinations</c>). Единицы — как в окнах: кН, кН·м, кН/м, кПа, отступы — м, перемещения — мм,
/// повороты — мрад. В документ входят нагрузки на узлы и стержни КонЭ, заданные перемещения узлов КонЭ и равномерные
/// нагрузки на пластины (КонЭ или группу); нагрузки сеточного уровня (на КЭ по номерам, неравномерные, импортные)
/// документом не описываются и при замене <c>loads</c> не трогаются.
/// </summary>
sealed partial class FemSchemasKind
{
    static readonly string[] LoadCaseKeys = ["tag", "type", "group", "gammaF", "gammaFReduced", "longTermShare", "selfWeight", "loads", "imperfections"];
    static readonly string[] LoadCaseTypes = [FemSp20Types.Permanent, FemSp20Types.LongTerm, FemSp20Types.ShortTerm,
        FemSp20Types.Accidental, FemSp20Types.Imperfection];
    static readonly string[] NodeLoadKeys = ["node", "f", "m"];
    static readonly string[] KinematicKeys = ["node", "displacement"];
    static readonly string[] MemberLoadKeys = ["member", "axes", "q", "q2", "point", "m", "a", "b"];
    static readonly string[] AreaLoadKeys = ["plates", "group", "p", "direction"];
    static readonly string[] Directions = ["x", "y", "z", "normal"];
    static readonly string[] ImperfectionKeys = ["tag", "kind", "comment", "params"];
    static readonly string[] ImperfectionKinds = [FemImperfectionKinds.Sway, FemImperfectionKinds.Bow,
        FemImperfectionKinds.WallBow, FemImperfectionKinds.Modes];
    static readonly string[] CombinationKeys = ["tag", "type", "comment", "terms", "all"];
    static readonly string[] TermKeys = ["loadCase", "k", "role"];
    static readonly string[] Roles = [FemLoadTermRoles.Permanent, FemLoadTermRoles.PermanentReduced, FemLoadTermRoles.Leading,
        FemLoadTermRoles.Second, FemLoadTermRoles.Other, FemLoadTermRoles.Accidental];

    /// <summary>Параметры несовершенств в документе — имена полей записи в camelCase; лишнее поле — ошибка.</summary>
    static readonly JsonSerializerOptions ImperfectionJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    abstract record LoadDoc(string Path);
    sealed record NodeLoadDoc(string Path, string Node, double[] F, double[] M) : LoadDoc(Path);
    sealed record KinematicDoc(string Path, string Node, List<(int Dof, double Value, string Path)> Values) : LoadDoc(Path);
    sealed record MemberLoadDoc(string Path, string Member, string Axes, string Distribution, double[] Q, double[] Q2,
        double[] M, double A, double B) : LoadDoc(Path);
    sealed record AreaLoadDoc(string Path, List<string>? Plates, string? Group, double Kpa, string Direction) : LoadDoc(Path);
    sealed record ImperfectionDoc(string Path, string Tag, string Kind, string? Comment, JsonObject Params, string? Analysis);
    sealed record LoadCaseDoc(string Path, string Tag, string Type, string? Group, double? GammaF, double? GammaFReduced,
        double? Share, double? SelfWeight, List<LoadDoc>? Loads, List<ImperfectionDoc>? Imperfections);
    sealed record TermDoc(string Path, string LoadCase, double? K, string? Role);
    sealed record CombinationDoc(string Path, string Tag, string? Type, string? Comment, bool All, List<TermDoc> Terms);

    // ── Выгрузка ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Загружения с нагрузками и несовершенствами, комбинации — для <see cref="ToJson"/>.</summary>
    static (JsonArray LoadCases, JsonArray Combinations) LoadsJson(DatabaseService db, FemSchema schema, Action<string> warn)
    {
        var session = FemSchemaAuthoring.OpenSession(db, schema);
        var nodeTag = session.Nodes.ToDictionary(n => n.Id, n => n.NodeTag);
        var memberTag = session.Members.ToDictionary(m => m.Id, m => m.ElemTag);
        var groupTag = session.MemberGroups.ToDictionary(g => g.Id, g => g.Tag);
        var imperfections = db.GetFemImperfections(schema.Id);
        var analyses = db.GetFemAnalyses(schema.Id);
        var caseTag = session.LoadCases.ToDictionary(c => c.Id, c => c.Tag);
        int skipped = 0;

        var cases = new JsonArray();
        foreach (var lc in session.LoadCases)
        {
            var o = new JsonObject { ["tag"] = lc.Tag, ["type"] = lc.Sp20Type };
            if (lc.Sp20Group != null) o["group"] = lc.Sp20Group;
            if (lc.GammaFUnfav is double g) o["gammaF"] = g;
            if (lc.GammaFFav is double gr) o["gammaFReduced"] = gr;
            if (lc.LongTermShare is double s) o["longTermShare"] = s;
            if (lc.SelfWeightFactor is double w && w != 0) o["selfWeight"] = w;

            var loads = new JsonArray();
            foreach (var l in session.NodeLoads.Where(l => l.LoadCaseId == lc.Id))
            {
                var lo = new JsonObject { ["node"] = Tag(nodeTag, l.NodeId) };
                if (l.Fx != 0 || l.Fy != 0 || l.Fz != 0) lo["f"] = Vec(l.Fx / 1e3, l.Fy / 1e3, l.Fz / 1e3);
                if (l.Mx != 0 || l.My != 0 || l.Mz != 0) lo["m"] = Vec(l.Mx / 1e3, l.My / 1e3, l.Mz / 1e3);
                if (lo.Count == 1) lo["f"] = Vec(0, 0, 0);
                loads.Add(lo);
            }
            foreach (var l in session.MemberLoads.Where(l => l.LoadCaseId == lc.Id))
            {
                var lo = new JsonObject { ["member"] = memberTag.GetValueOrDefault(l.MemberId, l.MemberId.ToString()) };
                if (l.CoordinateSystem != "local") lo["axes"] = l.CoordinateSystem;
                if (l.DistributionType == "point")
                {
                    lo["point"] = Vec(l.QxStart / 1e3, l.QyStart / 1e3, l.QzStart / 1e3);
                    if (l.Mx != 0 || l.My != 0 || l.Mz != 0) lo["m"] = Vec(l.Mx / 1e3, l.My / 1e3, l.Mz / 1e3);
                }
                else
                {
                    lo["q"] = Vec(l.QxStart / 1e3, l.QyStart / 1e3, l.QzStart / 1e3);
                    if (l.DistributionType == "trapezoidal") lo["q2"] = Vec(l.QxEnd / 1e3, l.QyEnd / 1e3, l.QzEnd / 1e3);
                }
                if (l.StartOffsetM != 0) lo["a"] = R(l.StartOffsetM);
                if (l.EndOffsetM != 0) lo["b"] = R(l.EndOffsetM);
                loads.Add(lo);
            }
            foreach (var group in session.KinematicLoads.Where(l => l.LoadCaseId == lc.Id).GroupBy(l => l.NodeId))
            {
                var disp = new JsonObject();
                foreach (var l in group.OrderBy(l => l.Dof)) disp[Dofs[l.Dof - 1]] = R(l.Value * 1e3);
                loads.Add(new JsonObject { ["node"] = Tag(nodeTag, group.Key), ["displacement"] = disp });
            }
            foreach (var l in session.ElementLoads.Where(l => l.LoadCaseId == lc.Id))
            {
                if (AreaDirection(l) is not { } dir || !IsDocumentLoad(l)
                    || (l.TargetKind == FemLoadTargetKinds.Group && (l.GroupId is not int gid || !groupTag.ContainsKey(gid))))
                {
                    skipped++;
                    continue;
                }
                var lo = l.TargetKind == FemLoadTargetKinds.Group
                    ? new JsonObject { ["group"] = groupTag[l.GroupId!.Value] }
                    : new JsonObject { ["plates"] = new JsonArray(l.TargetTags.Select(t => (JsonNode)t).ToArray()) };
                lo["p"] = R(l.Values[0] / 1e3);
                if (dir != "z") lo["direction"] = dir;
                loads.Add(lo);
            }
            skipped += session.MeshNodeLoads.Count(l => l.LoadCaseId == lc.Id);
            if (!FemSp20Types.IsImperfection(lc)) o["loads"] = loads;
            else
            {
                var imps = new JsonArray();
                foreach (var imp in imperfections.Where(i => i.LoadCaseId == lc.Id))
                    imps.Add(ImperfectionToJson(imp, analyses));
                o["imperfections"] = imps;
            }
            cases.Add(o);
        }
        if (skipped > 0) warn(string.Format(Loc.S("CliFemMeshLoadsSkipped"), schema.Tag, skipped));

        var combinations = new JsonArray();
        foreach (var def in session.LoadDefinitions)
        {
            var e = def.GetExpression();
            var o = new JsonObject { ["tag"] = def.Tag };
            if ((def.CombinationType ?? e.CombinationType) is { } type) o["type"] = type;
            if (!string.IsNullOrWhiteSpace(def.Description)) o["comment"] = def.Description;
            if (e.Mode == FemLoadExpressionMode.All) o["all"] = true;
            else
            {
                var terms = new JsonArray();
                foreach (var id in e.LoadCaseIds)
                    terms.Add(new JsonObject { ["loadCase"] = caseTag.GetValueOrDefault(id, id.ToString()), ["k"] = 1 });
                foreach (var t in e.Terms)
                {
                    var to = new JsonObject { ["loadCase"] = caseTag.GetValueOrDefault(t.LoadCaseId, t.LoadCaseId.ToString()) };
                    if (!t.IsAuto) to["k"] = R(t.Coefficient);
                    if (t.Role != null) to["role"] = t.Role;
                    terms.Add(to);
                }
                o["terms"] = terms;
            }
            combinations.Add(o);
        }
        return (cases, combinations);
    }

    static JsonNode Tag(Dictionary<int, string> tags, int id) =>
        tags.TryGetValue(id, out var t) && int.TryParse(t, out int n) ? n : tags.GetValueOrDefault(id, id.ToString());

    static JsonArray Vec(double x, double y, double z) => [R(x), R(y), R(z)];

    /// <summary>Нагрузка на КЭ, которую описывает документ: ручная равномерная на КонЭ или группу.</summary>
    static bool IsDocumentLoad(FemElementLoad l) =>
        l.Origin == FemLoadOrigin.Manual && l.LoadKind == FemElementLoadKinds.Uniform && l.Values.Count == 1
        && l.TargetKind is FemLoadTargetKinds.Members or FemLoadTargetKinds.Group;

    /// <summary>«x», «y», «z» (общие оси) или «normal» (местная z); иное — null.</summary>
    static string? AreaDirection(FemElementLoad l) =>
        !l.IsLocal && l.AxisIndex >= 0 ? Dofs[l.AxisIndex] : l.IsLocal && l.AxisIndex == 2 ? "normal" : null;

    static JsonObject ImperfectionToJson(FemImperfection imp, IReadOnlyList<FemAnalysis> analyses)
    {
        object record = imp.Kind switch
        {
            FemImperfectionKinds.Sway => FemSwayParams.Parse(imp.ParamsJson),
            FemImperfectionKinds.Bow => FemBowParams.Parse(imp.ParamsJson),
            FemImperfectionKinds.WallBow => FemWallBowParams.Parse(imp.ParamsJson),
            _ => FemModesParams.Parse(imp.ParamsJson),
        };
        var p = JsonSerializer.SerializeToNode(record, record.GetType(), ImperfectionJson)!.AsObject();
        if (imp.Kind == FemImperfectionKinds.Modes)
        {
            int id = p["analysisId"]?.GetValue<int>() ?? 0;
            p.Remove("analysisId");
            p["analysis"] = analyses.FirstOrDefault(a => a.Id == id)?.Tag ?? id.ToString();
        }
        var o = new JsonObject { ["tag"] = imp.Tag, ["kind"] = imp.Kind };
        if (imp.Comment != null) o["comment"] = imp.Comment;
        o["params"] = p;
        return o;
    }

    // ── Разбор ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Ссылка тегом: строка или целое.</summary>
    static string? Ref(JsonObject o, string key, string path, ModelErrors errors, bool required = true)
    {
        if (o[key] is JsonValue v)
        {
            if (v.TryGetValue(out string? s) && s.Trim().Length > 0) return s.Trim();
            if (v.TryGetValue(out long l)) return l.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (o[key] is not null || required)
            errors.Add($"{path}.{key}", o[key] is null ? Loc.S("CliModelErrRequired") : Loc.S("CliModelErrBadValue"));
        return null;
    }

    /// <summary>Три конечных числа; нет поля — null.</summary>
    static double[]? Vec3(JsonObject o, string key, string path, ModelErrors errors)
    {
        if (o[key] is null) return null;
        if (o[key] is JsonArray a && a.Count == 3 && a.All(x => x is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d)))
            return [a[0]!.GetValue<double>(), a[1]!.GetValue<double>(), a[2]!.GetValue<double>()];
        errors.Add($"{path}.{key}", Loc.S("CliFemErrVector3"));
        return null;
    }

    static List<LoadCaseDoc>? ParseLoadCases(JsonNode? node, string path, ModelErrors errors)
    {
        if (node is null) return null;
        if (node is not JsonArray arr) { errors.Add($"{path}.loadCases", Loc.S("CliModelErrNotArray")); return null; }
        var list = new List<LoadCaseDoc>();
        for (int i = 0; i < arr.Count; i++)
        {
            string p = $"{path}.loadCases[{i}]";
            if (arr[i] is not JsonObject o) { errors.Add(p, Loc.S("CliModelErrNotObject")); continue; }
            Unknown(o, LoadCaseKeys, p, errors);
            string? tag = SectionJson.Str(o, "tag", p, errors, required: true);
            string type = SectionJson.Str(o, "type", p, errors) ?? FemSp20Types.ShortTerm;
            if (!LoadCaseTypes.Contains(type))
                errors.Add($"{p}.type", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", LoadCaseTypes)));
            bool imperfection = type == FemSp20Types.Imperfection;
            string? group = SectionJson.Str(o, "group", p, errors);
            double? gamma = SectionJson.Num(o, "gammaF", p, errors);
            if (gamma is <= 0) errors.Add($"{p}.gammaF", Loc.S("FieldErrPositive"));
            double? reduced = SectionJson.Num(o, "gammaFReduced", p, errors);
            if (reduced is <= 0) errors.Add($"{p}.gammaFReduced", Loc.S("FieldErrPositive"));
            if (reduced != null && type != FemSp20Types.Permanent) errors.Add($"{p}.gammaFReduced", Loc.S("CliFemErrOnlyPermanent"));
            double? share = SectionJson.Num(o, "longTermShare", p, errors);
            if (share is < 0 or > 1) errors.Add($"{p}.longTermShare", string.Format(Loc.S("CliFemErrRange"), "0", "1"));
            if (share != null && type != FemSp20Types.ShortTerm) errors.Add($"{p}.longTermShare", Loc.S("CliFemErrOnlyShortTerm"));
            double? self = SectionJson.Num(o, "selfWeight", p, errors);
            if (self == 0) errors.Add($"{p}.selfWeight", Loc.S("FieldErrNonZero"));
            if (imperfection && (gamma ?? self) != null) errors.Add(p, Loc.S("CliFemErrImperfectionCaseNoLoads"));

            List<LoadDoc>? loads = null;
            if (o["loads"] is JsonArray la)
            {
                if (imperfection) errors.Add($"{p}.loads", Loc.S("CliFemErrImperfectionCaseNoLoads"));
                loads = [];
                for (int k = 0; k < la.Count; k++)
                    if (ParseLoad(la[k], $"{p}.loads[{k}]", errors) is { } ld) loads.Add(ld);
            }
            else if (o["loads"] is not null) errors.Add($"{p}.loads", Loc.S("CliModelErrNotArray"));

            List<ImperfectionDoc>? imps = null;
            if (o["imperfections"] is JsonArray ia)
            {
                if (!imperfection) errors.Add($"{p}.imperfections", Loc.S("CliFemErrImperfectionsNeedType"));
                imps = [];
                for (int k = 0; k < ia.Count; k++)
                {
                    string ip = $"{p}.imperfections[{k}]";
                    if (ia[k] is not JsonObject io) { errors.Add(ip, Loc.S("CliModelErrNotObject")); continue; }
                    Unknown(io, ImperfectionKeys, ip, errors);
                    string? itag = SectionJson.Str(io, "tag", ip, errors, required: true);
                    string? kind = SectionJson.Str(io, "kind", ip, errors, required: true);
                    if (kind != null && !ImperfectionKinds.Contains(kind))
                        errors.Add($"{ip}.kind", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", ImperfectionKinds)));
                    string? comment = SectionJson.Str(io, "comment", ip, errors);
                    var prm = io["params"] as JsonObject;
                    if (io["params"] is not null && prm == null) errors.Add($"{ip}.params", Loc.S("CliModelErrNotObject"));
                    prm = (JsonObject?)prm?.DeepClone() ?? [];
                    string? analysis = null;
                    if (kind == FemImperfectionKinds.Modes)
                    {
                        analysis = Ref(prm, "analysis", $"{ip}.params", errors);
                        prm.Remove("analysis");
                    }
                    if (itag != null && kind != null) imps.Add(new ImperfectionDoc(ip, itag, kind, comment, prm, analysis));
                }
            }
            else if (o["imperfections"] is not null) errors.Add($"{p}.imperfections", Loc.S("CliModelErrNotArray"));

            if (tag != null) list.Add(new LoadCaseDoc(p, tag, type, group, gamma, reduced, share, self, loads, imps));
        }
        return list;
    }

    static LoadDoc? ParseLoad(JsonNode? node, string p, ModelErrors errors)
    {
        if (node is not JsonObject o) { errors.Add(p, Loc.S("CliModelErrNotObject")); return null; }
        int targets = new[] { "node", "member", "plates", "group" }.Count(k => o[k] is not null);
        if (targets != 1) { errors.Add(p, Loc.S("CliFemErrLoadTarget")); return null; }
        int before = errors.Count;
        if (o["node"] is not null && o["displacement"] is not null)
        {
            Unknown(o, KinematicKeys, p, errors);
            string? n = Ref(o, "node", p, errors);
            var values = new List<(int, double, string)>();
            if (o["displacement"] is JsonObject d && d.Count > 0)
            {
                foreach (var (key, _) in d)
                {
                    int dof = Array.IndexOf(Dofs, key);
                    if (dof < 0) dof = Array.IndexOf(LegacyDofs, key);
                    if (dof < 0) { errors.Add($"{p}.displacement.{key}", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", Dofs))); continue; }
                    if (SectionJson.Num(d, key, $"{p}.displacement", errors) is double v) values.Add((dof + 1, v / 1e3, $"{p}.displacement.{key}"));
                }
            }
            else errors.Add($"{p}.displacement", Loc.S("CliFemErrDisplacementEmpty"));
            return errors.Count == before && n != null ? new KinematicDoc(p, n, values) : null;
        }
        if (o["node"] is not null)
        {
            Unknown(o, NodeLoadKeys, p, errors);
            string? n = Ref(o, "node", p, errors);
            var f = Vec3(o, "f", p, errors);
            var m = Vec3(o, "m", p, errors);
            if (o["f"] is null && o["m"] is null) errors.Add(p, Loc.S("CliFemErrNodeLoadEmpty"));
            return errors.Count == before && n != null
                ? new NodeLoadDoc(p, n, (f ?? [0, 0, 0]).Select(v => v * 1e3).ToArray(), (m ?? [0, 0, 0]).Select(v => v * 1e3).ToArray())
                : null;
        }
        if (o["member"] is not null)
        {
            Unknown(o, MemberLoadKeys, p, errors);
            string? member = Ref(o, "member", p, errors);
            string axes = SectionJson.Str(o, "axes", p, errors) ?? "local";
            if (axes is not ("local" or "global")) errors.Add($"{p}.axes", string.Format(Loc.S("CliModelErrOneOf"), "local, global"));
            var q = Vec3(o, "q", p, errors);
            var q2 = Vec3(o, "q2", p, errors);
            var point = Vec3(o, "point", p, errors);
            var m = Vec3(o, "m", p, errors);
            double a = SectionJson.Num(o, "a", p, errors) ?? 0;
            double b = SectionJson.Num(o, "b", p, errors) ?? 0;
            if (a < 0) errors.Add($"{p}.a", Loc.S("FieldErrNonNegative"));
            if (b < 0) errors.Add($"{p}.b", Loc.S("FieldErrNonNegative"));
            if ((o["q"] is null) == (o["point"] is null)) errors.Add(p, Loc.S("CliFemErrMemberLoadKind"));
            if (o["point"] is not null && (o["q2"] is not null || o["b"] is not null)) errors.Add(p, Loc.S("CliFemErrPointLoadFields"));
            if (o["q"] is not null && o["m"] is not null) errors.Add($"{p}.m", Loc.S("CliFemErrMomentOnlyPoint"));
            if (errors.Count > before || member == null) return null;
            string dist = point != null ? "point" : q2 != null ? "trapezoidal" : "uniform";
            var start = (point ?? q!).Select(v => v * 1e3).ToArray();
            var end = dist == "point" ? [0, 0, 0] : (q2 ?? q!).Select(v => v * 1e3).ToArray();
            return new MemberLoadDoc(p, member, axes, dist, start, end, (m ?? [0, 0, 0]).Select(v => v * 1e3).ToArray(), a, b);
        }
        Unknown(o, AreaLoadKeys, p, errors);
        List<string>? plates = null;
        if (o["plates"] is JsonArray pa && pa.Count > 0)
        {
            plates = [];
            for (int k = 0; k < pa.Count; k++)
            {
                if (pa[k] is JsonValue pv && pv.TryGetValue(out string? s) && s.Trim().Length > 0) plates.Add(s.Trim());
                else if (pa[k] is JsonValue pl && pl.TryGetValue(out long l)) plates.Add(l.ToString(System.Globalization.CultureInfo.InvariantCulture));
                else errors.Add($"{p}.plates[{k}]", Loc.S("CliModelErrBadValue"));
            }
        }
        else if (o["plates"] is not null) errors.Add($"{p}.plates", Loc.S("CliFemErrPlatesEmpty"));
        string? group = o["group"] is not null ? Ref(o, "group", p, errors) : null;
        double? kpa = SectionJson.Num(o, "p", p, errors, required: true);
        if (kpa == 0) errors.Add($"{p}.p", Loc.S("FieldErrNonZero"));
        string dir = SectionJson.Str(o, "direction", p, errors) ?? "z";
        if (!Directions.Contains(dir)) errors.Add($"{p}.direction", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", Directions)));
        return errors.Count == before && kpa is double v2 ? new AreaLoadDoc(p, plates, group, v2, dir) : null;
    }

    static List<CombinationDoc>? ParseCombinations(JsonNode? node, string path, ModelErrors errors)
    {
        if (node is null) return null;
        if (node is not JsonArray arr) { errors.Add($"{path}.combinations", Loc.S("CliModelErrNotArray")); return null; }
        var list = new List<CombinationDoc>();
        for (int i = 0; i < arr.Count; i++)
        {
            string p = $"{path}.combinations[{i}]";
            if (arr[i] is not JsonObject o) { errors.Add(p, Loc.S("CliModelErrNotObject")); continue; }
            Unknown(o, CombinationKeys, p, errors);
            string? tag = SectionJson.Str(o, "tag", p, errors, required: true);
            string? type = SectionJson.Str(o, "type", p, errors);
            string? comment = SectionJson.Str(o, "comment", p, errors);
            bool all = SectionJson.Bool(o, "all", p, errors) ?? false;
            var terms = new List<TermDoc>();
            if (o["terms"] is JsonArray ta)
            {
                if (all) errors.Add($"{p}.terms", Loc.S("CliFemErrAllWithTerms"));
                for (int k = 0; k < ta.Count; k++)
                {
                    string tp = $"{p}.terms[{k}]";
                    if (ta[k] is not JsonObject to) { errors.Add(tp, Loc.S("CliModelErrNotObject")); continue; }
                    Unknown(to, TermKeys, tp, errors);
                    string? lc = Ref(to, "loadCase", tp, errors);
                    double? kk = SectionJson.Num(to, "k", tp, errors);
                    string? role = SectionJson.Str(to, "role", tp, errors);
                    if (role != null && !Roles.Contains(role))
                        errors.Add($"{tp}.role", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", Roles)));
                    if (kk == null && role == null) errors.Add(tp, Loc.S("CliFemErrTermEmpty"));
                    if (lc != null) terms.Add(new TermDoc(tp, lc, kk, role));
                }
            }
            else if (o["terms"] is not null) errors.Add($"{p}.terms", Loc.S("CliModelErrNotArray"));
            else if (!all) errors.Add($"{p}.terms", Loc.S("CliModelErrRequired"));
            if (tag != null) list.Add(new CombinationDoc(p, tag, type, comment, all, terms));
        }
        return list;
    }

    // ── Сборка ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Загружения, нагрузки и комбинации по документу поверх сессии. Указанный <c>loadCases</c> заменяет набор
    /// загружений (удалить загружение постановки нельзя; входящее в комбинации — только вместе с <c>combinations</c>),
    /// указанный <c>loads</c> заменяет нагрузки загружения из документа, указанный <c>combinations</c> — набор
    /// комбинаций. Возвращает, сколько загружений и комбинаций удалено.
    /// </summary>
    static (int Cases, int Combinations) BuildLoads(HeadlessProjectContext ctx, SchemaDoc d, FemSchemaEditSession session,
        FemSchema schema, ModelErrors errors)
    {
        var db = ctx.Db;
        int casesRemoved = 0, combinationsRemoved = 0;
        if (d.LoadCases != null)
        {
            var keep = d.LoadCases.Select(c => c.Tag).ToHashSet(StringComparer.Ordinal);
            foreach (var gone in session.LoadCases.Where(c => !keep.Contains(c.Tag)).ToList())
            {
                var impact = FemLoadAuthoring.DeleteImpact(db, session, gone);
                if (impact.Blocked) { errors.Add($"{d.Path}.loadCases", FemLoadAuthoring.BlockedText(gone, impact)); continue; }
                if (d.Combinations == null && impact.Combinations.Count > 0)
                {
                    errors.Add($"{d.Path}.loadCases", string.Format(Loc.S("CliFemErrLoadCaseInCombinations"), gone.Tag,
                        string.Join(", ", impact.Combinations.Select(t => $"«{t}»"))));
                    continue;
                }
                session.Execute(new DeleteLoadCaseCommand(gone));
                casesRemoved++;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in d.LoadCases)
            {
                if (!seen.Add(c.Tag)) { errors.Add($"{c.Path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), c.Tag)); continue; }
                var lc = session.LoadCases.FirstOrDefault(x => x.Tag == c.Tag);
                if (lc == null)
                {
                    lc = new FemLoadCase { SchemaId = schema.Id, Tag = c.Tag };
                    session.Execute(new AddLoadCaseCommand(lc));
                }
                lc.Sp20Type = c.Type;
                lc.Sp20Group = c.Group;
                lc.GammaFUnfav = c.GammaF;
                lc.GammaFFav = c.GammaFReduced;
                lc.LongTermShare = c.Share;
                lc.SelfWeightFactor = c.SelfWeight;
                if (c.Loads != null) BuildCaseLoads(c, lc, session, errors);
                foreach (var imp in c.Imperfections ?? [])
                {
                    if (ImperfectionFromDoc(ctx, imp, schema, lc, errors) is not { } item) continue;
                    foreach (var e in FemLoadAuthoring.ImperfectionErrors(db, item)) errors.Add($"{imp.Path}.params", e);
                }
                if (c.Imperfections != null)
                    foreach (var dup in c.Imperfections.GroupBy(i => i.Tag, StringComparer.CurrentCultureIgnoreCase).Where(g => g.Count() > 1))
                        errors.Add($"{dup.Last().Path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), dup.Key));
            }
        }

        if (d.Combinations != null)
        {
            var keep = d.Combinations.Select(c => c.Tag).ToHashSet(StringComparer.Ordinal);
            foreach (var gone in session.LoadDefinitions.Where(x => !keep.Contains(x.Tag)).ToList())
            {
                session.Execute(new DeleteLoadDefinitionCommand(gone));
                combinationsRemoved++;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in d.Combinations)
            {
                if (!seen.Add(c.Tag)) { errors.Add($"{c.Path}.tag", string.Format(Loc.S("CliModelErrDuplicateKey"), c.Tag)); continue; }
                var terms = new List<FemLoadTerm>();
                foreach (var t in c.Terms)
                {
                    if (session.LoadCases.FirstOrDefault(x => x.Tag == t.LoadCase) is not { } lc)
                    {
                        errors.Add($"{t.Path}.loadCase", string.Format(Loc.S("CliFemErrUnknownLoadCase"), t.LoadCase));
                        continue;
                    }
                    if (terms.Any(x => x.LoadCaseId == lc.Id))
                    {
                        errors.Add($"{t.Path}.loadCase", string.Format(Loc.S("CliModelErrDuplicateKey"), t.LoadCase));
                        continue;
                    }
                    terms.Add(t.K is double k
                        ? new FemLoadTerm { LoadCaseId = lc.Id, Coefficient = k, Role = t.Role, Mode = t.Role != null ? FemLoadTerm.ManualMode : null }
                        : new FemLoadTerm { LoadCaseId = lc.Id, Coefficient = 1, Role = t.Role, Mode = FemLoadTerm.AutoMode });
                }
                var expression = c.All
                    ? new FemLoadExpression { Mode = FemLoadExpressionMode.All, CombinationType = c.Type }
                    : FemCombinationCoefficients.Recompute(new FemLoadExpression
                    {
                        Mode = terms.Count == 1 ? FemLoadExpressionMode.Single : FemLoadExpressionMode.Sum,
                        Terms = terms, CombinationType = c.Type,
                    }, session.LoadCases, c.Type, ctx.CalcSettings.ToSp20LoadValueKind(), ctx.CalcSettings.ToSp20GammaDefaults());
                var def = session.LoadDefinitions.FirstOrDefault(x => x.Tag == c.Tag);
                if (def == null)
                {
                    def = new FemLoadDefinition { SchemaId = schema.Id, Tag = c.Tag, SourceKind = "manual" };
                    session.Execute(new AddLoadDefinitionCommand(def));
                }
                def.CombinationType = c.Type;
                def.Description = c.Comment;
                def.SetExpression(expression);
            }
        }
        // Правка γf и доли длительности меняет «авто»-коэффициенты всех комбинаций (как в окне загружения).
        if (d.LoadCases != null)
            new RecomputeAutoTermsCommand(ctx.CalcSettings.ToSp20GammaDefaults(), ctx.CalcSettings.ToSp20LoadValueKind()).Do(session);
        return (casesRemoved, combinationsRemoved);
    }

    /// <summary>Нагрузки загружения по документу (полная замена описываемых документом).</summary>
    static void BuildCaseLoads(LoadCaseDoc c, FemLoadCase lc, FemSchemaEditSession session, ModelErrors errors)
    {
        session.NodeLoads.RemoveAll(l => l.LoadCaseId == lc.Id);
        session.MemberLoads.RemoveAll(l => l.LoadCaseId == lc.Id);
        session.KinematicLoads.RemoveAll(l => l.LoadCaseId == lc.Id);
        session.ElementLoads.RemoveAll(l => l.LoadCaseId == lc.Id && IsDocumentLoad(l) && AreaDirection(l) != null);
        var schemaId = session.Schema.Id;
        var nodeLoaded = new HashSet<int>();
        var kinematic = new HashSet<(int, int)>();
        foreach (var load in c.Loads!)
        {
            switch (load)
            {
                case NodeLoadDoc n:
                {
                    if (NodeFor(session, n.Node, $"{n.Path}.node", errors) is not { } node) break;
                    if (!nodeLoaded.Add(node.Id)) { errors.Add($"{n.Path}.node", string.Format(Loc.S("CliFemErrNodeLoadTwice"), n.Node)); break; }
                    session.NodeLoads.Add(new FemNodeLoad
                    {
                        SchemaId = schemaId, LoadCaseId = lc.Id, NodeId = node.Id,
                        Fx = n.F[0], Fy = n.F[1], Fz = n.F[2], Mx = n.M[0], My = n.M[1], Mz = n.M[2],
                    });
                    break;
                }
                case KinematicDoc k:
                {
                    if (NodeFor(session, k.Node, $"{k.Path}.node", errors) is not { } node) break;
                    foreach (var (dof, value, path) in k.Values)
                    {
                        if (!kinematic.Add((node.Id, dof))) { errors.Add(path, string.Format(Loc.S("CliModelErrDuplicateKey"), Dofs[dof - 1])); continue; }
                        if ((node.DofMask & (1 << (dof - 1))) != 0 && value != 0) { errors.Add(path, Loc.S("CliFemErrKinematicFixed")); continue; }
                        session.KinematicLoads.Add(new FemKinematicLoad { SchemaId = schemaId, LoadCaseId = lc.Id, NodeId = node.Id, Dof = dof, Value = value });
                    }
                    break;
                }
                case MemberLoadDoc m:
                {
                    var member = session.Members.FirstOrDefault(x => x.ElemTag == m.Member);
                    if (member == null) { errors.Add($"{m.Path}.member", string.Format(Loc.S("CliFemErrUnknownMember"), m.Member)); break; }
                    if (member.ElemType == "shell") { errors.Add($"{m.Path}.member", string.Format(Loc.S("CliFemErrMemberLoadOnShell"), m.Member)); break; }
                    var l = new FemMemberLoad
                    {
                        SchemaId = schemaId, LoadCaseId = lc.Id, MemberId = member.Id, CoordinateSystem = m.Axes,
                        DistributionType = m.Distribution, StartOffsetM = m.A, EndOffsetM = m.B,
                        QxStart = m.Q[0], QyStart = m.Q[1], QzStart = m.Q[2], QxEnd = m.Q2[0], QyEnd = m.Q2[1], QzEnd = m.Q2[2],
                        Mx = m.M[0], My = m.M[1], Mz = m.M[2],
                    };
                    if (FemLoadAuthoring.MemberLoadOffsetError(session, l) is { } error) { errors.Add($"{m.Path}.a", error); break; }
                    session.MemberLoads.Add(l);
                    break;
                }
                case AreaLoadDoc a:
                {
                    var l = new FemElementLoad
                    {
                        SchemaId = schemaId, LoadCaseId = lc.Id, LoadKind = FemElementLoadKinds.Uniform,
                        CoordinateSystem = a.Direction == "normal" ? "local" : "global", Axis = a.Direction == "normal" ? "z" : a.Direction,
                    };
                    l.SetValues([a.Kpa * 1e3]);
                    if (a.Group != null)
                    {
                        if (session.MemberGroups.FirstOrDefault(g => g.Tag == a.Group) is not { } group)
                        {
                            errors.Add($"{a.Path}.group", string.Format(Loc.S("CliFemErrUnknownGroup"), a.Group));
                            break;
                        }
                        l.TargetKind = FemLoadTargetKinds.Group;
                        l.GroupId = group.Id;
                    }
                    else
                    {
                        for (int k = 0; k < a.Plates!.Count; k++)
                        {
                            var member = session.Members.FirstOrDefault(x => x.ElemTag == a.Plates[k]);
                            if (member == null) errors.Add($"{a.Path}.plates[{k}]", string.Format(Loc.S("CliFemErrUnknownMember"), a.Plates[k]));
                            else if (member.ElemType != "shell") errors.Add($"{a.Path}.plates[{k}]", string.Format(Loc.S("CliFemErrNotPlate"), a.Plates[k]));
                        }
                        l.TargetKind = FemLoadTargetKinds.Members;
                        l.SetTargetTags(a.Plates);
                    }
                    session.ElementLoads.Add(l);
                    break;
                }
            }
        }
    }

    static FemNode? NodeFor(FemSchemaEditSession session, string tag, string path, ModelErrors errors)
    {
        var node = session.Nodes.FirstOrDefault(n => n.NodeTag == tag);
        if (node == null) errors.Add(path, string.Format(Loc.S("CliFemErrUnknownNode"), tag));
        return node;
    }

    /// <summary>Элемент несовершенства из документа (параметры — запись вида, постановка форм — по имени).</summary>
    static FemImperfection? ImperfectionFromDoc(HeadlessProjectContext ctx, ImperfectionDoc doc, FemSchema schema,
        FemLoadCase lc, ModelErrors errors)
    {
        var prm = (JsonObject)doc.Params.DeepClone();
        if (doc.Kind == FemImperfectionKinds.Modes)
        {
            var found = schema.Id > 0 ? ctx.Db.GetFemAnalyses(schema.Id).Where(a => a.Tag == doc.Analysis && a.Kind == "csfea_buckling").ToList() : [];
            if (found.Count != 1)
            {
                errors.Add($"{doc.Path}.params.analysis", string.Format(Loc.S("CliFemErrUnknownBucklingAnalysis"), doc.Analysis));
                return null;
            }
            prm["analysisId"] = found[0].Id;
        }
        string json;
        try
        {
            Type type = doc.Kind switch
            {
                FemImperfectionKinds.Sway => typeof(FemSwayParams),
                FemImperfectionKinds.Bow => typeof(FemBowParams),
                FemImperfectionKinds.WallBow => typeof(FemWallBowParams),
                _ => typeof(FemModesParams),
            };
            var record = prm.Deserialize(type, ImperfectionJson)!;
            json = (string)type.GetMethod("ToJson")!.Invoke(record, null)!;
        }
        catch (JsonException ex)
        {
            errors.Add($"{doc.Path}.params", string.Format(Loc.S("CliFemErrImperfectionParams"), ex.Path ?? "", ex.Message));
            return null;
        }
        return new FemImperfection
        {
            SchemaId = schema.Id, LoadCaseId = lc.Id, Kind = doc.Kind, Tag = doc.Tag, Comment = doc.Comment, ParamsJson = json,
        };
    }

    /// <summary>
    /// После записи схемы: элементы несовершенства загружений, у которых в документе указан <c>imperfections</c>, —
    /// замена (совпавшие по имени — на месте). Возвращает число удалённых.
    /// </summary>
    static int CommitImperfections(HeadlessProjectContext ctx, SchemaDoc d, FemSchemaEditSession session, FemSchema schema)
    {
        var db = ctx.Db;
        int removed = 0;
        foreach (var c in d.LoadCases ?? [])
        {
            if (c.Imperfections == null || session.LoadCases.FirstOrDefault(x => x.Tag == c.Tag) is not { } lc) continue;
            var existing = db.GetFemImperfections(schema.Id).Where(i => i.LoadCaseId == lc.Id).ToList();
            var keep = c.Imperfections.Select(i => i.Tag).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            foreach (var gone in existing.Where(i => !keep.Contains(i.Tag)))
            {
                db.DeleteFemImperfection(gone);
                removed++;
            }
            foreach (var doc in c.Imperfections)
            {
                var errors = new ModelErrors();
                if (ImperfectionFromDoc(ctx, doc, schema, lc, errors) is not { } item) { errors.ThrowIfAny(); continue; }
                item.Id = existing.FirstOrDefault(i => string.Equals(i.Tag, doc.Tag, StringComparison.CurrentCultureIgnoreCase))?.Id ?? 0;
                if (FemLoadAuthoring.SaveImperfection(db, item) is { } error)
                    throw new CliException(CliExitCode.Usage, $"{doc.Path}: {error}");
            }
        }
        return removed;
    }
}
