using System.Text.Json.Nodes;
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>
/// Контур и круг пула в документе opencs-model. Координаты, диаметр — мм (в модели — м). Вершины контура — без
/// замыкающей; тип — «hull» (оболочка), «hole» (отверстие), «none».
/// </summary>
static class ContourJson
{
    const double MmPerM = 1000;

    public static readonly (string Json, ContourType Value)[] Types =
        [("hull", ContourType.Hull), ("hole", ContourType.Hole), ("none", ContourType.None)];

    /// <summary>Число в мм для документа: метры × 1000, округление до 1 нм — без хвостов двоичного представления.</summary>
    public static double Mm(double m) => Math.Round(m * MmPerM, 6) + 0.0;

    /// <summary>Ключ контура: набор и обозначение («набор / обозначение» или только обозначение).</summary>
    public static string Key(string? set, string tag) =>
        string.IsNullOrWhiteSpace(set) ? tag.Trim() : $"{set.Trim()} / {tag.Trim()}";

    public static JsonObject ToJson(Contour c)
    {
        var points = new JsonArray();
        foreach (var (x, y) in ContourAuthoring.OpenRing(c.Points.Select(p => (p.X, p.Y)).ToList()))
            points.Add(new JsonArray(Mm(x), Mm(y)));
        var o = new JsonObject { ["tag"] = c.Tag };
        if (!string.IsNullOrWhiteSpace(c.GeometrySet)) o["set"] = c.GeometrySet;
        o["type"] = Types.FirstOrDefault(t => t.Value == c.Type).Json ?? "none";
        o["points"] = points;
        return o;
    }

    /// <summary>Контур из элемента документа (м, замкнут, проверен службой); null — ошибки в <paramref name="errors"/>.</summary>
    public static Contour? FromJson(JsonObject item, string path, ModelErrors errors)
    {
        int before = errors.Count;
        string tag = "", set = "";
        var type = ContourType.Hull;
        var pts = new List<(double X, double Y)>();
        try
        {
            if (item["tag"] is JsonValue t) tag = t.GetValue<string>().Trim();
            if (item["set"] is JsonValue s) set = s.GetValue<string>().Trim();
            type = Pick(item, "type", Types, ContourType.Hull, path, errors);
            if (item["points"] is not JsonArray arr)
                errors.Add($"{path}.points", Loc.S("CliModelErrNotArray"));
            else
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonArray { Count: 2 } pair && pair[0] is JsonValue x && pair[1] is JsonValue y)
                        pts.Add((x.GetValue<double>() / MmPerM, y.GetValue<double>() / MmPerM));
                    else
                        errors.Add($"{path}.points[{i}]", Loc.S("CliContourErrPoint"));
                }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or NullReferenceException)
        {
            errors.Add(path, Loc.S("CliModelErrBadValue"));
            return null;
        }
        if (errors.Count > before) return null;

        var c = ContourAuthoring.FromPoints(pts, tag, type);
        c.GeometrySet = set.Length > 0 ? set : null;
        foreach (var e in ContourAuthoring.Validate(c)) errors.Add($"{path}.{e.Path}", e.Message);
        return errors.Count > before ? null : c;
    }

    public static JsonObject CircleToJson(CircleP c)
    {
        var o = new JsonObject { ["tag"] = c.Tag };
        if (!string.IsNullOrWhiteSpace(c.GeometrySet)) o["set"] = c.GeometrySet;
        o["x"] = Mm(c.X);
        o["y"] = Mm(c.Y);
        o["d"] = Mm(2 * c.Radius);
        return o;
    }

    /// <summary>Круг из элемента документа (м, проверен службой); null — ошибки в <paramref name="errors"/>.</summary>
    public static CircleP? CircleFromJson(JsonObject item, string path, ModelErrors errors)
    {
        int before = errors.Count;
        CircleP c;
        try
        {
            double x = Num(item, "x", path, errors), y = Num(item, "y", path, errors), d = Num(item, "d", path, errors);
            c = new CircleP(x / MmPerM, y / MmPerM, d / 2 / MmPerM)
            {
                Tag = item["tag"] is JsonValue t ? t.GetValue<string>().Trim() : "",
            };
            if (item["set"] is JsonValue s && s.GetValue<string>().Trim() is { Length: > 0 } set) c.GeometrySet = set;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or NullReferenceException)
        {
            errors.Add(path, Loc.S("CliModelErrBadValue"));
            return null;
        }
        if (errors.Count > before) return null;
        foreach (var e in ContourAuthoring.ValidateCircle(c)) errors.Add($"{path}.{e.Path}", e.Message);
        return errors.Count > before ? null : c;
    }

    /// <summary>Ключ круга: набор и центр в мм (обозначение круга — метка, не уникальна).</summary>
    public static string CircleKey(string? set, double xM, double yM)
    {
        string at = FormattableString.Invariant($"({Math.Round(xM * MmPerM, 3)}; {Math.Round(yM * MmPerM, 3)})");
        return string.IsNullOrWhiteSpace(set) ? at : $"{set.Trim()} @ {at}";
    }

    static double Num(JsonObject o, string key, string path, ModelErrors errors)
    {
        if (o[key] is JsonValue v) return v.GetValue<double>();
        errors.Add($"{path}.{key}", Loc.S("CliModelErrRequired"));
        return double.NaN;
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
