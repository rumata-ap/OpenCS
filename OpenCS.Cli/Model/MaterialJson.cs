using System.Text.Json.Nodes;
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Материал в документе opencs-model — в единицах окна материала: прочности МПа (сжатие положительным), модуль ГПа,
/// деформации сжатия положительными. В модели: кПа, Fc &lt; 0, εc &lt; 0.
/// </summary>
static class MaterialJson
{
    static readonly CalcType[] Sets = [CalcType.C, CalcType.CL, CalcType.N, CalcType.NL];

    static readonly (string Json, MatType Value)[] Types =
        [("concrete", MatType.Concrete), ("rebarF", MatType.ReSteelF), ("rebarU", MatType.ReSteelU), ("steel", MatType.Steel)];

    static readonly (string Json, Dampness Value)[] Dampnesses =
        [("below40", Dampness.ниже_40), ("40to75", Dampness.от40_до70), ("above75", Dampness.свыше_70), ("any", Dampness.any)];

    static readonly (string Json, ConcreteKind Value)[] Kinds =
        [("heavy", ConcreteKind.Heavy), ("fineA", ConcreteKind.FineA), ("fineB", ConcreteKind.FineB)];

    /// <summary>Поле JSON ↔ свойство набора характеристик: «модель = json · K».</summary>
    static readonly (string Json, Func<MaterialChars, double> Get, Action<MaterialChars, double> Set, double K)[] Fields =
    [
        ("fc", c => c.Fc, (c, v) => c.Fc = v, -1e3),
        ("ft", c => c.Ft, (c, v) => c.Ft = v, 1e3),
        ("ry", c => c.Ry, (c, v) => c.Ry = v, 1e3),
        ("ru", c => c.Ru, (c, v) => c.Ru = v, 1e3),
        ("e", c => c.E, (c, v) => c.E = v, 1e6),
        ("ec0", c => c.Ec0, (c, v) => c.Ec0 = v, -1),
        ("ec1", c => c.Ec1, (c, v) => c.Ec1 = v, -1),
        ("ec2", c => c.Ec2, (c, v) => c.Ec2 = v, -1),
        ("ec1Red", c => c.Ec1Red, (c, v) => c.Ec1Red = v, -1),
        ("et1Red", c => c.Et1Red, (c, v) => c.Et1Red = v, 1),
        ("et0", c => c.Et0, (c, v) => c.Et0 = v, 1),
        ("et1", c => c.Et1, (c, v) => c.Et1 = v, 1),
        ("et2", c => c.Et2, (c, v) => c.Et2 = v, 1),
    ];

    /// <summary>Обозначения диаграмм пула custom-материала по видам расчёта; null — ссылки на отсутствующие диаграммы.</summary>
    public static Dictionary<CalcType, string>? CustomDiagramTags(Material m, IReadOnlyList<Diagramm> pool)
    {
        var tags = new Dictionary<CalcType, string>();
        foreach (var (ct, id) in m.CustomDiagramIds)
        {
            var d = pool.FirstOrDefault(x => x.Id == id);
            if (d == null) return null;
            tags[ct] = d.Tag;
        }
        return tags;
    }

    public static JsonObject ToJson(Material m, IReadOnlyList<Diagramm> pool)
    {
        if (m.Type == MatType.Custom)
        {
            var diagrams = new JsonObject();
            foreach (var (ct, tag) in CustomDiagramTags(m, pool)!.OrderBy(kv => kv.Key)) diagrams[ct.ToString()] = tag;
            return new JsonObject
            {
                ["tag"] = m.Tag,
                ["type"] = "custom",
                ["description"] = m.Description,
                ["custom"] = new JsonObject
                {
                    ["baseType"] = Types.FirstOrDefault(t => t.Value == m.BaseType).Json ?? "",
                    ["diagrams"] = diagrams
                }
            };
        }
        var chars = new JsonObject();
        foreach (var ct in Sets)
        {
            if (m.GetChars(ct) is not { } ch) continue;
            var o = new JsonObject { ["tag"] = ch.Tag, ["class"] = ch.Class };
            // «+ 0.0» превращает −0 (ноль, делённый на отрицательный множитель) в 0: в документе не будет «-0».
            foreach (var f in Fields) o[f.Json] = f.Get(ch) / f.K + 0.0;
            o["dampness"] = Dampnesses.FirstOrDefault(d => d.Value == ch.Dampness).Json ?? "any";
            chars[ct.ToString()] = o;
        }
        return new JsonObject
        {
            ["tag"] = m.Tag,
            ["type"] = Types.First(t => t.Value == m.Type).Json,
            ["description"] = m.Description,
            ["e"] = m.E / 1e6,
            ["aggregate"] = m.AggregateType,
            ["fireRebarClass"] = m.FireRebarClass,
            ["chars"] = chars
        };
    }

    /// <summary>Материал из элемента документа; null — ошибки записаны в <paramref name="errors"/>.</summary>
    /// <summary>
    /// Материал из элемента документа; null — ошибки записаны в <paramref name="errors"/>. У custom — обозначения
    /// диаграмм пула в <paramref name="diagramTags"/> (Id — после записи раздела diagrams), иначе null.
    /// </summary>
    public static Material? FromJson(JsonObject item, string path, ModelErrors errors, out Dictionary<CalcType, string>? diagramTags)
    {
        int before = errors.Count;
        diagramTags = null;
        Material? m;
        try
        {
            if (item["type"] is JsonValue tv0 && tv0.TryGetValue(out string? t0) && t0 == "custom")
            {
                m = FromCustom(item, path, errors, out diagramTags);
                if (m == null || errors.Count > before) return null;
                if (item["description"] is JsonValue cd) m.Description = cd.GetValue<string>();
                // Проверка без пула (Id временные): ссылки по обозначениям проверяет MaterialsKind.
                foreach (var e in MaterialAuthoring.Validate(m)) errors.Add($"{path}.{e.Path}", e.Message);
                m.CustomDiagramIds = [];
                return errors.Count > before ? null : m;
            }
            if (item["catalog"] is JsonObject catalog)
            {
                foreach (var forbidden in new[] { "type", "chars", "e" })
                    if (item.ContainsKey(forbidden)) errors.Add($"{path}.{forbidden}", Loc.S("CliMaterialErrCatalogAndExplicit"));
                m = FromCatalog(catalog, $"{path}.catalog", errors);
            }
            else
                m = FromExplicit(item, path, errors);
            if (m == null || errors.Count > before) return null;

            if (item["tag"] is JsonValue tag) m.Tag = tag.GetValue<string>().Trim();
            if (item["description"] is JsonValue d) m.Description = d.GetValue<string>();
            if (item["aggregate"] is JsonValue a) m.AggregateType = a.GetValue<string>();
            if (item["fireRebarClass"] is JsonValue fr) m.FireRebarClass = fr.GetValue<string>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            errors.Add(path, Loc.S("CliModelErrBadValue"));
            return null;
        }

        foreach (var e in MaterialAuthoring.Validate(m)) errors.Add($"{path}.{e.Path}", e.Message);
        return errors.Count > before ? null : m;
    }

    static Material? FromCatalog(JsonObject c, string path, ModelErrors errors)
    {
        if (c["concrete"] is JsonValue concrete)
        {
            var kind = Pick(c, "kind", Kinds, ConcreteKind.Heavy, path, errors);
            var damp = Pick(c, "dampness", Dampnesses, Dampness.от40_до70, path, errors);
            var recipe = new ConcreteRecipe(concrete.GetValue<string>(), kind, damp,
                Plain: Flag(c, "plain"), TallLayer: Flag(c, "tallLayer"), HeatTreated: Flag(c, "heatTreated"));
            return MaterialCatalog.CreateConcrete(recipe) ?? NotFound($"{path}.concrete", recipe.Class, errors);
        }
        if (c["rebar"] is JsonValue rebar)
            return MaterialCatalog.CreateRebar(rebar.GetValue<string>())
                   ?? NotFound($"{path}.rebar", rebar.GetValue<string>(), errors);
        if (c["steel"] is JsonValue steel)
        {
            string mark = steel.GetValue<string>();
            var made = c["thicknessMm"] is JsonValue t
                ? MaterialCatalog.CreateStructuralSteel(mark, t.GetValue<double>() / 1000, Flag(c, "shaped"))
                : MaterialCatalog.CreateStructuralSteel(mark);
            return made ?? NotFound($"{path}.steel", mark, errors);
        }
        errors.Add(path, Loc.S("CliMaterialErrCatalogKind"));
        return null;
    }

    /// <summary>
    /// Материал custom: <c>{"tag", "type": "custom", "custom": {"baseType", "diagrams": {"C": "обозначение", …}}}</c>.
    /// Id диаграмм — временные (1), чтобы общая проверка видела, какие виды расчёта заданы.
    /// </summary>
    static Material? FromCustom(JsonObject item, string path, ModelErrors errors, out Dictionary<CalcType, string>? tags)
    {
        tags = null;
        foreach (var forbidden in new[] { "catalog", "chars", "e" })
            if (item.ContainsKey(forbidden)) errors.Add($"{path}.{forbidden}", Loc.S("CliMaterialErrCustomExplicit"));
        if (item["custom"] is not JsonObject c)
        {
            errors.Add($"{path}.custom", Loc.S("CliModelErrNotObject"));
            return null;
        }
        var m = new Material(0) { Type = MatType.Custom, Tag = item["tag"] is JsonValue t ? t.GetValue<string>().Trim() : "" };
        m.BaseType = Pick(c, "baseType", Types, MatType.None, $"{path}.custom", errors);
        tags = [];
        if (c["diagrams"] is JsonObject ds)
            foreach (var ct in Sets)
                if (ds[ct.ToString()] is JsonValue v && v.GetValue<string>().Trim() is { Length: > 0 } tag)
                {
                    tags[ct] = tag;
                    m.CustomDiagramIds[ct] = 1;
                }
        return m;
    }

    static Material? FromExplicit(JsonObject item, string path, ModelErrors errors)
    {
        string? typeName = item["type"] is JsonValue tv ? tv.GetValue<string>() : null;
        var type = Types.FirstOrDefault(t => t.Json == typeName);
        if (type.Json == null)
        {
            errors.Add($"{path}.type", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", Types.Select(t => t.Json))));
            return null;
        }
        var m = new Material(0) { Type = type.Value };
        if (item["e"] is JsonValue e) m.E = e.GetValue<double>() * 1e6;
        var list = new List<MaterialChars>();
        if (item["chars"] is JsonObject chars)
            foreach (var ct in Sets)
            {
                if (chars[ct.ToString()] is not JsonObject o) continue;
                string setPath = $"{path}.chars.{ct}";
                try
                {
                    var ch = new MaterialChars(ct) { Type = type.Value, Tag = o["tag"] is JsonValue t ? t.GetValue<string>() : "" };
                    if (o["class"] is JsonValue cls) ch.Class = cls.GetValue<double>();
                    foreach (var f in Fields)
                        if (o[f.Json] is JsonValue v) f.Set(ch, v.GetValue<double>() * f.K);
                    ch.Dampness = Pick(o, "dampness", Dampnesses, Dampness.any, setPath, errors);
                    list.Add(ch);
                }
                catch (Exception ex) when (ex is InvalidOperationException or FormatException)
                {
                    errors.Add(setPath, Loc.S("CliModelErrBadValue"));
                }
            }
        m.MaterialChars = list;
        if (m.E == 0 && m.GetChars(CalcType.N) is { } n) m.E = n.E;
        return m;
    }

    static T Pick<T>(JsonObject o, string key, (string Json, T Value)[] options, T fallback, string path, ModelErrors errors)
    {
        if (o[key] is not JsonValue v) return fallback;
        string s = v.GetValue<string>();
        foreach (var (json, value) in options)
            if (json == s) return value;
        errors.Add($"{path}.{key}", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", options.Select(x => x.Json))));
        return fallback;
    }

    static bool Flag(JsonObject o, string key) => o[key] is JsonValue v && v.GetValue<bool>();

    static Material? NotFound(string path, string value, ModelErrors errors)
    {
        errors.Add(path, string.Format(Loc.S("CliMaterialErrNotInCatalog"), value));
        return null;
    }
}
