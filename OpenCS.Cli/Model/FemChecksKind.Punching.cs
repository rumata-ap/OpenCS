using System.Globalization;
using System.Text.Json.Nodes;
using CScore;
using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Вид <c>punching</c> раздела «femChecks» — проверка продавливания по МКЭ (<see cref="PunchingFemCheckParams"/>).
/// Источник усилий — постановка CSfea схемы (<c>{"analysis": имя}</c>, линейная или секущая) или приложенный к схеме
/// снимок результатов (<c>{"snapshot": "lira" | "scad"}</c>). Параметры — как в окне, единицы окна: мм, МПа, градусы.
/// Неуказанные поля не меняются; <c>punching.nodes</c> — только отличия узлов от найденного (как переопределения
/// окна), массив заменяет состав, без массива — не трогается. Собственная арматура узла — общая арматура проверки с
/// диаметром, числом, шагом и рядами узла (как столбцы таблицы окна).
/// </summary>
sealed partial class FemChecksKind
{
    static readonly string[] SourceKeys = ["analysis", "snapshot"];
    static readonly string[] PunchingKeys =
    [
        "method", "momentsAreMloc", "concreteFromSchema", "concrete", "rbtMpa", "defaultAxMm", "defaultAyMm", "sw", "options",
        "angleDeg", "edgeInfluence", "wallRatio", "liraContour", "pile", "nodes",
    ];
    static readonly string[] SwKeys = ["material", "rswMpa", "dMm", "layout", "count", "stepMm", "zoneMm", "rows", "crossX", "crossY"];
    static readonly string[] SwRowsKeys = ["firstMm", "stepMm", "count"];
    static readonly string[] CrossKeys = ["lines", "stepMm", "positive", "negative"];
    static readonly string[] OptionKeys = ["eccentricityX", "eccentricityY", "wb", "pylon", "pylonReduced", "wallFigure", "holeInfluence"];
    static readonly string[] PileKeys = ["shape", "sizeMm"];
    static readonly string[] NodeKeys = ["node", "include", "axMm", "ayMm", "capital", "contour", "pile", "sw"];
    static readonly string[] NodeSwKeys = ["dMm", "count", "stepMm", "rowCount"];
    static readonly string[] NodeCapitalKeys = ["bxMm", "byMm", "hcMm"];
    static readonly (string Json, string Value)[] Snapshots = [("lira", FragmentMaskSet.SourceLira), ("scad", FragmentMaskSet.SourceScad)];

    /// <summary>Мм/МПа документа ↔ м/кПа базы; округление — чтобы кругооборот давал те же числа.</summary>
    static double Mm(double m) => R(m * 1000);
    static double Mpa(double kpa) => R(kpa / 1000);

    // ── Выгрузка ────────────────────────────────────────────────────────────────────────────

    JsonObject? PunchingToJson(FemCheck c, Refs refs, Action<string> warn)
    {
        string schemaTag = refs.Schema.Tag.Trim();
        var p = PunchingFemCheckParams.Parse(c.ParamsJson);
        JsonObject source;
        if (p.IsCsfea)
        {
            var a = int.TryParse(p.SourceRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                ? refs.Analyses.FirstOrDefault(x => x.Id == id) : null;
            if (a == null || refs.AnalysesByTag(a.Tag.Trim()).Count != 1)
            {
                warn(string.Format(Loc.S("CliFemCheckSourceSkipped"), schemaTag, c.Tag.Trim()));
                return null;
            }
            source = new JsonObject { ["analysis"] = a.Tag.Trim() };
        }
        else source = new JsonObject { ["snapshot"] = Snapshots.FirstOrDefault(s => s.Value == p.SourceKind).Json ?? p.SourceKind };

        var o = new JsonObject
        {
            ["schema"] = schemaTag, ["tag"] = c.Tag.Trim(), ["kind"] = "punching", ["source"] = source,
            ["punching"] = PunchingJson(p, refs, schemaTag, c.Tag.Trim(), warn),
        };
        return o;
    }

    static JsonObject PunchingJson(PunchingFemCheckParams p, Refs refs, string schemaTag, string tag, Action<string> warn)
    {
        string? MaterialTag(int id)
        {
            if (id == 0) return null;
            if (refs.Materials.FirstOrDefault(m => m.Id == id) is { } m) return m.Tag.Trim();
            warn(string.Format(Loc.S("CliFemCheckMaterialMissing"), schemaTag, tag, id));
            return null;
        }
        var r = p.Reinforcement;
        var o = new JsonObject
        {
            ["method"] = p.Method,
            ["momentsAreMloc"] = p.MomentsAreMloc,
            ["concreteFromSchema"] = p.ConcreteFromSchema,
            ["concrete"] = MaterialTag(p.ConcreteMaterialId),
            ["rbtMpa"] = p.RbtOverride is { } rbt ? Mpa(rbt) : null,
            ["defaultAxMm"] = Mm(p.DefaultAx),
            ["defaultAyMm"] = Mm(p.DefaultAy),
            ["sw"] = r.Enabled ? SwJson(r, MaterialTag(r.MaterialId)) : null,
            ["options"] = new JsonObject
            {
                ["eccentricityX"] = p.Options.EccentricityX, ["eccentricityY"] = p.Options.EccentricityY,
                ["wb"] = p.Options.Wb, ["pylon"] = p.Options.Pylon, ["pylonReduced"] = p.Options.PylonReduced,
                ["wallFigure"] = p.Options.WallFigure, ["holeInfluence"] = R(p.Options.HoleInfluence),
            },
            ["angleDeg"] = R(p.AngleDeg),
            ["edgeInfluence"] = R(p.EdgeInfluence),
            ["wallRatio"] = R(p.WallRatio),
            ["liraContour"] = p.LiraContour,
            ["pile"] = new JsonObject { ["shape"] = p.PileShape, ["sizeMm"] = Mm(p.PileSize) },
        };
        var nodes = new JsonArray();
        foreach (var n in p.Overrides) nodes.Add(NodeJson(n));
        o["nodes"] = nodes;
        return o;
    }

    static JsonObject SwJson(PunchingReinforcementParams r, string? material) => new()
    {
        ["material"] = material,
        ["rswMpa"] = r.RswOverride is { } rsw ? Mpa(rsw) : null,
        ["dMm"] = Mm(r.Diameter),
        ["layout"] = r.Layout,
        ["count"] = r.Count,
        ["stepMm"] = Mm(r.Step),
        ["zoneMm"] = r.ZoneExtent is { } z ? Mm(z) : null,
        ["rows"] = r.FirstRow is { } s1
            ? new JsonObject { ["firstMm"] = Mm(s1), ["stepMm"] = Mm(r.RowStep), ["count"] = r.RowCount } : null,
        ["crossX"] = CrossJson(r.CrossX),
        ["crossY"] = CrossJson(r.CrossY),
    };

    static JsonObject CrossJson(PunchingCrossArmParams a) => new()
    {
        ["lines"] = a.Lines, ["stepMm"] = Mm(a.Step), ["positive"] = a.Positive, ["negative"] = a.Negative,
    };

    static JsonObject NodeJson(PunchingFemJointOverride n)
    {
        var o = new JsonObject { ["node"] = n.Node };
        if (n.Excluded) o["include"] = false;
        if (n.Ax is { } ax) o["axMm"] = Mm(ax);
        if (n.Ay is { } ay) o["ayMm"] = Mm(ay);
        if (n.Capital == "none") o["capital"] = "none";
        else if (n.Capital == "manual" && n.CapitalManual is { } cm)
            o["capital"] = new JsonObject { ["bxMm"] = Mm(cm.Bx), ["byMm"] = Mm(cm.By), ["hcMm"] = Mm(cm.Hc) };
        if (n.Contour != null) o["contour"] = n.Contour;
        if (n.PileShape != null || n.PileSize != null)
        {
            var pile = new JsonObject();
            if (n.PileShape != null) pile["shape"] = n.PileShape;
            if (n.PileSize is { } ps) pile["sizeMm"] = Mm(ps);
            o["pile"] = pile;
        }
        if (n.Reinforcement is { Enabled: true } r)
            o["sw"] = new JsonObject { ["dMm"] = Mm(r.Diameter), ["count"] = r.Count, ["stepMm"] = Mm(r.Step), ["rowCount"] = r.RowCount };
        return o;
    }

    // ── Разбор ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Проверка продавливания по элементу документа поверх <paramref name="existing"/> (продавливания) или умолчаний окна
    /// новой проверки (первый бетон и первая арматура проекта). null — ошибки.
    /// </summary>
    static FemCheck? BuildPunching(JsonObject item, string tag, Refs refs, FemCheck? existing, string path, ModelErrors errors)
    {
        int before = errors.Count;
        var c = new FemCheck
        {
            Id = existing?.Id ?? 0, SchemaId = refs.Schema.Id, Tag = tag, NormCode = PunchingFemCheckParams.NormCode,
            ResultId = existing?.ResultId, MemberId = 0, ElementId = null, ForceSetIdsJson = "[]",
        };
        var p = existing != null
            ? PunchingFemCheckParams.Parse(existing.ParamsJson)
            : new PunchingFemCheckParams
            {
                SourceRef = "",
                ConcreteMaterialId = refs.Materials.FirstOrDefault(m => m.Type == MatType.Concrete)?.Id ?? 0,
                Reinforcement = new PunchingReinforcementParams
                {
                    MaterialId = refs.Materials.FirstOrDefault(m => m.Type is MatType.ReSteelF or MatType.ReSteelU)?.Id ?? 0,
                },
            };

        if (item["source"] is JsonObject so) p = ReadSource(so, p, refs, $"{path}.source", errors);
        else if (item["source"] is not null) errors.Add($"{path}.source", Loc.S("CliModelErrNotObject"));
        else if (existing == null) errors.Add($"{path}.source", Loc.S("CliModelErrRequired"));

        if (item["punching"] is JsonObject po) p = ReadPunching(po, p, refs, $"{path}.punching", errors);
        else if (item["punching"] is not null) errors.Add($"{path}.punching", Loc.S("CliModelErrNotObject"));

        c.ParamsJson = p.ToJson();
        return errors.Count > before ? null : c;
    }

    static PunchingFemCheckParams ReadSource(JsonObject o, PunchingFemCheckParams p, Refs refs, string path, ModelErrors errors)
    {
        Unknown(o, SourceKeys, path, errors);
        string? analysis = SectionJson.Str(o, "analysis", path, errors), snapshot = SectionJson.Str(o, "snapshot", path, errors);
        if ((analysis == null) == (snapshot == null))
        {
            errors.Add(path, Loc.S("CliFemCheckErrPunchingSource"));
            return p;
        }
        if (snapshot != null)
        {
            var found = Snapshots.FirstOrDefault(s => s.Json == snapshot);
            if (found.Json == null)
            {
                errors.Add($"{path}.snapshot", string.Format(Loc.S("CliModelErrOneOf"), "lira, scad"));
                return p;
            }
            return p with { SourceKind = found.Value, SourceRef = "" };
        }
        var list = refs.AnalysesByTag(analysis!);
        if (list.Count != 1)
        {
            errors.Add($"{path}.analysis", list.Count == 0
                ? string.Format(Loc.S("CliFemCheckErrUnknownAnalysis"), analysis)
                : string.Format(Loc.S("CliModelErrAmbiguousKey"), list.Count, analysis));
            return p;
        }
        var a = list[0];
        if (a.Kind is not (FemCsfeaRunner.LinearAnalysisKind or FemCsfeaRunner.AnalysisKind))
        {
            errors.Add($"{path}.analysis", string.Format(Loc.S("CliFemCheckErrPunchingAnalysisKind"), analysis));
            return p;
        }
        return p with
        {
            SourceKind = a.Kind == FemCsfeaRunner.AnalysisKind ? FragmentMaskSet.SourceCsfeaSecant : FragmentMaskSet.SourceCsfeaLinear,
            SourceRef = a.Id.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Материал по имени (уникальному) с нужным типом; ошибка — с путём.</summary>
    static int? MaterialId(Refs refs, string tag, Func<Material, bool> ofType, string path, ModelErrors errors)
    {
        var found = refs.Materials.Where(m => string.Equals(m.Tag.Trim(), tag, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (found.Count == 1 && ofType(found[0])) return found[0].Id;
        errors.Add(path, found.Count switch
        {
            0 => string.Format(Loc.S("CliSectionErrUnknownMaterial"), tag),
            1 => string.Format(Loc.S("CliFemCheckErrMaterialType"), tag),
            _ => string.Format(Loc.S("CliModelErrAmbiguousKey"), found.Count, tag),
        });
        return null;
    }

    static string PickStr(JsonObject o, string key, string[] allowed, string old, string path, ModelErrors errors)
    {
        if (SectionJson.Str(o, key, path, errors) is not { } s) return old;
        if (allowed.Contains(s)) return s;
        errors.Add($"{path}.{key}", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", allowed)));
        return old;
    }

    static PunchingFemCheckParams ReadPunching(JsonObject o, PunchingFemCheckParams p, Refs refs, string path, ModelErrors errors)
    {
        Unknown(o, PunchingKeys, path, errors);
        p = p with
        {
            Method = PickStr(o, "method", ["auto", "column", "contour"], p.Method, path, errors),
            MomentsAreMloc = SectionJson.Bool(o, "momentsAreMloc", path, errors) ?? p.MomentsAreMloc,
            ConcreteFromSchema = SectionJson.Bool(o, "concreteFromSchema", path, errors) ?? p.ConcreteFromSchema,
            DefaultAx = SectionJson.Num(o, "defaultAxMm", path, errors) is { } ax ? ax / 1000 : p.DefaultAx,
            DefaultAy = SectionJson.Num(o, "defaultAyMm", path, errors) is { } ay ? ay / 1000 : p.DefaultAy,
            AngleDeg = SectionJson.Num(o, "angleDeg", path, errors) ?? p.AngleDeg,
            EdgeInfluence = SectionJson.Num(o, "edgeInfluence", path, errors) ?? p.EdgeInfluence,
            WallRatio = SectionJson.Num(o, "wallRatio", path, errors) ?? p.WallRatio,
            LiraContour = SectionJson.Bool(o, "liraContour", path, errors) ?? p.LiraContour,
        };
        if (o.ContainsKey("concrete"))
        {
            if (o["concrete"] is null) p = p with { ConcreteMaterialId = 0 };
            else if (SectionJson.Str(o, "concrete", path, errors) is { } t
                     && MaterialId(refs, t, m => m.Type == MatType.Concrete, $"{path}.concrete", errors) is int id)
                p = p with { ConcreteMaterialId = id };
        }
        if (o.ContainsKey("rbtMpa"))
            p = p with { RbtOverride = o["rbtMpa"] is null ? null : SectionJson.Num(o, "rbtMpa", path, errors) * 1000 ?? p.RbtOverride };

        if (o.ContainsKey("sw"))
        {
            if (o["sw"] is null) p = p with { Reinforcement = p.Reinforcement with { Enabled = false } };
            else if (o["sw"] is JsonObject sw) p = p with { Reinforcement = ReadSw(sw, p.Reinforcement, refs, $"{path}.sw", errors) };
            else errors.Add($"{path}.sw", Loc.S("CliModelErrNotObject"));
        }

        if (o["options"] is JsonObject oo)
        {
            Unknown(oo, OptionKeys, $"{path}.options", errors);
            string op = $"{path}.options";
            string[] ecc = ["loading", "unloading", "auto", "ignore"];
            p = p with
            {
                Options = p.Options with
                {
                    EccentricityX = PickStr(oo, "eccentricityX", ecc, p.Options.EccentricityX, op, errors),
                    EccentricityY = PickStr(oo, "eccentricityY", ecc, p.Options.EccentricityY, op, errors),
                    Wb = PickStr(oo, "wb", ["minimum", "stress_side"], p.Options.Wb, op, errors),
                    Pylon = PickStr(oo, "pylon", ["auto", "off"], p.Options.Pylon, op, errors),
                    PylonReduced = SectionJson.Bool(oo, "pylonReduced", op, errors) ?? p.Options.PylonReduced,
                    WallFigure = PickStr(oo, "wallFigure", ["amd1", "amd2"], p.Options.WallFigure, op, errors),
                    HoleInfluence = SectionJson.Num(oo, "holeInfluence", op, errors) ?? p.Options.HoleInfluence,
                },
            };
        }
        else if (o["options"] is not null) errors.Add($"{path}.options", Loc.S("CliModelErrNotObject"));

        if (o["pile"] is JsonObject pl)
        {
            Unknown(pl, PileKeys, $"{path}.pile", errors);
            p = p with
            {
                PileShape = PickStr(pl, "shape", ["square", "circle"], p.PileShape, $"{path}.pile", errors),
                PileSize = SectionJson.Num(pl, "sizeMm", $"{path}.pile", errors) is { } ps ? ps / 1000 : p.PileSize,
            };
        }
        else if (o["pile"] is not null) errors.Add($"{path}.pile", Loc.S("CliModelErrNotObject"));

        // Собственная арматура узлов — общая арматура проверки со значениями узла (как строит её окно).
        var common = p.Reinforcement;
        List<PunchingFemJointOverride> nodes;
        if (o["nodes"] is JsonArray na)
        {
            nodes = [];
            for (int i = 0; i < na.Count; i++)
            {
                if (na[i] is JsonObject no) { if (ReadNode(no, common, refs, $"{path}.nodes[{i}]", errors) is { } n) nodes.Add(n); }
                else errors.Add($"{path}.nodes[{i}]", Loc.S("CliModelErrNotObject"));
            }
        }
        else
        {
            if (o["nodes"] is not null) errors.Add($"{path}.nodes", Loc.S("CliModelErrNotArray"));
            nodes = p.Overrides;
        }
        nodes = nodes.Select(n => n.Reinforcement is { } r
                ? n with { Reinforcement = common with { Enabled = true, Diameter = r.Diameter, Count = r.Count, Step = r.Step, RowCount = r.RowCount } }
                : n)
            .Where(n => n.Excluded || n.Ax != null || n.Ay != null || n.Capital != null || n.Contour != null || n.PileShape != null
                        || n.PileSize != null || n.Reinforcement != null)
            .OrderBy(n => n.Node, FemNodeRigidBodies.TagComparer).ToList();
        return p with { Overrides = nodes };
    }

    static PunchingReinforcementParams ReadSw(JsonObject o, PunchingReinforcementParams r, Refs refs, string path, ModelErrors errors)
    {
        Unknown(o, SwKeys, path, errors);
        r = r with
        {
            Enabled = true,
            Diameter = SectionJson.Num(o, "dMm", path, errors) is { } d ? d / 1000 : r.Diameter,
            Layout = PickStr(o, "layout", ["uniform", "cross"], r.Layout, path, errors),
            Count = SectionJson.Int(o, "count", path, errors) ?? r.Count,
            Step = SectionJson.Num(o, "stepMm", path, errors) is { } s ? s / 1000 : r.Step,
        };
        if (o.ContainsKey("material"))
        {
            if (o["material"] is null) r = r with { MaterialId = 0 };
            else if (SectionJson.Str(o, "material", path, errors) is { } t
                     && MaterialId(refs, t, m => m.Type is MatType.ReSteelF or MatType.ReSteelU, $"{path}.material", errors) is int id)
                r = r with { MaterialId = id };
        }
        if (o.ContainsKey("rswMpa"))
            r = r with { RswOverride = o["rswMpa"] is null ? null : SectionJson.Num(o, "rswMpa", path, errors) * 1000 ?? r.RswOverride };
        if (o.ContainsKey("zoneMm"))
            r = r with { ZoneExtent = o["zoneMm"] is null ? null : SectionJson.Num(o, "zoneMm", path, errors) / 1000 ?? r.ZoneExtent };
        if (o.ContainsKey("rows"))
        {
            if (o["rows"] is null) r = r with { FirstRow = null };
            else if (o["rows"] is JsonObject ro)
            {
                Unknown(ro, SwRowsKeys, $"{path}.rows", errors);
                r = r with
                {
                    FirstRow = SectionJson.Num(ro, "firstMm", $"{path}.rows", errors) is { } f ? f / 1000 : r.FirstRow ?? 0.07,
                    RowStep = SectionJson.Num(ro, "stepMm", $"{path}.rows", errors) is { } rs ? rs / 1000 : r.RowStep,
                    RowCount = SectionJson.Int(ro, "count", $"{path}.rows", errors) ?? r.RowCount,
                };
            }
            else errors.Add($"{path}.rows", Loc.S("CliModelErrNotObject"));
        }
        r = r with
        {
            CrossX = ReadCross(o, "crossX", r.CrossX, path, errors),
            CrossY = ReadCross(o, "crossY", r.CrossY, path, errors),
        };
        return r;
    }

    static PunchingCrossArmParams ReadCross(JsonObject o, string key, PunchingCrossArmParams a, string path, ModelErrors errors)
    {
        if (o[key] is null) return a;
        if (o[key] is not JsonObject co)
        {
            errors.Add($"{path}.{key}", Loc.S("CliModelErrNotObject"));
            return a;
        }
        string cp = $"{path}.{key}";
        Unknown(co, CrossKeys, cp, errors);
        return a with
        {
            Lines = SectionJson.Int(co, "lines", cp, errors) ?? a.Lines,
            Step = SectionJson.Num(co, "stepMm", cp, errors) is { } s ? s / 1000 : a.Step,
            Positive = SectionJson.Bool(co, "positive", cp, errors) ?? a.Positive,
            Negative = SectionJson.Bool(co, "negative", cp, errors) ?? a.Negative,
        };
    }

    /// <summary>Узел документа: только отличия; узел без отличий не нужен (как в окне — переопределения нет).</summary>
    static PunchingFemJointOverride? ReadNode(JsonObject o, PunchingReinforcementParams common, Refs refs, string path, ModelErrors errors)
    {
        Unknown(o, NodeKeys, path, errors);
        if (SectionJson.Str(o, "node", path, errors, required: true) is not { } tag) return null;
        if (refs.NodeTags != null && !refs.NodeTags.Contains(tag))
            errors.Add($"{path}.node", string.Format(Loc.S("CliFemCheckErrUnknownNode"), tag));
        var n = new PunchingFemJointOverride
        {
            Node = tag,
            Excluded = SectionJson.Bool(o, "include", path, errors) is false,
            Ax = SectionJson.Num(o, "axMm", path, errors) / 1000,
            Ay = SectionJson.Num(o, "ayMm", path, errors) / 1000,
            Contour = o["contour"] is null ? null : PickStr(o, "contour", ["check", "opencs", "lira"], "check", path, errors) switch
            {
                "check" => null,
                var s => s,
            },
        };
        switch (o["capital"])
        {
            case null: break;
            case JsonValue v when v.TryGetValue(out string? s) && s is "auto" or "none":
                if (s == "none") n = n with { Capital = "none" };
                break;
            case JsonObject co:
                Unknown(co, NodeCapitalKeys, $"{path}.capital", errors);
                double bx = SectionJson.Num(co, "bxMm", $"{path}.capital", errors, required: true) ?? 0;
                double by = SectionJson.Num(co, "byMm", $"{path}.capital", errors, required: true) ?? 0;
                double hc = SectionJson.Num(co, "hcMm", $"{path}.capital", errors, required: true) ?? 0;
                n = n with
                {
                    Capital = "manual",
                    CapitalManual = new PunchingCapitalParams { Enabled = true, Bx = bx / 1000, By = by / 1000, D = bx / 1000, Hc = hc / 1000 },
                };
                break;
            default:
                errors.Add($"{path}.capital", Loc.S("CliFemCheckErrNodeCapital"));
                break;
        }
        if (o["pile"] is JsonObject pl)
        {
            Unknown(pl, PileKeys, $"{path}.pile", errors);
            string shape = PickStr(pl, "shape", ["check", "square", "circle"], "check", $"{path}.pile", errors);
            n = n with
            {
                PileShape = shape == "check" ? null : shape,
                PileSize = SectionJson.Num(pl, "sizeMm", $"{path}.pile", errors) / 1000,
            };
        }
        else if (o["pile"] is not null) errors.Add($"{path}.pile", Loc.S("CliModelErrNotObject"));
        if (o["sw"] is JsonObject sw)
        {
            Unknown(sw, NodeSwKeys, $"{path}.sw", errors);
            n = n with
            {
                Reinforcement = common with
                {
                    Enabled = true,
                    Diameter = SectionJson.Num(sw, "dMm", $"{path}.sw", errors) is { } d ? d / 1000 : common.Diameter,
                    Count = SectionJson.Int(sw, "count", $"{path}.sw", errors) ?? common.Count,
                    Step = SectionJson.Num(sw, "stepMm", $"{path}.sw", errors) is { } s ? s / 1000 : common.Step,
                    RowCount = SectionJson.Int(sw, "rowCount", $"{path}.sw", errors) ?? common.RowCount,
                },
            };
        }
        else if (o["sw"] is not null) errors.Add($"{path}.sw", Loc.S("CliModelErrNotObject"));
        return n;
    }
}
