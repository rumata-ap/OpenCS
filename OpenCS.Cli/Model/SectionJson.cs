using System.Text.Json.Nodes;
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Cli.Model;

/// <summary>Хомут (элемент группы поперечного армирования) в документе: Ø и площадь — м/м², вершины — м.</summary>
sealed record StirrupElementDoc(string Path, double D, double? Area, bool Closed, List<(double X, double Y)> Points);

/// <summary>Группа хомутов в документе: шаг и отступ — м; материал — только у бетонной области.</summary>
sealed record StirrupGroupDoc(string Path, string? Material, double Spacing, double? Offset, List<StirrupElementDoc> Elements);

/// <summary>Стержень группы в документе (м, м²; площадь null — из диаметра).</summary>
sealed record BarDoc(double X, double Y, double D, double? Area);

/// <summary>Область сечения в документе (единицы базы — м, м², МПа).</summary>
sealed class AreaDoc
{
    public string Path = "";
    public string Tag = "";
    public string? Description;
    public AreaCategory Category = AreaCategory.Region;
    public string Material = "";
    public DiagrammType? Diagram;
    public string? Contour;
    public List<(double X, double Y)>? Outer;
    public List<List<(double X, double Y)>> Holes = [];
    public MeshMethod MeshMethod = MeshMethod.Grid;
    public int Nx = 21, Ny = 21, Smooth = 5;
    public double MaxArea = 0.01, MinAngle = 30, MaxEdge;
    public string? Host;
    public List<BarDoc> Bars = [];
    public double SigSp, GammaSp = 1;
    public RebarRepresentation Representation = RebarRepresentation.PhysicalBars;
    public IdealizedRebarAxis? Axis;
    public List<StirrupGroupDoc> Stirrups = [];
}

/// <summary>Часть стадийного сечения: ссылка на сечение или свои области.</summary>
sealed record PartDoc(string Path, string Tag, string? Section, List<AreaDoc> Areas);

/// <summary>Сечение в документе: произвольное (<see cref="Areas"/>) или стадийное (<see cref="Parts"/>).</summary>
sealed record SectionDoc(string Path, string Tag, string? Description, List<AreaDoc>? Areas, List<PartDoc>? Parts)
{
    public bool IsStaged => Parts != null;

    /// <summary>Параметрическое определение (тогда ни <see cref="Areas"/>, ни <see cref="Parts"/> нет).</summary>
    public ParametricDoc? Parametric { get; init; }

    /// <summary>Свои области: у произвольного — состав, у стадийного — области частей без ссылки.</summary>
    public IEnumerable<AreaDoc> OwnAreas => Areas ?? Parts?.SelectMany(p => p.Areas) ?? [];
}

/// <summary>
/// Сечение в документе opencs-model (вид «sections»). Координаты, Ø, шаг, отступ — мм; площади стержней — мм²;
/// σsp — МПа. Области описываются внутри сечения (ключ области — сечение и её обозначение); бетонная область берёт
/// контур пула по ключу (<c>contour</c>, как <see cref="ContourJson.Key"/>) или задаётся многоугольником
/// (<c>polygon</c>). Сетка волокон в документ не входит (её строит окно «Сетка»), только её параметры.
/// </summary>
static class SectionJson
{
    const double MmPerM = 1000;

    public static readonly (string Json, AreaCategory Value)[] Categories =
        [("region", AreaCategory.Region), ("rebarGroup", AreaCategory.RebarGroup), ("stirrups", AreaCategory.Stirrups)];

    public static readonly (string Json, DiagrammType Value)[] Diagrams =
    [
        ("l2", DiagrammType.L2), ("l3", DiagrammType.L3), ("ekb", DiagrammType.EKB), ("sp63", DiagrammType.SP63),
        ("sp35", DiagrammType.SP35), ("sp16", DiagrammType.SP16), ("custom", DiagrammType.Custom)
    ];

    public static readonly (string Json, MeshMethod Value)[] MeshMethods =
        [("grid", MeshMethod.Grid), ("ruppert", MeshMethod.Ruppert), ("advancingFront", MeshMethod.AdvancingFront)];

    public static readonly (string Json, IdealizedRebarAxis Value)[] Axes =
        [("Mx", IdealizedRebarAxis.Mx), ("My", IdealizedRebarAxis.My)];

    static double Mm(double m) => ContourJson.Mm(m);
    static double Mm2(double m2) => Math.Round(m2 * MmPerM * MmPerM, 6) + 0.0;

    static JsonArray Ring(IEnumerable<(double X, double Y)> pts)
    {
        var a = new JsonArray();
        foreach (var (x, y) in pts) a.Add(new JsonArray(Mm(x), Mm(y)));
        return a;
    }

    static List<(double X, double Y)> RingOf(Contour c) =>
        ContourAuthoring.OpenRing(c.X.Count > 0 ? c.X.Zip(c.Y, (x, y) => (x, y)).ToList() : c.Points.Select(p => (p.X, p.Y)).ToList());

    static bool BarAreaFromDiameter(double d, double area) => Math.Abs(area - Math.PI * d * d / 4) <= 1e-9 * Math.Max(area, 1e-12);

    // ── Выгрузка ──────────────────────────────────────────────────────────

    /// <summary>
    /// Сечение для документа (проверенное службой). Ключи контуров пула — <paramref name="contourKey"/> (null — контур
    /// неоднозначен, область выгружается многоугольником).
    /// </summary>
    public static JsonObject ToJson(CrossSection s, IReadOnlyList<Material> materials, IReadOnlyList<CrossSection> all,
                                    Func<Contour, string?> contourKey)
    {
        var o = new JsonObject { ["tag"] = s.Tag };
        if (!string.IsNullOrWhiteSpace(s.Description)) o["description"] = s.Description;
        if (s is StagedSection st)
        {
            var parts = new JsonArray();
            foreach (var p in st.Parts)
            {
                var po = new JsonObject { ["tag"] = p.Tag };
                if (p.SourceSectionId is int src) po["section"] = all.First(c => c.Id == src).Tag;
                else po["areas"] = Areas(p.Areas, materials, contourKey);
                parts.Add(po);
            }
            o["staged"] = new JsonObject { ["parts"] = parts };
        }
        else
            o["areas"] = Areas(s.Areas, materials, contourKey);
        return o;
    }

    static JsonArray Areas(IReadOnlyList<MaterialArea> areas, IReadOnlyList<Material> materials, Func<Contour, string?> contourKey)
    {
        var arr = new JsonArray();
        foreach (var a in areas) arr.Add(AreaToJson(a, materials, contourKey));
        return arr;
    }

    static string MaterialTag(MaterialArea a, IReadOnlyList<Material> materials, int? id = null) =>
        (id is int mid ? materials.FirstOrDefault(m => m.Id == mid) : a.Material ?? materials.FirstOrDefault(m => m.Id == a.MaterialId))?.Tag.Trim() ?? "";

    static JsonObject AreaToJson(MaterialArea a, IReadOnlyList<Material> materials, Func<Contour, string?> contourKey)
    {
        var o = new JsonObject
        {
            ["tag"] = a.Tag,
            ["category"] = Categories.First(c => c.Value == a.Category).Json,
            ["material"] = MaterialTag(a, materials),
        };
        if (!string.IsNullOrWhiteSpace(a.Description)) o["description"] = a.Description;
        if (a.Category != AreaCategory.Stirrups)
            o["diagram"] = Diagrams.First(d => d.Value == a.DiagrammType).Json;
        switch (a.Category)
        {
            case AreaCategory.Region:
                string? key = a.PoolContour != null && ReferenceEquals(a.Hull, a.PoolContour) ? contourKey(a.PoolContour) : null;
                var holes = new JsonArray();
                foreach (var h in a.Holes) holes.Add(Ring(RingOf(h)));
                if (key != null)
                {
                    o["contour"] = key;
                    if (holes.Count > 0) o["holes"] = holes;
                }
                else
                {
                    var poly = new JsonObject { ["outer"] = Ring(RingOf(a.Hull!)) };
                    if (holes.Count > 0) poly["holes"] = holes;
                    o["polygon"] = poly;
                }
                var mesh = new JsonObject();
                if (a.MeshMethod != MeshMethod.Grid) mesh["method"] = MeshMethods.First(m => m.Value == a.MeshMethod).Json;
                if (a.NX != 21) mesh["nx"] = a.NX;
                if (a.NY != 21) mesh["ny"] = a.NY;
                if (a.MeshMaxArea != 0.01) mesh["maxArea"] = a.MeshMaxArea;
                if (a.MeshMinAngle != 30) mesh["minAngle"] = a.MeshMinAngle;
                if (a.MeshMaxEdgeLen != 0) mesh["maxEdge"] = Mm(a.MeshMaxEdgeLen);
                if (a.MeshSmoothIter != 5) mesh["smooth"] = a.MeshSmoothIter;
                if (mesh.Count > 0) o["mesh"] = mesh;
                break;
            case AreaCategory.RebarGroup:
                if (a.HostArea != null) o["host"] = a.HostArea.Tag;
                var bars = new JsonArray();
                foreach (var f in a.Fibers.Where(f => f.TypeFiber == FiberType.point))
                    bars.Add(BarAreaFromDiameter(f.Diameter, f.Area)
                        ? new JsonArray(Mm(f.X), Mm(f.Y), Mm(f.Diameter))
                        : new JsonObject { ["x"] = Mm(f.X), ["y"] = Mm(f.Y), ["d"] = Mm(f.Diameter), ["area"] = Mm2(f.Area) });
                o["bars"] = bars;
                if (a.SigSp != 0 || a.GammaSp != 1)
                    o["prestress"] = new JsonObject { ["sigma"] = a.SigSp, ["gamma"] = a.GammaSp };
                if (a.RebarRepresentation == RebarRepresentation.IdealizedLayer)
                {
                    o["representation"] = "idealizedLayer";
                    if (a.IdealizedAxis is { } axis) o["axis"] = Axes.First(x => x.Value == axis).Json;
                }
                break;
        }
        if (a.Stirrups.Count > 0)
        {
            var groups = new JsonArray();
            foreach (var g in a.Stirrups)
            {
                var go = new JsonObject();
                if (a.Category == AreaCategory.Region) go["material"] = MaterialTag(a, materials, g.MaterialId);
                go["spacing"] = Mm(g.SpacingM);
                if (g.OffsetM is double off) go["offset"] = Mm(off);
                var elements = new JsonArray();
                foreach (var e in g.Elements)
                {
                    var eo = new JsonObject { ["d"] = Mm(e.BarDiameterM) };
                    if (!BarAreaFromDiameter(e.BarDiameterM, e.BarAreaM2)) eo["area"] = Mm2(e.BarAreaM2);
                    eo["closed"] = e.IsClosed;
                    var line = e.CenterlineContour;
                    var pts = line.X.Zip(line.Y, (x, y) => (x, y)).ToList();
                    eo["points"] = Ring(e.IsClosed ? ContourAuthoring.OpenRing(pts) : pts);
                    elements.Add(eo);
                }
                go["elements"] = elements;
                groups.Add(go);
            }
            o["stirrups"] = groups;
        }
        return o;
    }

    // ── Разбор ────────────────────────────────────────────────────────────

    /// <summary>Сечение из элемента документа (без разрешения ссылок); null — ошибки в <paramref name="errors"/>.</summary>
    public static SectionDoc? Parse(JsonObject item, string path, ModelErrors errors)
    {
        int before = errors.Count;
        string tag = Str(item, "tag", path, errors, required: true) ?? "";
        string? description = Str(item, "description", path, errors);
        bool hasAreas = item.ContainsKey("areas"), hasStaged = item.ContainsKey("staged"), hasParametric = item.ContainsKey("parametric");
        if ((hasAreas ? 1 : 0) + (hasStaged ? 1 : 0) + (hasParametric ? 1 : 0) != 1)
        {
            errors.Add(path, Loc.S("CliSectionErrAreasOrStaged"));
            return null;
        }
        if (hasParametric)
        {
            var parametric = ParametricJson.Parse(item["parametric"], $"{path}.parametric", errors);
            return errors.Count > before || parametric == null ? null
                : new SectionDoc(path, tag, description, null, null) { Parametric = parametric };
        }
        List<AreaDoc>? areas = null;
        List<PartDoc>? parts = null;
        if (hasAreas)
            areas = ParseAreas(item["areas"], $"{path}.areas", errors);
        else if (item["staged"] is not JsonObject staged)
            errors.Add($"{path}.staged", Loc.S("CliModelErrNotObject"));
        else if (staged["parts"] is not JsonArray arr)
            errors.Add($"{path}.staged.parts", Loc.S("CliModelErrNotArray"));
        else
        {
            parts = [];
            for (int k = 0; k < arr.Count; k++)
            {
                string pp = $"{path}.staged.parts[{k}]";
                if (arr[k] is not JsonObject po)
                {
                    errors.Add(pp, Loc.S("CliModelErrNotObject"));
                    continue;
                }
                string ptag = Str(po, "tag", pp, errors, required: true) ?? "";
                string? section = Str(po, "section", pp, errors);
                bool partAreas = po.ContainsKey("areas");
                if ((section != null) == partAreas)
                {
                    errors.Add(pp, Loc.S("CliSectionErrPartSectionOrAreas"));
                    continue;
                }
                parts.Add(new PartDoc(pp, ptag, section, partAreas ? ParseAreas(po["areas"], $"{pp}.areas", errors) : []));
            }
            var seen = new HashSet<string>();
            foreach (var a in parts.SelectMany(p => p.Areas))
                if (a.Tag.Length > 0 && !seen.Add(a.Tag))
                    errors.Add($"{a.Path}.tag", string.Format(Loc.S("SectionErrAreaTagDuplicate"), a.Tag));
        }
        return errors.Count > before ? null : new SectionDoc(path, tag, description, areas, parts);
    }

    static List<AreaDoc> ParseAreas(JsonNode? node, string path, ModelErrors errors)
    {
        var list = new List<AreaDoc>();
        if (node is not JsonArray arr)
        {
            errors.Add(path, Loc.S("CliModelErrNotArray"));
            return list;
        }
        for (int i = 0; i < arr.Count; i++)
        {
            string ap = $"{path}[{i}]";
            if (arr[i] is not JsonObject o)
            {
                errors.Add(ap, Loc.S("CliModelErrNotObject"));
                continue;
            }
            list.Add(ParseArea(o, ap, errors));
        }
        return list;
    }

    static AreaDoc ParseArea(JsonObject o, string path, ModelErrors errors)
    {
        var a = new AreaDoc
        {
            Path = path,
            Tag = Str(o, "tag", path, errors, required: true) ?? "",
            Description = Str(o, "description", path, errors),
            Category = Pick(o, "category", Categories, AreaCategory.Region, path, errors),
            Material = Str(o, "material", path, errors, required: true) ?? "",
        };
        if (o.ContainsKey("diagram")) a.Diagram = Pick(o, "diagram", Diagrams, DiagrammType.L2, path, errors);
        switch (a.Category)
        {
            case AreaCategory.Region:
                a.Contour = Str(o, "contour", path, errors);
                bool poly = o.ContainsKey("polygon");
                if ((a.Contour != null) == poly)
                    errors.Add(path, Loc.S("CliSectionErrContourOrPolygon"));
                else if (poly)
                {
                    if (o["polygon"] is not JsonObject p)
                        errors.Add($"{path}.polygon", Loc.S("CliModelErrNotObject"));
                    else
                    {
                        a.Outer = Points(p["outer"], $"{path}.polygon.outer", errors);
                        a.Holes = Rings(p["holes"], $"{path}.polygon.holes", errors);
                    }
                    if (o.ContainsKey("holes")) errors.Add($"{path}.holes", Loc.S("CliSectionErrHolesWithPolygon"));
                }
                else
                    a.Holes = Rings(o["holes"], $"{path}.holes", errors);
                if (o["mesh"] is JsonObject m)
                {
                    string mp = $"{path}.mesh";
                    a.MeshMethod = Pick(m, "method", MeshMethods, MeshMethod.Grid, mp, errors);
                    a.Nx = Int(m, "nx", mp, errors) ?? 21;
                    a.Ny = Int(m, "ny", mp, errors) ?? 21;
                    a.MaxArea = Num(m, "maxArea", mp, errors) ?? 0.01;
                    a.MinAngle = Num(m, "minAngle", mp, errors) ?? 30;
                    a.MaxEdge = (Num(m, "maxEdge", mp, errors) ?? 0) / MmPerM;
                    a.Smooth = Int(m, "smooth", mp, errors) ?? 5;
                }
                else if (o.ContainsKey("mesh")) errors.Add($"{path}.mesh", Loc.S("CliModelErrNotObject"));
                break;
            case AreaCategory.RebarGroup:
                a.Host = Str(o, "host", path, errors);
                if (o["bars"] is not JsonArray bars)
                    errors.Add($"{path}.bars", Loc.S("CliModelErrNotArray"));
                else
                    for (int i = 0; i < bars.Count; i++)
                    {
                        string bp = $"{path}.bars[{i}]";
                        if (bars[i] is JsonArray { Count: 3 } t && t.All(v => v is JsonValue jv && jv.TryGetValue(out double _)))
                            a.Bars.Add(new BarDoc(t[0]!.GetValue<double>() / MmPerM, t[1]!.GetValue<double>() / MmPerM,
                                                  t[2]!.GetValue<double>() / MmPerM, null));
                        else if (bars[i] is JsonObject bo)
                        {
                            double? x = Num(bo, "x", bp, errors, true), y = Num(bo, "y", bp, errors, true),
                                    d = Num(bo, "d", bp, errors, true), area = Num(bo, "area", bp, errors);
                            if (x != null && y != null && d != null)
                                a.Bars.Add(new BarDoc(x.Value / MmPerM, y.Value / MmPerM, d.Value / MmPerM,
                                                      area / (MmPerM * MmPerM)));
                        }
                        else
                            errors.Add(bp, Loc.S("CliSectionErrBar"));
                    }
                if (o["prestress"] is JsonObject ps)
                {
                    a.SigSp = Num(ps, "sigma", $"{path}.prestress", errors) ?? 0;
                    a.GammaSp = Num(ps, "gamma", $"{path}.prestress", errors) ?? 1;
                }
                else if (o.ContainsKey("prestress")) errors.Add($"{path}.prestress", Loc.S("CliModelErrNotObject"));
                if (o.ContainsKey("representation"))
                    a.Representation = Pick(o, "representation",
                        [("physicalBars", RebarRepresentation.PhysicalBars), ("idealizedLayer", RebarRepresentation.IdealizedLayer)],
                        RebarRepresentation.PhysicalBars, path, errors);
                if (o.ContainsKey("axis")) a.Axis = Pick(o, "axis", Axes, IdealizedRebarAxis.Mx, path, errors);
                break;
        }
        if (o.ContainsKey("stirrups"))
        {
            if (o["stirrups"] is not JsonArray groups)
                errors.Add($"{path}.stirrups", Loc.S("CliModelErrNotArray"));
            else
                for (int g = 0; g < groups.Count; g++)
                {
                    string gp = $"{path}.stirrups[{g}]";
                    if (groups[g] is not JsonObject go)
                    {
                        errors.Add(gp, Loc.S("CliModelErrNotObject"));
                        continue;
                    }
                    var elements = new List<StirrupElementDoc>();
                    if (go["elements"] is not JsonArray els)
                        errors.Add($"{gp}.elements", Loc.S("CliModelErrNotArray"));
                    else
                        for (int e = 0; e < els.Count; e++)
                        {
                            string ep = $"{gp}.elements[{e}]";
                            if (els[e] is not JsonObject eo)
                            {
                                errors.Add(ep, Loc.S("CliModelErrNotObject"));
                                continue;
                            }
                            double d = (Num(eo, "d", ep, errors, true) ?? double.NaN) / MmPerM;
                            double? area = Num(eo, "area", ep, errors) / (MmPerM * MmPerM);
                            bool closed = Bool(eo, "closed", ep, errors) ?? true;
                            elements.Add(new StirrupElementDoc(ep, d, area, closed, Points(eo["points"], $"{ep}.points", errors) ?? []));
                        }
                    a.Stirrups.Add(new StirrupGroupDoc(gp, Str(go, "material", gp, errors),
                        (Num(go, "spacing", gp, errors, true) ?? double.NaN) / MmPerM,
                        Num(go, "offset", gp, errors) / MmPerM, elements));
                }
        }
        return a;
    }

    // ── Построение области ───────────────────────────────────────────────

    /// <summary>
    /// Область из документа в <paramref name="target"/> (новая или существующая — правка на месте). Носитель не
    /// разрешается (по обозначению в сечении — <see cref="ResolveHosts"/>). Сетка волокон сбрасывается, если сменилась
    /// геометрия области.
    /// </summary>
    public static void Fill(MaterialArea target, AreaDoc d, Func<string, Material?> material, Func<string, Contour?> contour)
    {
        string? oldWkt = target.WKT;
        var m = material(d.Material);
        target.Tag = d.Tag;
        target.Description = d.Description;
        target.Category = d.Category;
        target.Material = m;
        target.MaterialId = m?.Id ?? 0;
        target.DiagrammType = d.Diagram ?? (m == null ? DiagrammType.L2
            : m.Type == MatType.Concrete ? DiagrammType.SP63 : DiagrammCompatibility.Coerce(m.Type, DiagrammType.L2));
        target.HostArea = null;
        target.HostAreaId = null;
        target.Contours = [];
        target.PoolContour = null;
        target.PoolContourId = null;
        target.WKT = null;
        if (d.Category == AreaCategory.Region)
        {
            if (d.Contour != null && contour(d.Contour) is { } pool)
            {
                target.Hull = pool;
                target.PoolContour = pool;
                target.PoolContourId = pool.Id;
            }
            else if (d.Outer != null)
                target.Hull = Closed(d.Outer, "hull", ContourType.Hull);
            for (int i = 0; i < d.Holes.Count; i++)
                target.Contours.Add(Closed(d.Holes[i], $"hole{i}", ContourType.Hole));
            target.SetWKT();
        }
        target.MeshMethod = d.MeshMethod;
        target.NX = d.Nx;
        target.NY = d.Ny;
        target.MeshMaxArea = d.MaxArea;
        target.MeshMinAngle = d.MinAngle;
        target.MeshMaxEdgeLen = d.MaxEdge;
        target.MeshSmoothIter = d.Smooth;
        target.SigSp = d.SigSp;
        target.GammaSp = d.GammaSp;
        target.RebarRepresentation = d.Representation;
        target.IdealizedAxis = d.Axis;
        var keepMesh = target.WKT == oldWkt
            ? target.Fibers.Where(f => f.TypeFiber is FiberType.poly or FiberType.tri).ToList()
            : [];
        target.Fibers = [.. keepMesh, .. d.Bars.Select(b =>
        {
            var f = Fiber.CreatePoint(b.D, b.X, b.Y);
            if (b.Area is double area) f.Area = area;
            return f;
        })];
        target.Stirrups = d.Stirrups.Select(g => new StirrupGroup
        {
            MaterialId = d.Category == AreaCategory.Stirrups ? m?.Id ?? 0 : material(g.Material ?? "")?.Id ?? 0,
            SpacingM = g.Spacing,
            OffsetM = g.Offset,
            Elements = g.Elements.Select(e => new StirrupElement
            {
                CenterlineContour = Line(e),
                BarDiameterM = e.D,
                BarAreaM2 = e.Area ?? Math.PI * e.D * e.D / 4,
            }).ToList(),
        }).ToList();
    }

    /// <summary>Носители групп стержней — по обозначению среди областей того же набора.</summary>
    public static void ResolveHosts(IReadOnlyList<MaterialArea> areas, IReadOnlyList<AreaDoc> docs)
    {
        for (int i = 0; i < docs.Count; i++)
            if (docs[i].Host is string host)
                areas[i].HostArea = areas.FirstOrDefault(a => a.Tag == host);
    }

    /// <summary>Замкнутый контур (м): вершины с замыкающей, X/Y и WKT (треугольник тоже).</summary>
    static Contour Closed(List<(double X, double Y)> pts, string tag, ContourType type)
    {
        var c = ContourAuthoring.FromPoints(pts, tag, type);
        c.PointsToXYs();
        c.SetWKT();
        return c;
    }

    /// <summary>Центровая линия хомута: замкнутый — кольцо, открытый срез — полилиния (WKT LINESTRING).</summary>
    static Contour Line(StirrupElementDoc e)
    {
        if (e.Closed) return Closed(e.Points, "stirrup", ContourType.None);
        var c = new Contour
        {
            Tag = "stirrup",
            IsPolyline = true,
            X = e.Points.Select(p => p.X).ToList(),
            Y = e.Points.Select(p => p.Y).ToList(),
        };
        c.SetWKT();
        c.Points = c.XYsToPoints();
        return c;
    }

    // ── Чтение значений ──────────────────────────────────────────────────

    internal static string? Str(JsonObject o, string key, string path, ModelErrors errors, bool required = false)
    {
        if (o[key] is null)
        {
            if (required) errors.Add($"{path}.{key}", Loc.S("CliModelErrRequired"));
            return null;
        }
        if (o[key] is JsonValue v && v.TryGetValue(out string? s)) return s.Trim();
        errors.Add($"{path}.{key}", Loc.S("CliModelErrBadValue"));
        return null;
    }

    internal static double? Num(JsonObject o, string key, string path, ModelErrors errors, bool required = false)
    {
        if (o[key] is null)
        {
            if (required) errors.Add($"{path}.{key}", Loc.S("CliModelErrRequired"));
            return null;
        }
        if (o[key] is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d)) return d;
        errors.Add($"{path}.{key}", Loc.S("CliModelErrBadValue"));
        return null;
    }

    internal static int? Int(JsonObject o, string key, string path, ModelErrors errors)
    {
        if (o[key] is null) return null;
        if (o[key] is JsonValue v && v.TryGetValue(out int i)) return i;
        errors.Add($"{path}.{key}", Loc.S("CliModelErrBadValue"));
        return null;
    }

    internal static bool? Bool(JsonObject o, string key, string path, ModelErrors errors)
    {
        if (o[key] is null) return null;
        if (o[key] is JsonValue v && v.TryGetValue(out bool b)) return b;
        errors.Add($"{path}.{key}", Loc.S("CliModelErrBadValue"));
        return null;
    }

    internal static T Pick<T>(JsonObject o, string key, (string Json, T Value)[] options, T fallback, string path, ModelErrors errors)
    {
        if (o[key] is null) return fallback;
        if (o[key] is JsonValue v && v.TryGetValue(out string? s))
            foreach (var (json, value) in options)
                if (json == s) return value;
        errors.Add($"{path}.{key}", string.Format(Loc.S("CliModelErrOneOf"), string.Join(", ", options.Select(x => x.Json))));
        return fallback;
    }

    static List<(double X, double Y)>? Points(JsonNode? node, string path, ModelErrors errors)
    {
        if (node is not JsonArray arr)
        {
            errors.Add(path, Loc.S("CliModelErrNotArray"));
            return null;
        }
        var pts = new List<(double X, double Y)>();
        for (int i = 0; i < arr.Count; i++)
        {
            if (arr[i] is JsonArray { Count: 2 } pair && pair[0] is JsonValue x && x.TryGetValue(out double px)
                && pair[1] is JsonValue y && y.TryGetValue(out double py))
                pts.Add((px / MmPerM, py / MmPerM));
            else
                errors.Add($"{path}[{i}]", Loc.S("CliContourErrPoint"));
        }
        return pts;
    }

    static List<List<(double X, double Y)>> Rings(JsonNode? node, string path, ModelErrors errors)
    {
        var rings = new List<List<(double X, double Y)>>();
        if (node is null) return rings;
        if (node is not JsonArray arr)
        {
            errors.Add(path, Loc.S("CliModelErrNotArray"));
            return rings;
        }
        for (int i = 0; i < arr.Count; i++)
            if (Points(arr[i], $"{path}[{i}]", errors) is { } r) rings.Add(r);
        return rings;
    }
}
