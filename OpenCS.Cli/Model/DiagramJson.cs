using System.Text.Json.Nodes;
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Диаграмма пула в документе opencs-model — как в таблице страницы диаграммы: точки [ε, σ] со знаком (сжатие
/// отрицательно), σ в МПа. В модели σ в кПа.
/// </summary>
static class DiagramJson
{
    const double KPaPerMPa = 1000;

    public static readonly (string Json, MatType Value)[] MaterialTypes =
        [("concrete", MatType.Concrete), ("rebarF", MatType.ReSteelF), ("rebarU", MatType.ReSteelU), ("steel", MatType.Steel)];

    static readonly (string Json, DiagrammType Value)[] Types =
    [
        ("custom", DiagrammType.Custom), ("l2", DiagrammType.L2), ("l3", DiagrammType.L3), ("ekb", DiagrammType.EKB),
        ("sp63", DiagrammType.SP63), ("sp35", DiagrammType.SP35), ("sp16", DiagrammType.SP16)
    ];

    static readonly CalcType[] CalcTypes = [CalcType.C, CalcType.CL, CalcType.N, CalcType.NL];

    public static JsonObject ToJson(Diagramm d)
    {
        var points = new JsonArray();
        foreach (var p in DiagramAuthoring.ToPoints(d))
            points.Add(new JsonArray(p.Eps + 0.0, p.Sig / KPaPerMPa + 0.0));
        var o = new JsonObject
        {
            ["tag"] = d.Tag,
            ["materialType"] = MaterialTypes.FirstOrDefault(t => t.Value == d.MaterialType).Json ?? d.MaterialType.ToString(),
            ["calcType"] = d.CalcType.ToString(),
            ["type"] = Types.FirstOrDefault(t => t.Value == d.Type).Json ?? "custom",
            ["points"] = points
        };
        if (d.CharacteristicStrains is { Count: > 0 } cs)
            o["characteristic"] = new JsonArray(cs.Select(e => (JsonNode)JsonValue.Create(e)).ToArray());
        return o;
    }

    /// <summary>Диаграмма из элемента документа (σ переведены в кПа, ветви собраны); null — ошибки в <paramref name="errors"/>.</summary>
    public static Diagramm? FromJson(JsonObject item, string path, ModelErrors errors)
    {
        int before = errors.Count;
        var d = new Diagramm { Type = DiagrammType.Custom, CalcType = CalcType.C };
        var pts = new List<DiagramPointData>();
        var characteristic = new List<double>();
        try
        {
            if (item["tag"] is JsonValue tag) d.Tag = tag.GetValue<string>().Trim();
            d.MaterialType = Pick(item, "materialType", MaterialTypes, MatType.None, path, errors);
            if (d.MaterialType == MatType.None && !item.ContainsKey("materialType"))
                errors.Add($"{path}.materialType", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", MaterialTypes.Select(t => t.Json))));
            d.Type = Pick(item, "type", Types, DiagrammType.Custom, path, errors);
            d.CalcType = Pick(item, "calcType", CalcTypes.Select(c => (c.ToString(), c)).ToArray(), CalcType.C, path, errors);

            if (item["points"] is not JsonArray arr)
                errors.Add($"{path}.points", Loc.S("CliModelErrNotArray"));
            else
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonArray { Count: 2 } pair && pair[0] is JsonValue e && pair[1] is JsonValue s)
                        pts.Add(new DiagramPointData(e.GetValue<double>(), s.GetValue<double>() * KPaPerMPa));
                    else
                        errors.Add($"{path}.points[{i}]", Loc.S("CliDiagramErrPoint"));
                }
            if (item["characteristic"] is JsonArray ch)
                foreach (var v in ch) characteristic.Add(v!.GetValue<double>());
            else if (item.ContainsKey("characteristic"))
                errors.Add($"{path}.characteristic", Loc.S("CliModelErrNotArray"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or NullReferenceException)
        {
            errors.Add(path, Loc.S("CliModelErrBadValue"));
            return null;
        }
        if (errors.Count > before) return null;

        foreach (var e in DiagramAuthoring.ValidatePoints(pts)) errors.Add($"{path}.{e.Path}", e.Message);
        if (errors.Count > before) return null;
        DiagramAuthoring.Apply(d, pts, characteristic);
        foreach (var e in DiagramAuthoring.Validate(d)) errors.Add($"{path}.{e.Path}", e.Message);
        return errors.Count > before ? null : d;
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
}
