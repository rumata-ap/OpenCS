using System.Text.Json.Nodes;
using CScore;
using CScore.ParametricRc;
using CScore.ParametricSteel;
using CScore.Sp16;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Определение параметрического сечения из документа: ЖБ (<see cref="Rc"/>) или сталь (<see cref="Steel"/>) с
/// материалами по обозначению. Id материалов подставляет <see cref="Resolve"/> (материалы документа к разбору ещё
/// не записаны).
/// </summary>
sealed record ParametricDoc(string Path, ParametricRcSectionDefinition? Rc, ParametricSteelSectionDefinition? Steel,
                            string? Concrete, string? Rebar, List<string?> StirrupMaterials, string? SteelMaterial)
{
    /// <summary>Ссылки на материалы: путь поля документа и обозначение.</summary>
    public IEnumerable<(string Path, string Tag)> MaterialRefs()
    {
        if (Concrete != null) yield return ($"{Path}.concrete", Concrete);
        if (Rebar != null) yield return ($"{Path}.rebar", Rebar);
        for (int i = 0; i < StirrupMaterials.Count; i++)
            if (StirrupMaterials[i] is { } t) yield return ($"{Path}.stirrups[{i}].material", t);
        if (SteelMaterial != null) yield return ($"{Path}.steel", SteelMaterial);
    }

    /// <summary>Определение с обозначением сечения и Id материалов (0 — материал не найден).</summary>
    public object Resolve(string tag, Func<string, int> materialId)
    {
        int Id(string? t) => t == null ? 0 : materialId(t);
        if (Rc != null)
            return Rc with
            {
                Tag = tag,
                ConcreteMaterialId = Id(Concrete),
                LongitudinalMaterialId = Id(Rebar),
                StirrupCuts = Rc.StirrupCuts.Select((c, i) => c with { MaterialId = Id(StirrupMaterials[i]) }).ToList(),
            };
        return Steel! with { Tag = tag, MaterialId = Id(SteelMaterial) };
    }
}

/// <summary>
/// Параметрическое сечение в документе opencs-model: <c>sections[].parametric</c>. Размеры — мм, площади — мм², уклон
/// полок — %, It сортамента — мм⁴; материалы — по обозначению. Сформированные области в документ не входят: при
/// <c>apply</c> сечение строит служба (<c>GenerateAndSave</c>).
/// </summary>
static class ParametricJson
{
    const double Mm = 1000;

    static readonly (string Json, ParametricRcShape Value)[] Shapes =
    [
        ("rectangle", ParametricRcShape.Rectangle), ("tee", ParametricRcShape.Tee), ("iBeam", ParametricRcShape.IBeam),
        ("circle", ParametricRcShape.Circle), ("annulus", ParametricRcShape.Annulus)
    ];

    static readonly (string Json, ParametricStirrupZone Value)[] Zones =
    [
        ("body", ParametricStirrupZone.Body), ("flange", ParametricStirrupZone.Flange),
        ("topFlange", ParametricStirrupZone.TopFlange), ("bottomFlange", ParametricStirrupZone.BottomFlange),
        ("web", ParametricStirrupZone.Web)
    ];

    static readonly (string Json, ParametricStirrupDirection Value)[] Directions =
        [("vertical", ParametricStirrupDirection.Vertical), ("horizontal", ParametricStirrupDirection.Horizontal)];

    static readonly (string Json, SteelProfileKind Value)[] Profiles =
    [
        ("iBeam", SteelProfileKind.IBeam), ("channel", SteelProfileKind.Channel), ("tee", SteelProfileKind.Tee),
        ("box", SteelProfileKind.Box), ("pipe", SteelProfileKind.Pipe), ("angle", SteelProfileKind.Angle),
        ("rect", SteelProfileKind.Rect), ("round", SteelProfileKind.Round)
    ];

    static readonly (string Json, SteelFabrication Value)[] Fabrications =
        [("rolled", SteelFabrication.Rolled), ("welded", SteelFabrication.Welded), ("bent", SteelFabrication.Bent)];

    static double ToMm(double m) => ContourJson.Mm(m);
    static double ToMm2(double m2) => Math.Round(m2 * Mm * Mm, 6) + 0.0;
    static string J<T>((string Json, T Value)[] options, T value) where T : notnull =>
        options.First(o => o.Value.Equals(value)).Json;

    // ── Выгрузка ──────────────────────────────────────────────────────────

    /// <summary>ЖБ-определение в документ; <paramref name="tag"/> — обозначение материала по Id.</summary>
    public static JsonObject ToJson(ParametricRcSectionDefinition d, Func<int, string> tag)
    {
        var o = new JsonObject { ["kind"] = "rc", ["shape"] = J(Shapes, d.Shape) };
        if (d.Shape is ParametricRcShape.Circle or ParametricRcShape.Annulus)
        {
            o["d"] = ToMm(d.WidthM);
            if (d.Shape == ParametricRcShape.Annulus) o["dInner"] = ToMm(d.InnerDiameterM);
        }
        else
        {
            o["b"] = ToMm(d.WidthM);
            o["h"] = ToMm(d.HeightM);
            if (d.Shape is ParametricRcShape.Tee or ParametricRcShape.IBeam)
            {
                o["bw"] = ToMm(d.WebThicknessM);
                o["hf"] = ToMm(d.FlangeThicknessM);
            }
        }
        o["concrete"] = tag(d.ConcreteMaterialId);
        o["rebar"] = tag(d.LongitudinalMaterialId);
        if (d.LowerRebar is { Enabled: true } lower) o["bottom"] = Layer(lower);
        if (d.UpperRebar is { Enabled: true } upper) o["top"] = Layer(upper);
        if (d.PolarRebar is { } polar)
            o["polar"] = new JsonObject { ["count"] = polar.Count, ["d"] = ToMm(polar.DiameterM), ["r"] = ToMm(polar.RadiusM) };
        if (d.StirrupCuts.Count > 0)
        {
            var cuts = new JsonArray();
            foreach (var c in d.StirrupCuts)
                cuts.Add(new JsonObject
                {
                    ["zone"] = J(Zones, c.Zone), ["direction"] = J(Directions, c.Direction), ["count"] = c.Count,
                    ["d"] = ToMm(c.DiameterM), ["spacing"] = ToMm(c.SpacingM), ["cover"] = ToMm(c.CoverM),
                    ["material"] = tag(c.MaterialId),
                });
            o["stirrups"] = cuts;
        }
        if (d.ExtraBars.Count > 0)
        {
            var bars = new JsonArray();
            foreach (var b in d.ExtraBars)
                bars.Add(Math.Abs(b.AreaM2 - Math.PI * b.DiameterM * b.DiameterM / 4) <= 1e-9 * Math.Max(b.AreaM2, 1e-12)
                    ? new JsonArray(ToMm(b.X), ToMm(b.Y), ToMm(b.DiameterM))
                    : new JsonObject { ["x"] = ToMm(b.X), ["y"] = ToMm(b.Y), ["d"] = ToMm(b.DiameterM), ["area"] = ToMm2(b.AreaM2) });
            o["extraBars"] = bars;
        }
        return o;
    }

    static JsonObject Layer(ParametricLongitudinalLayer l)
    {
        var o = new JsonObject();
        if (l.IsIdealized)
        {
            o["area"] = ToMm2(l.AreaM2);
            o["axis"] = J(SectionJson.Axes, l.Axis ?? IdealizedRebarAxis.Mx);
        }
        else o["count"] = l.Count;
        o["d"] = ToMm(l.DiameterM);
        o["coord"] = ToMm(l.CoordinateM);
        return o;
    }

    /// <summary>Стальное определение в документ; нулевые (неиспользуемые) размеры не выгружаются.</summary>
    public static JsonObject ToJson(ParametricSteelSectionDefinition d, Func<int, string> tag)
    {
        var o = new JsonObject
        {
            ["kind"] = "steel", ["profile"] = J(Profiles, d.Kind), ["fabrication"] = J(Fabrications, d.Fabrication)
        };
        void Dim(string key, double m) { if (m != 0) o[key] = ToMm(m); }
        Dim("h", d.H); Dim("b", d.Bf1); Dim("tf", d.Tf1); Dim("b2", d.Bf2); Dim("tf2", d.Tf2); Dim("tw", d.Tw);
        Dim("r1", d.R1); Dim("r2", d.R2);
        if (d.FlangeSlope != 0) o["slopePercent"] = Math.Round(d.FlangeSlope * 100, 6) + 0.0;
        if (d.Rotated90) o["rotated90"] = true;
        if (d.Flipped) o["flipped"] = true;
        if (d.Catalog is { } c)
        {
            var co = new JsonObject { ["group"] = c.Group, ["standard"] = c.Standard, ["name"] = c.Name };
            if (c.It != 0) co["it"] = Math.Round(c.It * 1e12, 3) + 0.0;
            o["catalog"] = co;
        }
        o["steel"] = tag(d.MaterialId);
        return o;
    }

    // ── Разбор ────────────────────────────────────────────────────────────

    /// <summary>Определение из <c>parametric</c> (без разрешения материалов); null — ошибки в <paramref name="errors"/>.</summary>
    public static ParametricDoc? Parse(JsonNode? node, string path, ModelErrors errors)
    {
        if (node is not JsonObject o)
        {
            errors.Add(path, Loc.S("CliModelErrNotObject"));
            return null;
        }
        int before = errors.Count;
        string? kind = SectionJson.Str(o, "kind", path, errors, required: true);
        ParametricDoc? doc = kind switch
        {
            "rc" => ParseRc(o, path, errors),
            "steel" => ParseSteel(o, path, errors),
            null => null,
            _ => Fail(errors, $"{path}.kind", string.Format(Loc.S("CliModelErrOneOf"), "rc, steel")),
        };
        return errors.Count > before ? null : doc;
    }

    static ParametricDoc? Fail(ModelErrors errors, string path, string message)
    {
        errors.Add(path, message);
        return null;
    }

    static ParametricDoc ParseRc(JsonObject o, string path, ModelErrors errors)
    {
        double M(string key, bool required = false) => (SectionJson.Num(o, key, path, errors, required) ?? 0) / Mm;
        var shape = SectionJson.Pick(o, "shape", Shapes, ParametricRcShape.Rectangle, path, errors);
        if (o["shape"] is null) errors.Add($"{path}.shape", Loc.S("CliModelErrRequired"));
        bool round = shape is ParametricRcShape.Circle or ParametricRcShape.Annulus;
        double b, h, bw = 0, hf = 0, inner = 0;
        if (round)
        {
            b = h = M("d", required: true);
            if (shape == ParametricRcShape.Annulus) inner = M("dInner", required: true);
        }
        else
        {
            b = M("b", required: true);
            h = M("h", required: true);
            if (shape is ParametricRcShape.Tee or ParametricRcShape.IBeam)
            {
                bw = M("bw", required: true);
                hf = M("hf", required: true);
            }
        }
        string? concrete = SectionJson.Str(o, "concrete", path, errors, required: true);
        string? rebar = SectionJson.Str(o, "rebar", path, errors, required: true);

        ParametricPolarRebar? polar = null;
        if (o["polar"] is JsonObject po)
        {
            string pp = $"{path}.polar";
            polar = new(SectionJson.Int(po, "count", pp, errors) ?? 0, (SectionJson.Num(po, "d", pp, errors, true) ?? 0) / Mm,
                        (SectionJson.Num(po, "r", pp, errors, true) ?? 0) / Mm);
        }
        else if (o["polar"] != null) errors.Add($"{path}.polar", Loc.S("CliModelErrNotObject"));

        var cuts = new List<ParametricStirrupCutSet>();
        var cutMaterials = new List<string?>();
        if (o["stirrups"] is JsonArray sa)
            for (int i = 0; i < sa.Count; i++)
            {
                string sp = $"{path}.stirrups[{i}]";
                if (sa[i] is not JsonObject so)
                {
                    errors.Add(sp, Loc.S("CliModelErrNotObject"));
                    continue;
                }
                int count = SectionJson.Int(so, "count", sp, errors) ?? 0;
                cuts.Add(new ParametricStirrupCutSet(
                    SectionJson.Pick(so, "zone", Zones, ParametricStirrupZone.Body, sp, errors),
                    SectionJson.Pick(so, "direction", Directions, ParametricStirrupDirection.Vertical, sp, errors),
                    count,
                    (SectionJson.Num(so, "d", sp, errors, true) ?? 0) / Mm,
                    (SectionJson.Num(so, "spacing", sp, errors, true) ?? 0) / Mm,
                    (SectionJson.Num(so, "cover", sp, errors, true) ?? 0) / Mm, 0));
                cutMaterials.Add(SectionJson.Str(so, "material", sp, errors, required: count > 0));
            }
        else if (o["stirrups"] != null) errors.Add($"{path}.stirrups", Loc.S("CliModelErrNotArray"));

        var extra = new List<ParametricRebarPoint>();
        if (o["extraBars"] is JsonArray ea)
            for (int i = 0; i < ea.Count; i++)
            {
                string bp = $"{path}.extraBars[{i}]";
                if (ea[i] is JsonArray { Count: 3 } t && t.All(v => v is JsonValue jv && jv.TryGetValue(out double _)))
                {
                    double x = t[0]!.GetValue<double>() / Mm, y = t[1]!.GetValue<double>() / Mm, d = t[2]!.GetValue<double>() / Mm;
                    extra.Add(new(x, y, Math.PI * d * d / 4, d));
                }
                else if (ea[i] is JsonObject bo)
                {
                    double x = (SectionJson.Num(bo, "x", bp, errors, true) ?? 0) / Mm, y = (SectionJson.Num(bo, "y", bp, errors, true) ?? 0) / Mm;
                    double d = (SectionJson.Num(bo, "d", bp, errors, true) ?? 0) / Mm;
                    double? area = SectionJson.Num(bo, "area", bp, errors);
                    extra.Add(new(x, y, area is { } a ? a / (Mm * Mm) : Math.PI * d * d / 4, d));
                }
                else errors.Add(bp, Loc.S("CliSectionErrBar"));
            }
        else if (o["extraBars"] != null) errors.Add($"{path}.extraBars", Loc.S("CliModelErrNotArray"));

        var def = new ParametricRcSectionDefinition(shape, b, h, bw, hf, inner, "",
            ParseLayer(o, "top", path, errors), ParseLayer(o, "bottom", path, errors), polar)
        {
            StirrupCuts = cuts,
            ExtraBars = extra,
        };
        return new ParametricDoc(path, def, null, concrete, rebar, cutMaterials, null);
    }

    static ParametricLongitudinalLayer? ParseLayer(JsonObject o, string key, string path, ModelErrors errors)
    {
        if (o[key] is null) return null;
        string lp = $"{path}.{key}";
        if (o[key] is not JsonObject lo)
        {
            errors.Add(lp, Loc.S("CliModelErrNotObject"));
            return null;
        }
        double d = (SectionJson.Num(lo, "d", lp, errors, true) ?? 0) / Mm;
        double coord = (SectionJson.Num(lo, "coord", lp, errors, true) ?? 0) / Mm;
        if (lo["area"] != null)
        {
            double area = (SectionJson.Num(lo, "area", lp, errors) ?? 0) / (Mm * Mm);
            var axis = SectionJson.Pick(lo, "axis", SectionJson.Axes, IdealizedRebarAxis.Mx, lp, errors);
            return ParametricLongitudinalLayer.Idealized(area, d, coord, axis);
        }
        int count = SectionJson.Int(lo, "count", lp, errors) ?? 0;
        if (lo["count"] is null) errors.Add($"{lp}.count", Loc.S("CliModelErrRequired"));
        return ParametricLongitudinalLayer.Physical(count, d, coord);
    }

    static ParametricDoc ParseSteel(JsonObject o, string path, ModelErrors errors)
    {
        double M(string key) => (SectionJson.Num(o, key, path, errors) ?? 0) / Mm;
        var kind = SectionJson.Pick(o, "profile", Profiles, SteelProfileKind.IBeam, path, errors);
        if (o["profile"] is null) errors.Add($"{path}.profile", Loc.S("CliModelErrRequired"));
        var fabrication = SectionJson.Pick(o, "fabrication", Fabrications,
            ParametricSteelSectionDefinition.AllowedFabrications(kind).FirstOrDefault(), path, errors);
        ParametricSteelCatalogRef? catalog = null;
        if (o["catalog"] is JsonObject co)
        {
            string cp = $"{path}.catalog";
            catalog = new(SectionJson.Str(co, "group", cp, errors) ?? "", SectionJson.Str(co, "standard", cp, errors) ?? "",
                          SectionJson.Str(co, "name", cp, errors, required: true) ?? "",
                          (SectionJson.Num(co, "it", cp, errors) ?? 0) / 1e12);
        }
        else if (o["catalog"] != null) errors.Add($"{path}.catalog", Loc.S("CliModelErrNotObject"));
        if (o["h"] is null) errors.Add($"{path}.h", Loc.S("CliModelErrRequired"));
        var def = new ParametricSteelSectionDefinition
        {
            Kind = kind,
            Fabrication = fabrication,
            H = M("h"), Bf1 = M("b"), Tf1 = M("tf"), Bf2 = M("b2"), Tf2 = M("tf2"), Tw = M("tw"), R1 = M("r1"), R2 = M("r2"),
            FlangeSlope = (SectionJson.Num(o, "slopePercent", path, errors) ?? 0) / 100,
            Rotated90 = SectionJson.Bool(o, "rotated90", path, errors) ?? false,
            Flipped = SectionJson.Bool(o, "flipped", path, errors) ?? false,
            Catalog = catalog,
        };
        string? steel = SectionJson.Str(o, "steel", path, errors, required: true);
        return new ParametricDoc(path, null, def, null, null, [], steel);
    }
}
