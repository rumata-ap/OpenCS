using System.Text.Json;

namespace CScore.Sp63Punching;

/// <summary>Край плиты на рисунке: точка на краю и внутренняя нормаль (плита — со стороны нормали).</summary>
public sealed record PunchingSceneEdge(PlanPoint Point, PlanPoint InwardNormal);

/// <summary>Касательные из центра тяжести площади к влияющему отверстию (п. 8.1.46): единичные направления.</summary>
public sealed record PunchingSceneHoleRays(int HoleIndex, PlanPoint Lo, PlanPoint Hi);

/// <summary>Вариант контура на рисунке.</summary>
/// <param name="Id">Идентификатор варианта ядра (closed, open_edge_0, pylon…).</param>
/// <param name="Kind">Вид.</param>
/// <param name="Zone">Зона: у грани или за арматурой.</param>
/// <param name="Pieces">Учитываемые куски контура.</param>
/// <param name="Excluded">Части, вырезанные отверстиями.</param>
/// <param name="Centroid">Центр тяжести контура, м.</param>
/// <param name="Utilization">Кисп варианта.</param>
/// <param name="Governs">Определяющий вариант.</param>
/// <param name="Armed">Участки контура под лучами креста (п. 8.1.48, рис. 8.11г); null — нет.</param>
public sealed record PunchingSceneVariant(
    string Id, PunchingVariantKind Kind, PunchingZone Zone,
    IReadOnlyList<IReadOnlyList<ContourElement>> Pieces, IReadOnlyList<ContourElement> Excluded,
    PlanPoint Centroid, double Utilization, bool Governs, IReadOnlyList<ContourElement>? Armed = null);

/// <summary>
/// Рисунок узла продавливания: грузовая площадь, края плиты, отверстия и варианты контура.
/// Общая модель окна параметров, окна результата и отчёта; окно результата и отчёт читают её из
/// сохранённого JSON и не пересчитывают ядро.
/// </summary>
/// <param name="AreaPolygon">Грузовая площадь многоугольником (для круга — пусто).</param>
/// <param name="AreaDiameter">Диаметр круга или наружный диаметр кольца; null — многоугольник.</param>
/// <param name="AreaInnerDiameter">Внутренний диаметр кольца; null — нет.</param>
/// <param name="AreaCentroid">Центр тяжести площади, м.</param>
/// <param name="Edges">Края плиты.</param>
/// <param name="Holes">Отверстия многоугольниками.</param>
/// <param name="H0">Рабочая высота, м (для подписи h0/2).</param>
/// <param name="Variants">Варианты контура.</param>
/// <param name="HoleRays">Касательные к отверстиям, вырезающим часть контура.</param>
/// <param name="SwBars">Стержни поперечной арматуры в плане (по рядам); null — не заданы.</param>
/// <param name="SwCross">Раскладка — крест.</param>
public sealed record PunchingScene(
    IReadOnlyList<PlanPoint> AreaPolygon, double? AreaDiameter, double? AreaInnerDiameter, PlanPoint AreaCentroid,
    IReadOnlyList<PunchingSceneEdge> Edges, IReadOnlyList<IReadOnlyList<PlanPoint>> Holes, double H0,
    IReadOnlyList<PunchingSceneVariant> Variants, IReadOnlyList<PunchingSceneHoleRays>? HoleRays = null,
    IReadOnlyList<PlanPoint>? SwBars = null, bool SwCross = false)
{
    /// <summary>Сцена без вариантов — только исходные данные (для рисунка до расчёта).</summary>
    public static PunchingScene FromInput(PunchingInput input) => From(input, null);

    /// <summary>Сцена по исходным данным и результату ядра.</summary>
    public static PunchingScene From(PunchingInput input, PunchingResult? result)
    {
        ArgumentNullException.ThrowIfNull(input);
        input = input.Normalized();
        IReadOnlyList<PlanPoint> polygon = input.LoadArea switch
        {
            PunchingLoadArea.Circle or PunchingLoadArea.Ring => Array.Empty<PlanPoint>(),
            PunchingLoadArea.Polygon p => p.Points.ToList(),
            _ => PunchingContourBuilder.AreaPolygon(input.LoadArea)
        };
        double? d = input.LoadArea switch
        {
            PunchingLoadArea.Circle c => c.D,
            PunchingLoadArea.Ring r => r.D,
            _ => null
        };
        double? di = input.LoadArea is PunchingLoadArea.Ring ring ? Math.Max(0, ring.D - 2 * ring.T) : null;
        var edges = (input.Edges ?? Array.Empty<PunchingSlabEdge>())
            .Select(e => new PunchingSceneEdge(e.PointOnEdge, e.N)).ToList();
        var holes = (input.Holes ?? Array.Empty<PunchingHole>())
            .Select(h => (IReadOnlyList<PlanPoint>)h.Polygon.ToList()).ToList();
        var variants = result is null
            ? new List<PunchingSceneVariant>()
            : result.Variants.Select(v => new PunchingSceneVariant(
                v.Variant.Id, v.Variant.Kind, v.Variant.Zone,
                v.Variant.Contour.Pieces.Select(p => p.Elements).ToList(),
                v.Variant.Excluded.ToList(),
                new PlanPoint(v.Geometry.Xc, v.Geometry.Yc), v.Utilization, v.Governs,
                v.Sw?.Armed is { } am ? am.ElementsX.Concat(am.ElementsY).ToList() : null)).ToList();
        var rays = new List<PunchingSceneHoleRays>();
        for (int i = 0; i < holes.Count; i++)
            if (PunchingContourBuilder.HoleTangents(input, holes[i]) is { } tg)
                rays.Add(new PunchingSceneHoleRays(i, tg.Lo, tg.Hi));
        var bars = PunchingSwLayout.BarPoints(input);
        return new PunchingScene(polygon, d, di, PunchingContourBuilder.LoadCenter(input),
            edges, holes, input.H0, variants, rays, bars.Count > 0 ? bars : null, input.Reinforcement?.Cross is not null);
    }

    /// <summary>Точки элемента контура для рисования: отрезок — концы, дуга — ломаная с шагом не больше 3°.</summary>
    public static IReadOnlyList<PlanPoint> Tessellate(ContourElement e)
    {
        if (e is not ArcElement a) return [e.Start, e.End];
        int n = Math.Max(2, (int)Math.Ceiling(a.Theta / (Math.PI / 60)));
        return Enumerable.Range(0, n + 1)
            .Select(i => a.Center + a.R * PlanPoint.FromAngle(a.Phi1 + a.Theta * i / n)).ToList();
    }

    /// <summary>Площадь для рисования многоугольником (круг — 72-угольник).</summary>
    public IReadOnlyList<PlanPoint> AreaOutline()
    {
        if (AreaDiameter is not double d) return AreaPolygon;
        return Enumerable.Range(0, 72).Select(i => AreaCentroid + d / 2 * PlanPoint.FromAngle(2 * Math.PI * i / 72)).ToList();
    }

    /// <summary>Внутренний контур кольца (пусто — нет).</summary>
    public IReadOnlyList<PlanPoint> AreaInnerOutline()
    {
        if (AreaInnerDiameter is not double d || d <= 0) return [];
        return Enumerable.Range(0, 72).Select(i => AreaCentroid + d / 2 * PlanPoint.FromAngle(2 * Math.PI * i / 72)).ToList();
    }

    /// <summary>Габарит площади, отверстий и контуров: (xmin, xmax, ymin, ymax), м.</summary>
    public (double XMin, double XMax, double YMin, double YMax) Bounds()
    {
        double xmin = double.MaxValue, xmax = double.MinValue, ymin = double.MaxValue, ymax = double.MinValue;
        void Add(double x, double y)
        {
            xmin = Math.Min(xmin, x); xmax = Math.Max(xmax, x);
            ymin = Math.Min(ymin, y); ymax = Math.Max(ymax, y);
        }
        foreach (var p in AreaPolygon) Add(p.X, p.Y);
        if (AreaDiameter is double d)
        {
            Add(AreaCentroid.X - d / 2, AreaCentroid.Y - d / 2);
            Add(AreaCentroid.X + d / 2, AreaCentroid.Y + d / 2);
        }
        foreach (var h in Holes) foreach (var p in h) Add(p.X, p.Y);
        foreach (var p in SwBars ?? []) Add(p.X, p.Y);
        foreach (var v in Variants)
            foreach (var e in v.Pieces.SelectMany(x => x).Concat(v.Excluded))
            {
                var b = e.Bounds();
                Add(b.XMin, b.YMin); Add(b.XMax, b.YMax);
            }
        if (xmin > xmax) return (-0.5, 0.5, -0.5, 0.5);
        return (xmin, xmax, ymin, ymax);
    }
}

/// <summary>JSON сцены продавливания (ключ <c>scene</c> результата задачи).</summary>
public static class PunchingSceneJson
{
    /// <summary>Пишет сцену объектом.</summary>
    public static void Write(Utf8JsonWriter w, PunchingScene s)
    {
        w.WriteStartObject();
        w.WritePropertyName("area");
        w.WriteStartObject();
        WritePoints(w, "polygon", s.AreaPolygon);
        if (s.AreaDiameter is double d) w.WriteNumber("d", d);
        if (s.AreaInnerDiameter is double di) w.WriteNumber("d_inner", di);
        w.WriteNumber("cx", s.AreaCentroid.X);
        w.WriteNumber("cy", s.AreaCentroid.Y);
        w.WriteEndObject();
        w.WriteNumber("h0", s.H0);
        w.WriteStartArray("edges");
        foreach (var e in s.Edges)
        {
            w.WriteStartObject();
            w.WriteNumber("x", e.Point.X); w.WriteNumber("y", e.Point.Y);
            w.WriteNumber("nx", e.InwardNormal.X); w.WriteNumber("ny", e.InwardNormal.Y);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("holes");
        foreach (var h in s.Holes)
        {
            w.WriteStartArray();
            foreach (var p in h) { w.WriteStartArray(); w.WriteNumberValue(p.X); w.WriteNumberValue(p.Y); w.WriteEndArray(); }
            w.WriteEndArray();
        }
        w.WriteEndArray();
        w.WriteStartArray("hole_rays");
        foreach (var r in s.HoleRays ?? [])
        {
            w.WriteStartObject();
            w.WriteNumber("hole", r.HoleIndex);
            w.WriteNumber("lox", r.Lo.X); w.WriteNumber("loy", r.Lo.Y);
            w.WriteNumber("hix", r.Hi.X); w.WriteNumber("hiy", r.Hi.Y);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("variants");
        foreach (var v in s.Variants)
        {
            w.WriteStartObject();
            w.WriteString("id", v.Id);
            w.WriteString("kind", v.Kind.ToString());
            w.WriteString("zone", v.Zone.ToString());
            if (double.IsFinite(v.Utilization)) w.WriteNumber("ratio", v.Utilization); else w.WriteNull("ratio");
            w.WriteBoolean("governs", v.Governs);
            w.WriteNumber("xc", v.Centroid.X);
            w.WriteNumber("yc", v.Centroid.Y);
            w.WriteStartArray("pieces");
            foreach (var piece in v.Pieces) WriteElements(w, piece);
            w.WriteEndArray();
            w.WritePropertyName("excluded");
            WriteElements(w, v.Excluded);
            if (v.Armed is { Count: > 0 } armed)
            {
                w.WritePropertyName("armed");
                WriteElements(w, armed);
            }
            w.WriteEndObject();
        }
        w.WriteEndArray();
        if (s.SwBars is { Count: > 0 } bars) WritePoints(w, "sw_bars", bars);
        if (s.SwCross) w.WriteBoolean("sw_cross", true);
        w.WriteEndObject();
    }

    /// <summary>Сцена отдельной строкой JSON.</summary>
    public static string ToJson(PunchingScene s)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms)) Write(w, s);
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Читает сцену; неизвестные и отсутствующие поля дают пустые значения.</summary>
    public static PunchingScene Read(JsonElement e)
    {
        var area = e.TryGetProperty("area", out var a) ? a : default;
        var polygon = area.ValueKind == JsonValueKind.Object ? ReadPoints(area, "polygon") : new List<PlanPoint>();
        double? d = area.ValueKind == JsonValueKind.Object && area.TryGetProperty("d", out var dv) && dv.ValueKind == JsonValueKind.Number ? dv.GetDouble() : null;
        double? di = area.ValueKind == JsonValueKind.Object && area.TryGetProperty("d_inner", out var div) && div.ValueKind == JsonValueKind.Number ? div.GetDouble() : null;
        var centroid = area.ValueKind == JsonValueKind.Object ? new PlanPoint(Num(area, "cx"), Num(area, "cy")) : default;

        var edges = new List<PunchingSceneEdge>();
        if (e.TryGetProperty("edges", out var ea) && ea.ValueKind == JsonValueKind.Array)
            foreach (var x in ea.EnumerateArray())
                edges.Add(new PunchingSceneEdge(new PlanPoint(Num(x, "x"), Num(x, "y")), new PlanPoint(Num(x, "nx"), Num(x, "ny"))));

        var holes = new List<IReadOnlyList<PlanPoint>>();
        if (e.TryGetProperty("holes", out var ha) && ha.ValueKind == JsonValueKind.Array)
            foreach (var h in ha.EnumerateArray())
                holes.Add(h.EnumerateArray().Select(p => new PlanPoint(p[0].GetDouble(), p[1].GetDouble())).ToList());

        var variants = new List<PunchingSceneVariant>();
        if (e.TryGetProperty("variants", out var va) && va.ValueKind == JsonValueKind.Array)
            foreach (var v in va.EnumerateArray())
            {
                var pieces = new List<IReadOnlyList<ContourElement>>();
                if (v.TryGetProperty("pieces", out var pa) && pa.ValueKind == JsonValueKind.Array)
                    foreach (var piece in pa.EnumerateArray()) pieces.Add(ReadElements(piece));
                var excluded = v.TryGetProperty("excluded", out var xa) ? ReadElements(xa) : new List<ContourElement>();
                double ratio = v.TryGetProperty("ratio", out var r) && r.ValueKind == JsonValueKind.Number
                    ? r.GetDouble() : double.PositiveInfinity;
                variants.Add(new PunchingSceneVariant(
                    Str(v, "id"),
                    Enum.TryParse<PunchingVariantKind>(Str(v, "kind"), out var k) ? k : PunchingVariantKind.Custom,
                    Enum.TryParse<PunchingZone>(Str(v, "zone"), out var z) ? z : PunchingZone.Main,
                    pieces, excluded, new PlanPoint(Num(v, "xc"), Num(v, "yc")), ratio,
                    v.TryGetProperty("governs", out var g) && g.ValueKind == JsonValueKind.True,
                    v.TryGetProperty("armed", out var am) ? ReadElements(am) : null));
            }
        var rays = new List<PunchingSceneHoleRays>();
        if (e.TryGetProperty("hole_rays", out var ra) && ra.ValueKind == JsonValueKind.Array)
            foreach (var r in ra.EnumerateArray())
                rays.Add(new PunchingSceneHoleRays((int)Num(r, "hole"), new PlanPoint(Num(r, "lox"), Num(r, "loy")), new PlanPoint(Num(r, "hix"), Num(r, "hiy"))));
        var bars = ReadPoints(e, "sw_bars");
        return new PunchingScene(polygon, d, di, centroid, edges, holes, Num(e, "h0"), variants, rays,
            bars.Count > 0 ? bars : null, e.TryGetProperty("sw_cross", out var sc) && sc.ValueKind == JsonValueKind.True);
    }

    static void WritePoints(Utf8JsonWriter w, string name, IReadOnlyList<PlanPoint> pts)
    {
        w.WriteStartArray(name);
        foreach (var p in pts) { w.WriteStartArray(); w.WriteNumberValue(p.X); w.WriteNumberValue(p.Y); w.WriteEndArray(); }
        w.WriteEndArray();
    }

    static List<PlanPoint> ReadPoints(JsonElement e, string name)
        => e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(p => new PlanPoint(p[0].GetDouble(), p[1].GetDouble())).ToList()
            : new List<PlanPoint>();

    static void WriteElements(Utf8JsonWriter w, IEnumerable<ContourElement> elements)
    {
        w.WriteStartArray();
        foreach (var el in elements)
        {
            w.WriteStartObject();
            switch (el)
            {
                case LineElement l:
                    w.WriteString("type", "line");
                    w.WriteNumber("x1", l.A.X); w.WriteNumber("y1", l.A.Y);
                    w.WriteNumber("x2", l.B.X); w.WriteNumber("y2", l.B.Y);
                    break;
                case ArcElement arc:
                    w.WriteString("type", "arc");
                    w.WriteNumber("cx", arc.Center.X); w.WriteNumber("cy", arc.Center.Y); w.WriteNumber("r", arc.R);
                    w.WriteNumber("phi1", arc.Phi1); w.WriteNumber("phi2", arc.Phi2);
                    break;
            }
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    static List<ContourElement> ReadElements(JsonElement a)
    {
        var list = new List<ContourElement>();
        if (a.ValueKind != JsonValueKind.Array) return list;
        foreach (var x in a.EnumerateArray())
        {
            if (Str(x, "type") == "arc")
                list.Add(new ArcElement(new PlanPoint(Num(x, "cx"), Num(x, "cy")), Num(x, "r"), Num(x, "phi1"), Num(x, "phi2")));
            else
                list.Add(new LineElement(new PlanPoint(Num(x, "x1"), Num(x, "y1")), new PlanPoint(Num(x, "x2"), Num(x, "y2"))));
        }
        return list;
    }

    static double Num(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
