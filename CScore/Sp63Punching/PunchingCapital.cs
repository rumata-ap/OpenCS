namespace CScore.Sp63Punching;

/// <summary>
/// Капитель (надколонное утолщение) — прямоугольная или круглая ступень под плитой.
/// Методика (решение пользователя 17.11.2026, пп. 8.1.46–8.1.52 СП 63 без новых формул): у колонны — рабочая высота
/// плиты с утолщением h0 + hc; за капителью — грузовая площадь по грани капители и рабочая высота плиты.
/// </summary>
/// <param name="Area">Капитель в плане: прямоугольник или круг с центром в (0; 0).</param>
/// <param name="Hc">Высота утолщения под плитой, м.</param>
/// <param name="Offset">Смещение центра капители от центра тяжести колонны, м (у колонн у края — капитель по край
/// плиты). У круглой капители — только (0; 0).</param>
public sealed record PunchingCapital(PunchingLoadArea Area, double Hc, PlanPoint Offset = default);

/// <summary>Разбиение узла с капителью на две проверки и их объединение в один результат.</summary>
public static class PunchingCapitalCheck
{
    const double Tol = 1e-9;

    /// <summary>Капитель в осях плиты как грузовая площадь проверки за капителью.</summary>
    public static PunchingLoadArea CapitalArea(PunchingInput input)
    {
        var cap = input.Capital ?? throw new ArgumentException("Капитель не задана.", nameof(input));
        var c = PunchingContourBuilder.AreaCentroid(input.LoadArea) + cap.Offset;
        if (Math.Abs(c.X) < Tol && Math.Abs(c.Y) < Tol) return cap.Area;
        return new PunchingLoadArea.Polygon(PunchingContourBuilder.AreaPolygon(cap.Area).Select(p => p + c).ToList());
    }

    /// <summary>Многоугольник капители против часовой стрелки (круг — 720-угольник).</summary>
    public static List<PlanPoint> CapitalPolygon(PunchingInput input) => PunchingContourBuilder.AreaPolygon(CapitalArea(input));

    /// <summary>
    /// Проверка исходных данных капители: ошибка — исключение. Капитель содержит колонну, высота &gt; 0,
    /// у круглой — без смещения, узел стены не поддерживается.
    /// </summary>
    public static void Validate(PunchingInput input)
    {
        if (input.Capital is not { } cap) return;
        if (input.Wall is not null) throw new ArgumentException("Капитель у узла стены не поддерживается.", nameof(input));
        if (!(cap.Hc > 0)) throw new ArgumentException("Высота капители должна быть больше нуля.", nameof(input));
        if (cap.Area is PunchingLoadArea.Circle or PunchingLoadArea.Ring && (Math.Abs(cap.Offset.X) > Tol || Math.Abs(cap.Offset.Y) > Tol))
            throw new ArgumentException("Круглая капитель задаётся без смещения от центра колонны.", nameof(input));
        if (!Contains(CapitalPolygon(input), PunchingContourBuilder.AreaPolygon(input.LoadArea), 1e-6))
            throw new ArgumentException("Капитель в плане должна включать сечение колонны.", nameof(input));
    }

    /// <summary>Контур у колонны (h0к/2 от грани, h0к = h0 + hc) целиком в пределах капители.</summary>
    public static bool ColumnContourInside(PunchingInput input)
    {
        var cap = input.Capital!;
        var contour = PunchingContourBuilder.OffsetAreaPolygon(input.LoadArea, (input.H0 + cap.Hc) / 2);
        return Contains(CapitalPolygon(input), contour, 1e-6);
    }

    /// <summary>Рабочая высота у колонны: h0 + hc, если контур у колонны в пределах капители; без капители — h0.</summary>
    public static double ColumnH0(PunchingInput input) => input.Capital is null ? input.H0 : ColumnPart(input).H0;

    /// <summary>Вход проверки у колонны: h0 + hc и h + hc (если контур в капители), без капители.</summary>
    public static PunchingInput ColumnPart(PunchingInput input)
    {
        var cap = input.Capital!;
        bool inside = ColumnContourInside(input);
        return input with
        {
            Capital = null,
            H0 = inside ? input.H0 + cap.Hc : input.H0,
            H = inside ? input.SlabH + cap.Hc : input.SlabH,
        };
    }

    /// <summary>
    /// Вход проверки за капителью: грузовая площадь — капитель, h0 плиты, без поперечной арматуры, пилона и
    /// контуров пользователя; эксцентриситет — от центра колонны.
    /// </summary>
    public static PunchingInput CapitalPart(PunchingInput input)
    {
        var o = input.Options ?? new PunchingOptions();
        return input with
        {
            Capital = null,
            LoadArea = CapitalArea(input),
            H = input.SlabH,
            Reinforcement = null,
            CustomContours = null,
            Options = o with { Pylon = PylonMode.Off, PylonReducedPerimeter = false, PylonReducedGoverns = false },
            ForcePoint = PunchingContourBuilder.LoadCenter(input),
            IsCapitalPart = true,
        };
    }

    internal static PunchingResult Check(PunchingInput input, bool includeTrace, PunchingGeometryCache? geometry, double swScale)
    {
        Validate(input);
        var warnings = new List<PunchingWarning>();
        var cap = input.Capital!;
        var options = input.Options ?? new PunchingOptions();
        var col = ColumnPart(input);
        bool inside = col.H0 > input.H0;
        if (!inside)
            warnings.Add(new("capital_too_small",
                $"Контур у колонны (h0к/2 от грани, h0к = h0 + hc = {(input.H0 + cap.Hc) * 1000:0} мм) выходит за капитель — " +
                "у колонны принята рабочая высота плиты без утолщения.",
                [((input.H0 + cap.Hc) * 1000).ToString("0", System.Globalization.CultureInfo.InvariantCulture)]));
        var rCol = PunchingChecker.CheckSingle(col, includeTrace, geometry, swScale);
        var rCap = PunchingChecker.CheckSingle(CapitalPart(input), includeTrace, geometry?.CapitalPart, swScale);

        var colVariants = rCol.Variants.ToList();
        // Контур за зоной поперечной арматуры, вышедший за капитель, — с рабочей высотой плиты.
        if (inside && colVariants.Any(v => v.Variant.Zone == PunchingZone.BeyondReinforcement))
        {
            var capPoly = CapitalPolygon(input);
            var outside = colVariants.Where(v => v.Variant.Zone == PunchingZone.BeyondReinforcement
                && !v.Variant.Contour.Elements.SelectMany(PunchingScene.Tessellate).All(p => Inside(capPoly, p, 1e-6)))
                .Select(v => v.Variant.Id).ToHashSet(StringComparer.Ordinal);
            if (outside.Count > 0)
            {
                var thin = PunchingChecker.CheckSingle(col with { H0 = input.H0, H = input.SlabH }, includeTrace, null, swScale);
                for (int i = 0; i < colVariants.Count; i++)
                    if (outside.Contains(colVariants[i].Variant.Id)
                        && thin.Variants.FirstOrDefault(t => t.Variant.Id == colVariants[i].Variant.Id) is { } t)
                        colVariants[i] = t;
                warnings.Add(new("capital_sw_beyond",
                    "Зона поперечной арматуры выходит за капитель — контур за зоной арматуры проверен с рабочей высотой плиты."));
            }
        }

        var all = colVariants.Concat(rCap.Variants).Select(v => v with
        {
            Governs = false,
            Trace = includeTrace ? PunchingTrace.Capital(v, input, cap.Hc).Concat(v.Trace).ToList() : v.Trace,
        }).ToList();
        int gov = -1;
        double util = 0;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Variant.Kind == PunchingVariantKind.PylonReduced && !options.PylonReducedGoverns) continue;
            if (gov < 0 || all[i].Utilization > util) { gov = i; util = all[i].Utilization; }
        }
        if (gov >= 0) all[gov] = all[gov] with { Governs = true };

        // За капителью поперечная арматура не моделируется: «с предельной арматурой» там = по бетону.
        var main = all.Where(v => v.Variant.Zone == PunchingZone.Main
            && (v.Variant.Kind != PunchingVariantKind.PylonReduced || options.PylonReducedGoverns)).ToList();
        var beyondCap = all.Where(v => v.Variant.Zone == PunchingZone.BeyondCapital).ToList();
        double kb = main.Concat(beyondCap).Select(v => v.UtilizationConcreteOnly).DefaultIfEmpty(0).Max();
        double kmax = main.Select(v => v.UtilizationMaxReinforcement)
            .Concat(beyondCap.Select(v => v.UtilizationConcreteOnly)).DefaultIfEmpty(0).Max();
        var status = kb <= 1.0 ? PunchingStatus.ConcreteSufficient
            : kmax <= 1.0 ? PunchingStatus.ReinforcementRequired
            : PunchingStatus.NotEnsured;
        if (beyondCap.Any(v => v.UtilizationConcreteOnly > 1.0))
            warnings.Add(new("capital_beyond_fails",
                "Не проходит проверка за капителью — поперечная арматура у колонны её не исправит: увеличьте капитель или толщину плиты."));

        var allWarnings = warnings.Concat(rCol.Warnings).Concat(rCap.Warnings)
            .Where(w => w.Code != "no_variants" || gov < 0)
            .DistinctBy(w => w.Code + w.Text).ToList();
        return new PunchingResult(all, gov, util, status, kb, kmax, rCol.Fred, rCol.BaseArea, allWarnings, rCol.Detailing, rCol.Rows);
    }

    static bool Contains(IReadOnlyList<PlanPoint> convexCcw, IEnumerable<PlanPoint> pts, double tol)
        => pts.All(p => Inside(convexCcw, p, tol));

    static bool Inside(IReadOnlyList<PlanPoint> convexCcw, PlanPoint p, double tol)
    {
        for (int i = 0; i < convexCcw.Count; i++)
        {
            var a = convexCcw[i];
            var b = convexCcw[(i + 1) % convexCcw.Count];
            var ab = b - a;
            double len = ab.Length;
            if (len < 1e-15) continue;
            if (ab.Cross(p - a) / len < -tol) return false;
        }
        return true;
    }
}
