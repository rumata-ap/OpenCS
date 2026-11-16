namespace CScore.Sp63Punching;

/// <summary>Вид варианта расчётного контура.</summary>
public enum PunchingVariantKind
{
    /// <summary>Замкнутый контур вокруг площади (рис. 8.11а).</summary>
    Closed,

    /// <summary>Незамкнутый контур от одного края плиты (рис. 8.11б).</summary>
    OpenEdge,

    /// <summary>Незамкнутый контур от двух краёв — угол (рис. 8.11в).</summary>
    OpenCorner,

    /// <summary>Разорванный контур пилона: два П-контура у торцов (СП 430 рис. 6.1а, методика Гоши).</summary>
    PylonBroken,

    /// <summary>Справочный приведённый периметр по Галяутдинову (2022), только сила.</summary>
    PylonReduced,

    /// <summary>Контур пользователя.</summary>
    Custom,

    /// <summary>П-контур у торца стены (СП 430 п. 6.3.8, рис. 6.1, а).</summary>
    WallEnd,

    /// <summary>Контур 1′ у торца стены возле края плиты (рис. 6.1, б).</summary>
    WallEndEdge,

    /// <summary>Контур у угла стены (рис. 6.2): наружный Г и диагональ у внутреннего угла.</summary>
    WallCorner
}

/// <summary>Где проверяется контур.</summary>
public enum PunchingZone
{
    /// <summary>На расстоянии h0/2 от грузовой площади.</summary>
    Main,

    /// <summary>За зоной поперечной арматуры (п. 8.1.48, рис. 8.13) — только бетон.</summary>
    BeyondReinforcement
}

/// <summary>Вариант расчётного контура.</summary>
/// <param name="Id">Стабильный идентификатор (closed, open_edge_0, outer_closed…).</param>
/// <param name="Kind">Вид.</param>
/// <param name="Zone">Зона.</param>
/// <param name="Contour">Учитываемая часть контура.</param>
/// <param name="Excluded">Части, вырезанные отверстиями, — для рисунка.</param>
/// <param name="EdgeIndices">Края плиты, от которых идёт незамкнутый контур.</param>
public sealed record PunchingVariant(
    string Id, PunchingVariantKind Kind, PunchingZone Zone,
    PunchingContour Contour, IReadOnlyList<ContourElement> Excluded,
    IReadOnlyList<int> EdgeIndices);

/// <summary>Строит варианты расчётных контуров по пп. 8.1.46, 8.1.48 СП 63 и методике пилона.</summary>
public static class PunchingContourBuilder
{
    const double Tol = 1e-9;

    /// <summary>Результат построения: варианты и предупреждения.</summary>
    public sealed record BuildResult(IReadOnlyList<PunchingVariant> Variants, IReadOnlyList<PunchingWarning> Warnings);

    /// <summary>Строит все применимые варианты.</summary>
    public static BuildResult Build(PunchingInput input)
    {
        bool wallCrossIgnored = input.Wall is not null && input.Reinforcement?.Cross is not null;
        input = input.Normalized();
        if (input.Wall is { } wall) return BuildWall(input, wall, wallCrossIgnored);
        var warnings = new List<PunchingWarning>();
        var variants = new List<PunchingVariant>();
        var options = input.Options ?? new PunchingOptions();
        var edges = input.Edges ?? Array.Empty<PunchingSlabEdge>();
        double h0 = input.H0;

        if (input.LoadArea is PunchingLoadArea.Polygon poly && !IsConvex(poly.Points))
            warnings.Add(new("concave_area_hull",
                "Грузовая площадь невыпуклая — контур построен по выпуклой оболочке."));

        AddZone(input, PunchingZone.Main, ClosedBoundary(input.LoadArea, h0 / 2), variants, warnings);

        if (input.Reinforcement is { } rf && rf.EffectiveZoneExtent is double ext && ext > 0)
        {
            // За крестом — по диагоналям от концов лучей (п. 8.1.48, рис. 8.11г), иначе — на h0/2 за зоной (рис. 8.13).
            var outer = rf.Cross is { } cross && (cross.AlongX is { Any: true } || cross.AlongY is { Any: true })
                ? Lines(PunchingSwLayout.CrossBeyondPolygon(input.LoadArea, cross, ext, h0))
                : ClosedBoundary(input.LoadArea, ext + h0 / 2);
            AddZone(input, PunchingZone.BeyondReinforcement, outer, variants, warnings);
        }

        if (options.Pylon == PylonMode.Auto && input.LoadArea is PunchingLoadArea.Rectangle rect)
        {
            double cmin = Math.Min(rect.Bx, rect.By), cmax = Math.Max(rect.Bx, rect.By);
            if (cmax > 2 * cmin + h0 + Tol)
            {
                if (edges.Count > 0)
                    warnings.Add(new("pylon_at_edge_not_checked",
                        "Пилон у края плиты: разорванный контур не проверяется, только контуры по СП 63."));
                else
                {
                    variants.Add(ApplyHoles(input, Pylon(rect, h0), warnings));
                    if (cmin / h0 > 2 + Tol)
                        warnings.Add(new("pylon_cmin_h0_out_of_range",
                            $"Cmin/h0 = {cmin / h0:0.##} > 2: по Галяутдинову (2022, с. 162) контур СП 430 " +
                            "при Cmin/h0 ≥ 3 переоценивает прочность — сравните со справочным вариантом.",
                            [(cmin / h0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)]));
                }
            }
        }

        if (options.PylonReducedPerimeter && input.LoadArea is PunchingLoadArea.Rectangle && edges.Count == 0)
            variants.Add(new PunchingVariant("pylon_reduced", PunchingVariantKind.PylonReduced, PunchingZone.Main,
                PunchingContour.Empty, Array.Empty<ContourElement>(), Array.Empty<int>()));

        var customs = input.CustomContours ?? Array.Empty<PunchingCustomContour>();
        for (int i = 0; i < customs.Count; i++)
        {
            var c = customs[i];
            var els = new List<ContourElement>();
            for (int k = 0; k < c.Polygon.Count; k++)
                if (!c.InactiveEdges.Contains(k))
                    els.Add(new LineElement(c.Polygon[k], c.Polygon[(k + 1) % c.Polygon.Count]));
            var v = new PunchingVariant($"custom_{i}", PunchingVariantKind.Custom, PunchingZone.Main,
                PunchingContour.FromElements(els), Array.Empty<ContourElement>(), Array.Empty<int>());
            variants.Add(ApplyHoles(input, v, warnings));
        }

        return new BuildResult(variants, warnings);
    }

    /// <summary>
    /// Узел стены (СП 430 п. 6.3.8): торец — П-контур и контуры 1′ у краёв за торцом; угол — наружный Г и диагональ.
    /// Замкнутый контур СП 63 не строится (стена продолжается); края обрезают контур, отверстия — по п. 8.1.46.
    /// </summary>
    /// <summary>Предупреждение: у стены крестообразная арматура не применяется.</summary>
    internal static PunchingWarning WallCrossIgnoredWarning => new("wall_sw_cross_ignored",
        "У стены крестообразная поперечная арматура не применяется — арматура не учитывается. Задайте равномерную раскладку рядами.");

    static BuildResult BuildWall(PunchingInput input, PunchingWall wall, bool crossIgnored)
    {
        var warnings = new List<PunchingWarning>();
        var variants = new List<PunchingVariant>();
        if (crossIgnored) warnings.Add(WallCrossIgnoredWarning);
        double h0 = input.H0;
        AddWallZone(input, wall, PunchingZone.Main, h0 / 2, variants, warnings);
        if (input.Reinforcement is { } rf && rf.EffectiveZoneExtent is double ext && ext > 0)
            AddWallZone(input, wall, PunchingZone.BeyondReinforcement, ext + h0 / 2, variants, warnings);

        var customs = input.CustomContours ?? Array.Empty<PunchingCustomContour>();
        for (int i = 0; i < customs.Count; i++)
        {
            var c = customs[i];
            var els = new List<ContourElement>();
            for (int k = 0; k < c.Polygon.Count; k++)
                if (!c.InactiveEdges.Contains(k))
                    els.Add(new LineElement(c.Polygon[k], c.Polygon[(k + 1) % c.Polygon.Count]));
            variants.Add(ApplyHoles(input, new PunchingVariant($"custom_{i}", PunchingVariantKind.Custom, PunchingZone.Main,
                PunchingContour.FromElements(els), Array.Empty<ContourElement>(), Array.Empty<int>()), warnings));
        }
        return new BuildResult(variants, warnings);
    }

    static void AddWallZone(PunchingInput input, PunchingWall wall, PunchingZone zone, double d,
        List<PunchingVariant> variants, List<PunchingWarning> warnings)
    {
        var edges = input.Edges ?? Array.Empty<PunchingSlabEdge>();
        string prefix = zone == PunchingZone.Main ? "" : "outer_";
        bool end = wall.Kind == PunchingWallKind.End;
        var lines = PunchingWallGeometry.Lines(wall, d, input.H0);
        if (!end && wall.IsAmd2 && zone == PunchingZone.Main)
            warnings.Add(new("wall_corner_amd2_draft",
                "Контур у угла стен построен по рис. 6.2 проекта Изменения № 2 к СП 430 (не утверждён) — расчёт справочный."));
        var kept = edges.Aggregate((IEnumerable<ContourElement>)lines, (acc, e) => ClipBy(acc, e)).ToList();
        double full = lines.Sum(x => x.Length), left = kept.Sum(x => x.Length);
        if (left > Tol)
        {
            if (zone == PunchingZone.Main && full - left > 1e-9)
                warnings.Add(end
                    ? new("wall_end_clipped", "Край плиты ближе h0/2 к торцу или граням стены — П-контур обрезан краем, учитывается часть в пределах плиты.")
                    : new("wall_corner_clipped", "Край плиты пересекает контур у угла стен — учитывается часть контура в пределах плиты."));
            variants.Add(ApplyHoles(input, new PunchingVariant(prefix + (end ? "wall_end" : "wall_corner"),
                end ? PunchingVariantKind.WallEnd : PunchingVariantKind.WallCorner, zone,
                PunchingContour.FromElements(kept), Array.Empty<ContourElement>(), Array.Empty<int>()), warnings));
        }
        if (!end) return;
        for (int i = 0; i < edges.Count; i++)
        {
            if (PunchingWallGeometry.EdgeLines(wall, edges[i], d) is not { } els) continue;
            var clipped = Enumerable.Range(0, edges.Count).Where(j => j != i)
                .Aggregate((IEnumerable<ContourElement>)els, (acc, j) => ClipBy(acc, edges[j])).ToList();
            if (clipped.Sum(x => x.Length) <= Tol) continue;
            variants.Add(ApplyHoles(input, new PunchingVariant($"{prefix}wall_end_edge_{i}", PunchingVariantKind.WallEndEdge, zone,
                PunchingContour.FromElements(clipped), Array.Empty<ContourElement>(), new[] { i }), warnings));
        }
    }

    /// <summary>Центр, из которого проводятся касательные к отверстиям и считается эксцентриситет: центр тяжести площади
    /// или центр площадки передачи нагрузки узла стены.</summary>
    public static PlanPoint LoadCenter(PunchingInput input)
        => input.Wall is { } w ? PunchingWallGeometry.LoadCenter(w) : AreaCentroid(input.LoadArea);

    /// <summary>Основание пирамиды узла (колонна — площадь, смещённая на h0; стена — по <see cref="PunchingWallGeometry.BasePolygon"/>).</summary>
    public static List<PlanPoint> PyramidBasePolygon(PunchingInput input)
    {
        input = input.Normalized();
        var edges = input.Edges ?? Array.Empty<PunchingSlabEdge>();
        return input.Wall is { } w ? PunchingWallGeometry.BasePolygon(w, input.H0, edges) : PyramidBasePolygon(input.LoadArea, input.H0, edges);
    }

    /// <summary>Площадь основания пирамиды узла, м².</summary>
    public static double PyramidBaseArea(PunchingInput input)
    {
        var pts = PyramidBasePolygon(input);
        double a = 0;
        for (int i = 0; i < pts.Count; i++) a += pts[i].Cross(pts[(i + 1) % pts.Count]);
        return Math.Abs(a) / 2;
    }

    static void AddZone(PunchingInput input, PunchingZone zone, List<ContourElement> boundary,
        List<PunchingVariant> variants, List<PunchingWarning> warnings)
    {
        var edges = input.Edges ?? Array.Empty<PunchingSlabEdge>();
        string prefix = zone == PunchingZone.Main ? "" : "outer_";

        // Замкнутый — если целиком в плите.
        var closed = PunchingContour.FromElements(boundary);
        if (edges.All(e => !Clipped(boundary, e)))
            variants.Add(ApplyHoles(input,
                new PunchingVariant(prefix + "closed", PunchingVariantKind.Closed, zone, closed,
                    Array.Empty<ContourElement>(), Array.Empty<int>()), warnings));
        else if (zone == PunchingZone.Main)
            warnings.Add(new("closed_not_fit",
                "Замкнутый контур выходит за край плиты — проверяется только незамкнутый (п. 8.1.46)."));

        // Незамкнутый от каждого края — если не пересекает другие края.
        for (int i = 0; i < edges.Count; i++)
        {
            var els = SweptOpen(input.LoadArea, boundary, new[] { i }, edges);
            if (els.Count == 0) continue;
            bool crossesOther = Enumerable.Range(0, edges.Count).Where(j => j != i).Any(j => Clipped(els, edges[j]));
            if (crossesOther) continue;
            var clippedEls = ClipBy(els, edges[i]);
            variants.Add(ApplyHoles(input, new PunchingVariant($"{prefix}open_edge_{i}", PunchingVariantKind.OpenEdge,
                zone, PunchingContour.FromElements(clippedEls), Array.Empty<ContourElement>(), new[] { i }), warnings));
        }

        // Угол — от двух краёв.
        if (edges.Count >= 2)
            for (int i = 0; i < edges.Count; i++)
                for (int j = i + 1; j < edges.Count; j++)
                {
                    var els = SweptOpen(input.LoadArea, boundary, new[] { i, j }, edges);
                    if (els.Count == 0) continue;
                    var clippedEls = ClipBy(ClipBy(els, edges[i]), edges[j]);
                    bool crossesOther = Enumerable.Range(0, edges.Count).Where(k => k != i && k != j)
                        .Any(k => Clipped(clippedEls, edges[k]));
                    if (crossesOther) continue;
                    variants.Add(ApplyHoles(input, new PunchingVariant($"{prefix}open_corner_{i}_{j}",
                        PunchingVariantKind.OpenCorner, zone, PunchingContour.FromElements(clippedEls),
                        Array.Empty<ContourElement>(), new[] { i, j }), warnings));
                }
    }

    /// <summary>Замкнутая граница площади, смещённой на offset (многоугольник — острые углы, круг — окружность).</summary>
    public static List<ContourElement> ClosedBoundary(PunchingLoadArea area, double offset)
    {
        switch (area)
        {
            case PunchingLoadArea.Circle c:
                return new List<ContourElement> { new ArcElement(default, c.D / 2 + offset, 0, 2 * Math.PI) };
            case PunchingLoadArea.Ring r:
                return new List<ContourElement> { new ArcElement(default, r.D / 2 + offset, 0, 2 * Math.PI) };
            default:
                var pts = OffsetPolygon(AreaPolygon(area), offset);
                var els = new List<ContourElement>();
                for (int i = 0; i < pts.Count; i++)
                    els.Add(new LineElement(pts[i], pts[(i + 1) % pts.Count]));
                return els;
        }
    }

    static List<ContourElement> Lines(List<PlanPoint> pts)
    {
        var els = new List<ContourElement>();
        for (int i = 0; i < pts.Count; i++)
            els.Add(new LineElement(pts[i], pts[(i + 1) % pts.Count]));
        return els;
    }

    /// <summary>Многоугольник площади, смещённый наружу на d с острыми углами (круг — 720-угольник).</summary>
    public static List<PlanPoint> OffsetAreaPolygon(PunchingLoadArea area, double d) => OffsetPolygon(AreaPolygon(area), d);

    /// <summary>Выпуклая оболочка точек против часовой стрелки.</summary>
    public static List<PlanPoint> ConvexHullCcw(IReadOnlyList<PlanPoint> points) => ConvexHull(points);

    /// <summary>Выпуклый многоугольник площади против часовой стрелки.</summary>
    public static List<PlanPoint> AreaPolygon(PunchingLoadArea area) => area switch
    {
        PunchingLoadArea.Rectangle r => new List<PlanPoint>
        {
            new(-r.Bx / 2, -r.By / 2), new(r.Bx / 2, -r.By / 2), new(r.Bx / 2, r.By / 2), new(-r.Bx / 2, r.By / 2)
        },
        PunchingLoadArea.Polygon p => ConvexHull(p.Points),
        PunchingLoadArea.Circle c => RegularPolygon(c.D / 2, 720),
        PunchingLoadArea.Ring r => RegularPolygon(r.D / 2, 720),
        _ => throw new ArgumentOutOfRangeException(nameof(area))
    };

    /// <summary>Центр тяжести грузовой площади, м.</summary>
    public static PlanPoint AreaCentroid(PunchingLoadArea area)
    {
        if (area is not PunchingLoadArea.Polygon) return default;
        var pts = AreaPolygon(area);
        double a = 0, cx = 0, cy = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i]; var q = pts[(i + 1) % pts.Count];
            double cr = p.Cross(q);
            a += cr; cx += (p.X + q.X) * cr; cy += (p.Y + q.Y) * cr;
        }
        return Math.Abs(a) > 1e-18 ? new PlanPoint(cx / (3 * a), cy / (3 * a)) : pts[0];
    }

    /// <summary>
    /// Площадь основания пирамиды продавливания (площадь, смещённая на h0), обрезанная краями плиты, м².
    /// Круг приближается 720-угольником (погрешность площади ~1e-5).
    /// </summary>
    public static double PyramidBaseArea(PunchingLoadArea area, double h0, IReadOnlyList<PunchingSlabEdge> edges)
    {
        var pts = PyramidBasePolygon(area, h0, edges);
        double a = 0;
        for (int i = 0; i < pts.Count; i++) a += pts[i].Cross(pts[(i + 1) % pts.Count]);
        return Math.Abs(a) / 2;
    }

    /// <summary>
    /// Основание пирамиды продавливания — площадь, смещённая на h0 (острые углы), обрезанная краями плиты;
    /// выпуклый многоугольник против часовой стрелки. Круг — 720-угольник. Пусто — основание вне плиты.
    /// </summary>
    public static List<PlanPoint> PyramidBasePolygon(PunchingLoadArea area, double h0, IReadOnlyList<PunchingSlabEdge> edges)
    {
        var pts = OffsetPolygon(AreaPolygon(area), h0);
        foreach (var e in edges)
        {
            pts = ClipPolygon(pts, e);
            if (pts.Count < 3) return [];
        }
        return pts;
    }

    static PunchingVariant Pylon(PunchingLoadArea.Rectangle rect, double h0)
    {
        bool alongY = rect.By >= rect.Bx;
        double cmin = Math.Min(rect.Bx, rect.By), cmax = Math.Max(rect.Bx, rect.By);
        double w = cmin + h0, leg = cmin + h0, yEnd = cmax / 2 + h0 / 2;
        PlanPoint P(double u, double v) => alongY ? new PlanPoint(u, v) : new PlanPoint(v, -u);
        var els = new List<ContourElement>();
        foreach (int s in new[] { 1, -1 })
        {
            // П-контур: ветвь, торцевой отрезок, ветвь; обход против часовой стрелки.
            var a = P(s * w / 2, s * (yEnd - leg));
            var b = P(s * w / 2, s * yEnd);
            var c = P(-s * w / 2, s * yEnd);
            var d = P(-s * w / 2, s * (yEnd - leg));
            els.Add(new LineElement(a, b)); els.Add(new LineElement(b, c)); els.Add(new LineElement(c, d));
        }
        return new PunchingVariant("pylon_broken", PunchingVariantKind.PylonBroken, PunchingZone.Main,
            PunchingContour.FromElements(els), Array.Empty<ContourElement>(), Array.Empty<int>());
    }

    /// <summary>
    /// Незамкнутый контур от краёв: часть границы, чьи внешние нормали смотрят от всех краёв,
    /// и лучи к краям вдоль −n из её концов (выпуклая оболочка контура и его проекций на края).
    /// </summary>
    static List<ContourElement> SweptOpen(PunchingLoadArea area, List<ContourElement> boundary,
        int[] edgeIdx, IReadOnlyList<PunchingSlabEdge> edges)
    {
        var ns = edgeIdx.Select(i => edges[i].N).ToArray();
        var chain = new List<ContourElement>();
        int startEdge, endEdge;

        if (boundary.Count == 1 && boundary[0] is ArcElement circle)
        {
            double th0 = Math.Atan2(ns[0].Y, ns[0].X);
            var th = ns.Select(n => th0 + Wrap(Math.Atan2(n.Y, n.X) - th0)).ToArray();
            double lo = th.Max(t => t - Math.PI / 2), hi = th.Min(t => t + Math.PI / 2);
            if (hi - lo <= 1e-12) return chain;
            startEdge = edgeIdx[Array.IndexOf(th, th.First(t => Math.Abs(t - Math.PI / 2 - lo) < 1e-12))];
            endEdge = edgeIdx[Array.IndexOf(th, th.First(t => Math.Abs(t + Math.PI / 2 - hi) < 1e-12))];
            chain.Add(circle with { Phi1 = lo, Phi2 = hi });
        }
        else
        {
            var lines = boundary.Cast<LineElement>().ToList();
            int m = lines.Count;
            var normals = lines.Select(l => new PlanPoint((l.B - l.A).Y, -(l.B - l.A).X).Normalized()).ToList();
            var kept = normals.Select(nu => ns.All(n => nu.Dot(n) >= -1e-9)).ToArray();
            if (kept.All(k => !k) || kept.All(k => k)) return chain;
            int s = Enumerable.Range(0, m).First(i => kept[i] && !kept[(i - 1 + m) % m]);
            int idx = s;
            while (kept[idx]) { chain.Add(lines[idx]); idx = (idx + 1) % m; if (idx == s) break; }
            int prev = (s - 1 + m) % m, next = idx;
            startEdge = edgeIdx[Enumerable.Range(0, ns.Length).MinBy(k => normals[prev].Dot(ns[k]))];
            endEdge = edgeIdx[Enumerable.Range(0, ns.Length).MinBy(k => normals[next].Dot(ns[k]))];
        }

        var result = new List<ContourElement>();
        var p0 = chain[0].Start;
        double t0 = edges[startEdge].SignedDistance(p0);
        if (t0 > Tol) result.Add(new LineElement(p0 - t0 * edges[startEdge].N, p0));
        result.AddRange(chain);
        var p1 = chain[^1].End;
        double t1 = edges[endEdge].SignedDistance(p1);
        if (t1 > Tol) result.Add(new LineElement(p1, p1 - t1 * edges[endEdge].N));
        return result;
    }

    /// <summary>
    /// Направления касательных из центра тяжести площади к отверстию, если оно ближе HoleInfluence·h0
    /// (п. 8.1.46); null — отверстие на контур не влияет. Для рисунка.
    /// </summary>
    public static (PlanPoint Lo, PlanPoint Hi)? HoleTangents(PunchingInput input, IReadOnlyList<PlanPoint> hole)
    {
        input = input.Normalized();
        var options = input.Options ?? new PunchingOptions();
        if (hole.Count < 3 || DistanceToHole(input, hole) >= options.HoleInfluence * input.H0) return null;
        var o = LoadCenter(input);
        var hc = new PlanPoint(hole.Average(p => p.X), hole.Average(p => p.Y));
        double refAng = Math.Atan2(hc.Y - o.Y, hc.X - o.X);
        var angs = hole.Select(p => Wrap(Math.Atan2(p.Y - o.Y, p.X - o.X) - refAng)).ToList();
        double lo = angs.Min(), hi = angs.Max();
        if (hi - lo >= Math.PI - 1e-9) return null;
        return (PlanPoint.FromAngle(refAng + lo), PlanPoint.FromAngle(refAng + hi));
    }

    static PunchingVariant ApplyHoles(PunchingInput input, PunchingVariant v, List<PunchingWarning> warnings)
    {
        var holes = input.Holes ?? Array.Empty<PunchingHole>();
        if (holes.Count == 0 || v.Contour.Pieces.Count == 0) return v;
        var options = input.Options ?? new PunchingOptions();
        var o = LoadCenter(input);
        var kept = v.Contour.Elements.ToList();
        var excluded = new List<ContourElement>(v.Excluded);
        for (int h = 0; h < holes.Count; h++)
        {
            var hole = holes[h].Polygon;
            if (hole.Count < 3) continue;
            if (DistanceToHole(input, hole) >= options.HoleInfluence * input.H0) continue;
            var hc = new PlanPoint(hole.Average(p => p.X), hole.Average(p => p.Y));
            double refAng = Math.Atan2(hc.Y - o.Y, hc.X - o.X);
            var angs = hole.Select(p => Wrap(Math.Atan2(p.Y - o.Y, p.X - o.X) - refAng)).ToList();
            double lo = angs.Min(), hi = angs.Max();
            if (hi - lo >= Math.PI - 1e-9) continue;
            var dlo = PlanPoint.FromAngle(refAng + lo);
            var dhi = PlanPoint.FromAngle(refAng + hi);
            var a1 = new PlanPoint(-dlo.Y, dlo.X);
            var a2 = new PlanPoint(dhi.Y, -dhi.X);
            double k1 = a1.Dot(o), k2 = a2.Dot(o);
            var next = new List<ContourElement>();
            bool cut = false;
            foreach (var e in kept)
            {
                var inside1 = e.ClipToHalfPlane(a1, k1).ToList();
                var inWedge = inside1.SelectMany(x => x.ClipToHalfPlane(a2, k2)).ToList();
                if (inWedge.Sum(x => x.Length) <= 1e-12) { next.Add(e); continue; }
                cut = true;
                excluded.AddRange(inWedge);
                var outside = e.ClipToHalfPlane(new PlanPoint(-a1.X, -a1.Y), -k1)
                    .Concat(inside1.SelectMany(x => x.ClipToHalfPlane(new PlanPoint(-a2.X, -a2.Y), -k2)));
                next.AddRange(SortAlong(e, outside));
            }
            kept = next;
            if (cut)
                warnings.Add(new("hole_excluded",
                    $"Отверстие {h + 1} ближе {options.HoleInfluence:0.#}h0: часть контура «{v.Id}» между касательными не учитывается (п. 8.1.46).",
                    [(h + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                     options.HoleInfluence.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), v.Id]));
        }
        return v with { Contour = PunchingContour.FromElements(kept), Excluded = excluded };
    }

    static IEnumerable<ContourElement> SortAlong(ContourElement parent, IEnumerable<ContourElement> parts) => parent switch
    {
        LineElement l => parts.OrderBy(p => (p.Start - l.A).Length),
        _ => parts.OrderBy(p => p is ArcElement a ? a.Phi1 : 0)
    };

    /// <summary>Расстояние от грузовой площади до отверстия; у узла стены — до фактического (возможно, вогнутого) куска стены.</summary>
    static double DistanceToHole(PunchingInput input, IReadOnlyList<PlanPoint> hole)
        => input.Wall is { } w ? PolygonDistance(PunchingWallGeometry.AreaPolygon(w, input.H0), hole) : DistanceAreaToPolygon(input.LoadArea, hole);

    static double PolygonDistance(IReadOnlyList<PlanPoint> a, IReadOnlyList<PlanPoint> hole)
    {
        double d = double.MaxValue;
        for (int i = 0; i < a.Count; i++)
            for (int j = 0; j < hole.Count; j++)
            {
                d = Math.Min(d, PointSegment(a[i], hole[j], hole[(j + 1) % hole.Count]));
                d = Math.Min(d, PointSegment(hole[j], a[i], a[(i + 1) % a.Count]));
            }
        return d;
    }

    static double DistanceAreaToPolygon(PunchingLoadArea area, IReadOnlyList<PlanPoint> hole)
    {
        if (area is PunchingLoadArea.Circle or PunchingLoadArea.Ring)
        {
            double r = area is PunchingLoadArea.Circle c ? c.D / 2 : ((PunchingLoadArea.Ring)area).D / 2;
            double dc = Enumerable.Range(0, hole.Count)
                .Min(i => PointSegment(default, hole[i], hole[(i + 1) % hole.Count]));
            return Math.Max(0, dc - r);
        }
        var a = AreaPolygon(area);
        double d = double.MaxValue;
        for (int i = 0; i < a.Count; i++)
            for (int j = 0; j < hole.Count; j++)
            {
                d = Math.Min(d, PointSegment(a[i], hole[j], hole[(j + 1) % hole.Count]));
                d = Math.Min(d, PointSegment(hole[j], a[i], a[(i + 1) % a.Count]));
            }
        return d;
    }

    static double PointSegment(PlanPoint p, PlanPoint a, PlanPoint b)
    {
        var ab = b - a;
        double l2 = ab.Dot(ab);
        double t = l2 > 0 ? Math.Clamp((p - a).Dot(ab) / l2, 0, 1) : 0;
        return (p - (a + t * ab)).Length;
    }

    static bool Clipped(IEnumerable<ContourElement> els, PunchingSlabEdge e)
    {
        var list = els.ToList();
        double before = list.Sum(x => x.Length);
        double after = ClipBy(list, e).Sum(x => x.Length);
        return before - after > 1e-9;
    }

    static List<ContourElement> ClipBy(IEnumerable<ContourElement> els, PunchingSlabEdge e)
    {
        var n = e.N;
        double k = n.Dot(e.PointOnEdge);
        return els.SelectMany(x => x.ClipToHalfPlane(n, k)).ToList();
    }

    static List<PlanPoint> ClipPolygon(List<PlanPoint> pts, PunchingSlabEdge e)
    {
        var res = new List<PlanPoint>();
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i]; var q = pts[(i + 1) % pts.Count];
            double dp = e.SignedDistance(p), dq = e.SignedDistance(q);
            if (dp >= 0) res.Add(p);
            if ((dp >= 0) != (dq >= 0)) res.Add(p + dp / (dp - dq) * (q - p));
        }
        return res;
    }

    static double Wrap(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a <= -Math.PI) a += 2 * Math.PI;
        return a;
    }

    /// <summary>Смещение выпуклого многоугольника (против часовой) наружу с острыми углами.</summary>
    static List<PlanPoint> OffsetPolygon(List<PlanPoint> pts, double d)
    {
        int m = pts.Count;
        var res = new List<PlanPoint>(m);
        for (int i = 0; i < m; i++)
        {
            var p0 = pts[(i - 1 + m) % m]; var p1 = pts[i]; var p2 = pts[(i + 1) % m];
            var n1 = new PlanPoint((p1 - p0).Y, -(p1 - p0).X).Normalized();
            var n2 = new PlanPoint((p2 - p1).Y, -(p2 - p1).X).Normalized();
            // Точка пересечения смещённых прямых: p1 + d·(n1 + n2)/(1 + n1·n2).
            res.Add(p1 + (d / (1 + n1.Dot(n2))) * (n1 + n2));
        }
        return res;
    }

    static List<PlanPoint> RegularPolygon(double r, int n)
        => Enumerable.Range(0, n).Select(i => r * PlanPoint.FromAngle(2 * Math.PI * i / n)).ToList();

    static List<PlanPoint> ConvexHull(IReadOnlyList<PlanPoint> points)
    {
        var p = points.Distinct().OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
        if (p.Count < 3) return p;
        var h = new List<PlanPoint>();
        foreach (var pass in new[] { p, Enumerable.Reverse(p).ToList() })
        {
            int start = h.Count;
            foreach (var q in pass)
            {
                while (h.Count >= start + 2 && (h[^1] - h[^2]).Cross(q - h[^2]) <= 0) h.RemoveAt(h.Count - 1);
                h.Add(q);
            }
            h.RemoveAt(h.Count - 1);
        }
        return h;
    }

    static bool IsConvex(IReadOnlyList<PlanPoint> pts) => ConvexHull(pts).Count == pts.Distinct().Count();
}
