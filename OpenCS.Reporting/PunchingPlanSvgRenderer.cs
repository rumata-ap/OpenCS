using System.Globalization;
using System.Net;
using System.Text;
using CScore.Sp63Punching;

namespace OpenCS.Reporting;

/// <summary>Тексты продавливания для отчёта (русские подписи вариантов и статусов).</summary>
public static class PunchingReportText
{
    /// <summary>Порог «близко к пределу» — как у пакетов.</summary>
    public const double NearLimit = 0.99;

    /// <summary>Вывод подбора поперечной арматуры одной фразой.</summary>
    public static string DesignSentence(PunchingReportDesign d)
    {
        var c = CultureInfo.GetCultureInfo("ru-RU");
        string Q(double? v) => v is double x ? x.ToString("0.#", c) : "—";
        string req = d.Cross
            ? $"qsw,x ≥ {Q(d.QswXReq)} кН/м (лучи вдоль Y), qsw,y ≥ {Q(d.QswYReq)} кН/м (лучи вдоль X)"
            : $"qsw ≥ {Q(d.QswReq)} кН/м";
        string tail = d.BarDiameter is double dia
            ? $"; при заданных рядах и шагах — стержни Ø{(dia * 1000).ToString("0", c)}"
            : d.BarAreaReq is double ab ? $"; площадь стержня ≥ {(ab * 1e6).ToString("0.#", c)} мм² — больше Ø32, измените шаги или ряды" : "";
        string rows = d.RowsReq is int n ? $"; рядов не менее {n}" : d.ZoneReq is double z ? $"; зона армирования не менее {(z * 1000).ToString("0", c)} мм от грани" : "";
        string beyond = d.BeyondNotEnsured ? " Контур за армированием не проходит при любой ширине зоны — увеличьте толщину или класс бетона." : "";
        string have = d.ProvidedSufficient ? " Заданной арматуры достаточно." : "";
        return d.State switch
        {
            "not_required" => "Поперечная арматура по расчёту не требуется — прочность обеспечена бетоном.",
            "not_ensured" => "Арматурой прочность не обеспечить: K > 1 и при предельной арматуре (Fult = 2Fb, Mult = 2Mb) — увеличьте толщину плиты, класс бетона или размеры площадки (капитель).",
            "layout_ineffective" => "Заданная раскладка не пересекает определяющий контур или пирамиду продавливания (лучи креста мимо контура, ряды вне h0/3…h0) — измените раскладку.",
            "by_threshold" => $"Требуется {req} — по условию учёта арматуры Fsw,ult ≥ 0,25·Fb,ult (п. 8.1.48), по прочности хватило бы меньшей{tail}{rows}.{have}{beyond}",
            _ => $"Требуется {req} — из условия K = 1{tail}{rows}.{have}{beyond}"
        };
    }

    /// <summary>Название варианта контура.</summary>
    public static string VariantTitle(PunchingVariantKind kind, PunchingZone zone, string id, int edgeCount = 2)
    {
        string edgeNo = edgeCount > 1 ? EdgeNumber(id) : "";
        string name = kind switch
        {
            PunchingVariantKind.Closed => "Замкнутый контур (рис. 8.11а)",
            PunchingVariantKind.OpenEdge => $"Незамкнутый контур у края{edgeNo} (рис. 8.11б)",
            PunchingVariantKind.OpenCorner => "Незамкнутый контур у угла (рис. 8.11в)",
            PunchingVariantKind.PylonBroken => "Разорванный контур у торцов пилона (СП 430, п. 6.3.8)",
            PunchingVariantKind.PylonReduced => "Приведённый периметр пилона — справочно (Галяутдинов, 2022)",
            PunchingVariantKind.WallEnd => "П-образный контур у торца стены (СП 430, рис. 6.1а)",
            PunchingVariantKind.WallEndEdge => $"Контур 1′ у торца стены возле края{edgeNo} (СП 430, рис. 6.1б)",
            PunchingVariantKind.WallCorner => "Контур у угла стен (СП 430, рис. 6.2)",
            _ => "Контур пользователя"
        };
        return zone == PunchingZone.BeyondReinforcement
            ? name + " — за зоной поперечной арматуры (п. 8.1.48, рис. 8.13)"
            : name;
    }

    /// <summary>Вариант узла стены (СП 430 п. 6.3.8).</summary>
    public static bool IsWall(PunchingVariantKind kind)
        => kind is PunchingVariantKind.WallEnd or PunchingVariantKind.WallEndEdge or PunchingVariantKind.WallCorner;

    /// <summary>Пояснение к варианту узла стены для отчёта; null — не стена.</summary>
    public static string? WallExplanation(PunchingVariantKind kind) => kind switch
    {
        PunchingVariantKind.WallEnd => "Плита у торца стены (СП 430.1325800, п. 6.3.8, рис. 6.1а): контур на расстоянии h0/2 от граней стены "
            + "из торцевого участка длиной a1 + h0 и двух участков вдоль стены той же длины. Сила F — равнодействующая поперечных сил по "
            + "длине контура; момент — только из плоскости стены, равный половине разности изгибающих моментов плиты по обе стороны "
            + "стены (½ΔM). Эксцентриситет силы вдоль стены не учитывается.",
        PunchingVariantKind.WallEndEdge => "Торец стены у края плиты (СП 430.1325800, п. 6.3.8, рис. 6.1б): помимо П-образного контура "
            + "рассматривается контур 1′ — два участка от края плиты на расстоянии h0/2 от граней стены вдоль её длины на расстояние a1 от "
            + "торца; в расчёт принимается контур с меньшей несущей способностью.",
        PunchingVariantKind.WallCorner => "Плита у угла стен (СП 430.1325800, п. 6.3.8, рис. 6.2 в редакции с Изменением № 1): наружный "
            + "контур на расстоянии h0/2 от наружных граней с участками h0/2 + a1 + a2 и диагональ у внутреннего угла длиной √(a1² + a2²). "
            + "Учитывается только сила F — равнодействующая поперечных сил по всем участкам контура, моменты не учитываются.",
        _ => null
    };

    /// <summary>Краткое название варианта — заголовок столбца таблиц пакета.</summary>
    public static string ShortTitle(PunchingVariantKind kind, PunchingZone zone, string id, int edgeCount = 2)
    {
        string name = kind switch
        {
            PunchingVariantKind.Closed => "замкнутый",
            PunchingVariantKind.OpenEdge => "у края" + (edgeCount > 1 ? EdgeNumber(id) : ""),
            PunchingVariantKind.OpenCorner => "у угла",
            PunchingVariantKind.PylonBroken => "пилон",
            PunchingVariantKind.PylonReduced => "пилон, справочно",
            PunchingVariantKind.WallEnd => "торец, П",
            PunchingVariantKind.WallEndEdge => "торец, 1′" + (edgeCount > 1 ? EdgeNumber(id) : ""),
            PunchingVariantKind.WallCorner => "угол стен",
            _ => "пользователя " + CustomNumber(id)
        };
        return zone == PunchingZone.BeyondReinforcement ? name + ", за арматурой" : name;
    }

    static string CustomNumber(string id)
    {
        int i = id.LastIndexOf('_');
        return i >= 0 && int.TryParse(id[(i + 1)..], out int n) ? (n + 1).ToString(CultureInfo.InvariantCulture) : "";
    }

    static string EdgeNumber(string id)
    {
        int i = id.LastIndexOf('_');
        return (id.Contains("open_edge", StringComparison.Ordinal) || id.Contains("wall_end_edge", StringComparison.Ordinal)) && i >= 0 && int.TryParse(id[(i + 1)..], out int n)
            ? $" {n + 1}" : "";
    }

    /// <summary>Статус узла по коду JSON.</summary>
    public static string Status(string status) => status switch
    {
        "concrete_sufficient" => "прочность обеспечена бетоном",
        "reinforcement_required" => "требуется поперечное армирование",
        "not_ensured" => "прочность не обеспечена даже с предельным армированием (увеличить толщину, класс бетона или устроить капитель)",
        _ => status
    };

    /// <summary>Цвет вердикта: норма, близко к пределу, не проходит.</summary>
    public static string VerdictColor(double k) =>
        !double.IsFinite(k) || k > 1.0 ? "#c0392b" : k >= NearLimit ? "#b7791f" : "#2d7a3e";
}

/// <summary>SVG-план узла продавливания: площадь, края, отверстия, контуры (по образцу схем strength-web).</summary>
public sealed class PunchingPlanSvgRenderer
{
    const double Width = 640, Height = 560, Pad = 36, Top = 72, Bottom = 30;

    /// <summary>
    /// Рисует план. <paramref name="variantId"/> = null — все варианты, определяющий выделен;
    /// иначе — один вариант с центром тяжести и вырезанными частями.
    /// </summary>
    public string Render(PunchingScene scene, string? variantId = null, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var shown = variantId is null
            ? scene.Variants.Where(v => v.Pieces.Count > 0).ToList()
            : scene.Variants.Where(v => v.Id == variantId).ToList();

        var (x0, x1, y0, y1) = scene.Bounds();
        double span = Math.Max(x1 - x0, y1 - y0);
        double margin = Math.Max(0.15 * span, Math.Max(0.5 * scene.H0, 0.05));
        foreach (var e in scene.Edges)
        {
            // Край в пределах разумной близости — в поле рисунка.
            double d = (scene.AreaCentroid - e.Point).Dot(e.InwardNormal);
            if (Math.Abs(d) <= Math.Max(span, 6 * scene.H0))
            {
                var foot = scene.AreaCentroid - d * e.InwardNormal;
                x0 = Math.Min(x0, foot.X); x1 = Math.Max(x1, foot.X);
                y0 = Math.Min(y0, foot.Y); y1 = Math.Max(y1, foot.Y);
            }
        }
        x0 -= margin; x1 += margin; y0 -= margin; y1 += margin;
        double drawW = Width - 2 * Pad, drawH = Height - Top - Bottom;
        double scale = Math.Min(drawW / Math.Max(x1 - x0, 1e-9), drawH / Math.Max(y1 - y0, 1e-9));
        double ox = Pad + (drawW - (x1 - x0) * scale) / 2, oy = Top + (drawH - (y1 - y0) * scale) / 2;
        (double X, double Y) M(PlanPoint p) => (ox + (p.X - x0) * scale, oy + (y1 - p.Y) * scale);

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Width}\" height=\"{Height}\" viewBox=\"0 0 {Width} {Height}\" role=\"img\">");
        svg.Append("<title>").Append(E(title ?? "План узла продавливания")).AppendLine("</title>");
        svg.AppendLine("<defs><pattern id=\"hatch\" width=\"7\" height=\"7\" patternUnits=\"userSpaceOnUse\" patternTransform=\"rotate(45)\">"
            + "<line x1=\"0\" y1=\"0\" x2=\"0\" y2=\"7\" stroke=\"#6b7280\" stroke-width=\"1\"/></pattern>");
        svg.Append(CultureInfo.InvariantCulture,
            $"<clipPath id=\"plan\"><rect x=\"{F(ox)}\" y=\"{F(oy)}\" width=\"{F((x1 - x0) * scale)}\" height=\"{F((y1 - y0) * scale)}\"/></clipPath></defs>");
        svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
        svg.AppendLine("<g clip-path=\"url(#plan)\">");

        // За краем плиты — серая заливка «нет плиты».
        double big = 10 * (x1 - x0 + y1 - y0);
        foreach (var e in scene.Edges)
        {
            var n = e.InwardNormal;
            var t = new PlanPoint(-n.Y, n.X);
            var a = e.Point + big * t;
            var b = e.Point - big * t;
            svg.Append("<polygon points=\"").Append(Pts([a, b, b - big * n, a - big * n], M)).AppendLine("\" fill=\"#eceff3\"/>");
            svg.Append("<polyline points=\"").Append(Pts([a, b], M)).AppendLine("\" fill=\"none\" stroke=\"#111827\" stroke-width=\"3\"/>");
        }

        // Оси через центр тяжести площади.
        var c0 = scene.AreaCentroid;
        svg.Append("<polyline points=\"").Append(Pts([new(x0, c0.Y), new(x1, c0.Y)], M))
            .AppendLine("\" fill=\"none\" stroke=\"#9ca3af\" stroke-width=\"0.8\" stroke-dasharray=\"10 3 2 3\"/>");
        svg.Append("<polyline points=\"").Append(Pts([new(c0.X, y0), new(c0.X, y1)], M))
            .AppendLine("\" fill=\"none\" stroke=\"#9ca3af\" stroke-width=\"0.8\" stroke-dasharray=\"10 3 2 3\"/>");

        // Касательные к влияющим отверстиям: часть контура между ними не учитывается (п. 8.1.46).
        double rayLen = 2 * (x1 - x0 + y1 - y0);
        foreach (var r in scene.HoleRays ?? [])
            foreach (var dir in new[] { r.Lo, r.Hi })
                svg.Append("<polyline points=\"").Append(Pts([c0, c0 + rayLen * dir], M))
                    .AppendLine("\" fill=\"none\" stroke=\"#94a3b8\" stroke-width=\"0.9\" stroke-dasharray=\"4 3\"/>");

        // Отверстия.
        foreach (var h in scene.Holes)
        {
            svg.Append("<polygon points=\"").Append(Pts(h, M)).AppendLine("\" fill=\"white\" stroke=\"#475569\" stroke-width=\"1.4\"/>");
            if (h.Count >= 4)
            {
                var (hx0, hx1) = (h.Min(p => p.X), h.Max(p => p.X));
                var (hy0, hy1) = (h.Min(p => p.Y), h.Max(p => p.Y));
                svg.Append("<polyline points=\"").Append(Pts([new(hx0, hy0), new(hx1, hy1)], M)).AppendLine("\" stroke=\"#94a3b8\" stroke-width=\"0.8\"/>");
                svg.Append("<polyline points=\"").Append(Pts([new(hx0, hy1), new(hx1, hy0)], M)).AppendLine("\" stroke=\"#94a3b8\" stroke-width=\"0.8\"/>");
            }
        }

        // Грузовая площадь.
        svg.Append("<polygon points=\"").Append(Pts(scene.AreaOutline(), M))
            .AppendLine("\" fill=\"url(#hatch)\" stroke=\"#111827\" stroke-width=\"1.6\"/>");
        var inner = scene.AreaInnerOutline();
        if (inner.Count > 0)
            svg.Append("<polygon points=\"").Append(Pts(inner, M)).AppendLine("\" fill=\"white\" stroke=\"#111827\" stroke-width=\"1.2\"/>");

        // Поперечная арматура: стержни; у выделенного контура — участки под лучами креста.
        if (scene.SwBars is { Count: > 0 } bars)
        {
            double br = Math.Clamp(scale * 0.006, 1.0, 2.8);
            foreach (var b in bars)
            {
                var (bx, by) = M(b);
                svg.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{F(bx)}\" cy=\"{F(by)}\" r=\"{F(br)}\" fill=\"#2563eb\"/>");
            }
            svg.AppendLine();
        }
        var armedOf = variantId is not null ? shown.FirstOrDefault() : scene.Variants.FirstOrDefault(v => v.Governs);
        foreach (var e in armedOf?.Armed ?? [])
            svg.Append("<polyline points=\"").Append(Pts(PunchingScene.Tessellate(e), M))
                .AppendLine("\" fill=\"none\" stroke=\"#2563eb\" stroke-opacity=\"0.45\" stroke-width=\"8\"/>");

        // Контуры: прочие — штрих, определяющий (или единственный) — сплошной.
        foreach (var v in shown.OrderBy(v => v.Governs))
        {
            bool strong = variantId is not null || v.Governs;
            string color = PunchingReportText.VerdictColor(v.Utilization);
            foreach (var piece in v.Pieces)
            {
                var pts = piece.SelectMany(PunchingScene.Tessellate).ToList();
                svg.Append("<polyline points=\"").Append(Pts(pts, M)).Append("\" fill=\"none\" stroke=\"").Append(color)
                    .Append(strong ? "\" stroke-width=\"2.8\"" : "\" stroke-width=\"1.6\" stroke-dasharray=\"8 4\" stroke-opacity=\"0.85\"")
                    .AppendLine(" stroke-linejoin=\"round\"/>");
            }
            foreach (var x in v.Excluded)
                svg.Append("<polyline points=\"").Append(Pts(PunchingScene.Tessellate(x), M))
                    .AppendLine("\" fill=\"none\" stroke=\"#94a3b8\" stroke-width=\"1.2\" stroke-dasharray=\"2 3\"/>");
            if (strong)
            {
                var (cx, cy) = M(v.Centroid);
                svg.Append(CultureInfo.InvariantCulture,
                    $"<path d=\"M{F(cx - 7)},{F(cy)}H{F(cx + 7)}M{F(cx)},{F(cy - 7)}V{F(cy + 7)}\" stroke=\"{color}\" stroke-width=\"2\"/>");
                svg.AppendLine();
            }
        }
        svg.AppendLine("</g>");

        // Подписи осей.
        var (axX, axY) = M(new PlanPoint(x1, c0.Y));
        svg.Append(CultureInfo.InvariantCulture,
            $"<text x=\"{F(axX - 4)}\" y=\"{F(axY - 6)}\" text-anchor=\"end\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"13\" fill=\"#6b7280\">X</text>");
        var (ayX, ayY) = M(new PlanPoint(c0.X, y1));
        svg.Append(CultureInfo.InvariantCulture,
            $"<text x=\"{F(ayX + 6)}\" y=\"{F(ayY + 14)}\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"13\" fill=\"#6b7280\">Y</text>");

        // Подписи: длинный заголовок — в две строки (по « — »).
        string head = title ?? (variantId is null ? "Расчётные контуры" : "");
        string head2 = "";
        int dash = head.IndexOf(" — ", StringComparison.Ordinal);
        if (head.Length > 58 && dash > 0) { head2 = head[(dash + 3)..]; head = head[..dash]; }
        var gov = scene.Variants.FirstOrDefault(v => v.Governs);
        string sub = variantId is null
            ? (gov is null ? "" : $"определяющий: K = {K(gov.Utilization)}")
            : shown.Count == 1 ? $"K = {K(shown[0].Utilization)} {(shown[0].Utilization <= 1 ? "≤" : ">")} 1" : "";
        string subColor = variantId is null ? "#374151" : shown.Count == 1 ? PunchingReportText.VerdictColor(shown[0].Utilization) : "#374151";
        svg.Append(CultureInfo.InvariantCulture,
            $"<text x=\"{Width / 2}\" y=\"22\" text-anchor=\"middle\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"15\" fill=\"#111827\">{E(head)}</text>");
        svg.Append(CultureInfo.InvariantCulture,
            $"<text x=\"{Width / 2}\" y=\"40\" text-anchor=\"middle\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"12\" fill=\"#4b5563\">{E(head2)}</text>");
        svg.Append(CultureInfo.InvariantCulture,
            $"<text x=\"{Width / 2}\" y=\"{(head2.Length > 0 ? 60 : 44)}\" text-anchor=\"middle\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"13\" fill=\"{subColor}\">{E(sub)}</text>");
        // Пояснение арматуры — второй строкой: в одну строку шириной 640 оно не помещается.
        string swNote = scene.SwCross ? "синим — стержни и участки контура под лучами креста"
            : scene.SwBars is { Count: > 0 } ? "синим — стержни поперечной арматуры" : "";
        svg.Append(CultureInfo.InvariantCulture,
            $"<text x=\"{Pad}\" y=\"{Height - (swNote.Length > 0 ? 22 : 10)}\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"11\" fill=\"#6b7280\">{E($"h0 = {F(scene.H0 * 1000)} мм; + — центр тяжести контура; серым — вне плиты; серый пунктир — исключено у отверстий")}</text>");
        if (swNote.Length > 0)
            svg.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{Pad}\" y=\"{Height - 8}\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"11\" fill=\"#6b7280\">{E(swNote)}</text>");
        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    static string K(double k) => double.IsFinite(k) ? ReportNumberFormatter.FormatUtilization(k).Replace('.', ',') : "∞";

    static string Pts(IEnumerable<PlanPoint> pts, Func<PlanPoint, (double X, double Y)> map)
        => string.Join(" ", pts.Select(p => { var (x, y) = map(p); return F(x) + "," + F(y); }));

    static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    static string E(string s) => WebUtility.HtmlEncode(s);
}
