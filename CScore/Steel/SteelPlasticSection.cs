namespace CScore;

/// <summary>
/// Пластический момент сопротивления Wpl,x / Wpl,y стального сечения (класс 1 по
/// СП 16.13330.2017 — работа в пластическом шарнире). Порт идеи из sectionproperties
/// (analysis/plastic_section.py + pre/bisect_section.py), адаптированный под то, что
/// <see cref="SteelSection"/> — всегда один однородный материал (одно значение Ry):
/// для однородного сечения условие равновесия пластических сил F_top=F_bot·Ry сводится
/// к условию равенства ПЛОЩАДЕЙ (Ry сокращается) — пластическая нейтральная ось есть
/// ось, делящая площадь сечения пополам. Отсекание полуплоскостью — через уже имеющийся
/// <see cref="GridSplit.ClipByHalfPlane"/> (Сазерленд–Ходжман), площадь/центроид половин —
/// через <see cref="WktHelper.PolygonArea"/>/<see cref="WktHelper.PolygonCentroid"/>.
/// Поиск оси — обычная бисекция (функция «площадь выше линии» непрерывна и монотонна).
/// </summary>
public static class SteelPlasticSection
{
    public readonly struct Result
    {
        /// <summary>Пластический момент сопротивления относительно оси X (для изгиба Mx), м³.</summary>
        public required double WplX { get; init; }
        /// <summary>Пластический момент сопротивления относительно оси Y (для изгиба My), м³.</summary>
        public required double WplY { get; init; }
        /// <summary>Координата Y пластической нейтральной оси (горизонтальной, для WplX), м.</summary>
        public required double YPna { get; init; }
        /// <summary>Координата X пластической нейтральной оси (вертикальной, для WplY), м.</summary>
        public required double XPna { get; init; }
    }

    const int BisectionIterations = 60;

    public static Result Compute(SteelSection section)
    {
        var outer = section.OuterContour;
        var holes = section.InnerContours;
        double totalArea = section.Area;

        (double yMin, double yMax, double xMin, double xMax) = Bounds(outer);

        double yPna = BisectAxis(outer, holes, totalArea,
            d => AreaAbove(outer, holes, px: 0, py: d, nx: 0, ny: 1), yMin, yMax);
        var (areaTop, _, cyTop) = ClippedAreaCentroid(outer, holes, 0, yPna, 0, 1);
        var (areaBot, _, cyBot) = ClippedAreaCentroid(outer, holes, 0, yPna, 0, -1);
        double wplX = areaTop * (cyTop - yPna) + areaBot * (yPna - cyBot);

        double xPna = BisectAxis(outer, holes, totalArea,
            d => AreaAbove(outer, holes, px: d, py: 0, nx: 1, ny: 0), xMin, xMax);
        var (areaRight, cxRight, _) = ClippedAreaCentroid(outer, holes, xPna, 0, 1, 0);
        var (areaLeft, cxLeft, _) = ClippedAreaCentroid(outer, holes, xPna, 0, -1, 0);
        double wplY = areaRight * (cxRight - xPna) + areaLeft * (xPna - cxLeft);

        return new Result { WplX = wplX, WplY = wplY, YPna = yPna, XPna = xPna };
    }

    /// <summary>Площадь части сечения по сторону нормали n от точки (p): (v-p)·n ≥ 0.</summary>
    static double AreaAbove(List<(double X, double Y)> outer, List<List<(double X, double Y)>> holes,
        double px, double py, double nx, double ny)
        => ClippedAreaCentroid(outer, holes, px, py, nx, ny).area;

    static double BisectAxis(List<(double X, double Y)> outer, List<List<(double X, double Y)>> holes,
        double totalArea, Func<double, double> areaAboveAt, double lo, double hi)
    {
        double target = totalArea / 2.0;
        double a = lo, b = hi;
        for (int i = 0; i < BisectionIterations; i++)
        {
            double mid = 0.5 * (a + b);
            double areaAbove = areaAboveAt(mid);
            // areaAboveAt монотонно убывает по d: если площадь выше линии больше половины,
            // значит линия ещё слишком низко — двигаем нижнюю границу вверх.
            if (areaAbove > target) a = mid; else b = mid;
        }
        return 0.5 * (a + b);
    }

    static (double area, double cx, double cy) ClippedAreaCentroid(
        List<(double X, double Y)> outer, List<List<(double X, double Y)>> holes,
        double px, double py, double nx, double ny)
    {
        var (oArea, ocx, ocy) = RingAreaCentroid(GridSplit.ClipByHalfPlane(outer, px, py, nx, ny));
        double area = oArea;
        double mx = ocx * oArea, my = ocy * oArea;
        foreach (var hole in holes)
        {
            var (hArea, hcx, hcy) = RingAreaCentroid(GridSplit.ClipByHalfPlane(hole, px, py, nx, ny));
            area -= hArea;
            mx -= hcx * hArea;
            my -= hcy * hArea;
        }
        if (area < 1e-14) return (0.0, 0.0, 0.0);
        return (area, mx / area, my / area);
    }

    static (double area, double cx, double cy) RingAreaCentroid(List<(double X, double Y)> verts)
    {
        if (verts.Count < 3) return (0.0, 0.0, 0.0);
        var xs = verts.Select(v => v.X).ToList(); xs.Add(xs[0]);
        var ys = verts.Select(v => v.Y).ToList(); ys.Add(ys[0]);
        double area = WktHelper.PolygonArea(xs, ys);
        var (cx, cy) = WktHelper.PolygonCentroid(xs, ys);
        return (area, cx, cy);
    }

    static (double yMin, double yMax, double xMin, double xMax) Bounds(List<(double X, double Y)> outer)
    {
        double yMin = double.MaxValue, yMax = double.MinValue, xMin = double.MaxValue, xMax = double.MinValue;
        foreach (var (x, y) in outer)
        {
            if (x < xMin) xMin = x;
            if (x > xMax) xMax = x;
            if (y < yMin) yMin = y;
            if (y > yMax) yMax = y;
        }
        return (yMin, yMax, xMin, xMax);
    }
}
