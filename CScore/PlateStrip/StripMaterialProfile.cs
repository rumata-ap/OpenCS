using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>
/// Где у полосы есть материал плиты: поперечное сечение полосы на станции пересекается с контуром региона
/// и его отверстиями (<see cref="PlateStripGeometry.MaterialHull"/>, <see cref="PlateStripGeometry.MaterialHoles"/>).
/// Отверстие, не перерезающее полосу целиком, сужает её; доля материала в ячейке квадратуры по ширине входит
/// в жёсткость множителем (<see cref="ScaledPlateSectionResponse"/>), поэтому полная ширина материала
/// учитывается точно по площади, а положения точек Гаусса не меняются. Без контура в геометрии
/// (старые записи, полосы без отверстий) материал считается сплошным по всей ширине.
/// </summary>
public static class StripMaterialProfile
{
    /// <summary>Доля ширины полосы, ниже которой материала на станции практически нет — полоса перерезана.</summary>
    public const double CutFraction = 0.01;

    /// <summary>Контур региона задан — профиль материала учитывается.</summary>
    public static bool IsDefined(PlateStripGeometry geometry) => geometry.MaterialHull.Count >= 3;

    /// <summary>
    /// Дополнить геометрию контуром региона, если профиль материала не задан (полоса, созданная вручную или
    /// сохранённая до учёта отверстий). Вызывается перед расчётом: сечение, ранее посчитанное по сплошной ширине,
    /// получит другой отпечаток и будет помечено устаревшим.
    /// </summary>
    public static void EnsureFromRegion(PlateStripGeometry geometry, PlanarRegion region)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(region);
        if (IsDefined(geometry) || region.Hull is null) return;
        geometry.MaterialHull = OpenLoop(region.Hull.X, region.Hull.Y);
        geometry.MaterialHoles = [.. region.Holes.Select(h => (IReadOnlyList<PlanarPoint2D>)OpenLoop(h.X, h.Y))];
    }

    /// <summary>Открытый обход контура (без повтора замыкающей точки).</summary>
    public static List<PlanarPoint2D> OpenLoop(IList<double> x, IList<double> y)
    {
        var (ox, oy) = PlanarRegionTopologyValidator.ToOpenLoop(x, y);
        var result = new List<PlanarPoint2D>(ox.Length);
        for (int i = 0; i < ox.Length; i++) result.Add(new PlanarPoint2D(ox[i], oy[i]));
        return result;
    }

    /// <summary>Интервалы поперечной координаты v ∈ [−b/2, +b/2], где на станции есть материал.</summary>
    public static IReadOnlyList<(double V0, double V1)> Intervals(PlateStripBeamAnalogy analogy, double stationFraction)
    {
        ArgumentNullException.ThrowIfNull(analogy);
        double b = analogy.ExplicitWidthM, h = b / 2.0;
        var geometry = analogy.Geometry;
        if (!IsDefined(geometry) || geometry.LeftBoundary.Count < 2 || geometry.RightBoundary.Count < 2)
            return [(-h, h)];

        double s = Math.Clamp(stationFraction, 0.0, 1.0);
        var right = Lerp(geometry.RightBoundary[0], geometry.RightBoundary[^1], s);
        var left = Lerp(geometry.LeftBoundary[0], geometry.LeftBoundary[^1], s);

        var cuts = new SortedSet<double> { 0.0, 1.0 };
        void AddCrossings(IReadOnlyList<PlanarPoint2D> loop)
        {
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                if (Crossing(right, left, loop[j], loop[i]) is double t && t > 0.0 && t < 1.0)
                    cuts.Add(t);
        }
        AddCrossings(geometry.MaterialHull);
        foreach (var hole in geometry.MaterialHoles) AddCrossings(hole);

        var result = new List<(double V0, double V1)>();
        double previous = double.NaN;
        foreach (double t in cuts)
        {
            if (!double.IsNaN(previous) && t - previous > 1e-12)
            {
                double mid = 0.5 * (previous + t);
                var p = new PlanarPoint2D(right.U + (left.U - right.U) * mid, right.V + (left.V - right.V) * mid);
                bool material = Inside(geometry.MaterialHull, p) && !geometry.MaterialHoles.Any(hole => Inside(hole, p));
                if (material)
                {
                    double v0 = -h + previous * b, v1 = -h + t * b;
                    if (result.Count > 0 && Math.Abs(result[^1].V1 - v0) < 1e-12)
                        result[^1] = (result[^1].V0, v1);
                    else
                        result.Add((v0, v1));
                }
            }
            previous = t;
        }
        return result;
    }

    /// <summary>
    /// Поперечная координата точки, где брать армирование: сама <paramref name="v"/>, если она в материале;
    /// иначе ближайший к ней край материала на станции. Точка квадратуры, попавшая в отверстие, не имеет
    /// собственного армирования, а её ячейка частично материальна — берётся армирование соседней кромки.
    /// Материала на станции нет — <paramref name="v"/> без изменений.
    /// </summary>
    public static double SnapToMaterial(PlateStripBeamAnalogy analogy, double stationFraction, double v)
    {
        var intervals = Intervals(analogy, stationFraction);
        if (intervals.Count == 0) return v;
        double best = v, distance = double.MaxValue;
        foreach (var (v0, v1) in intervals)
        {
            if (v >= v0 && v <= v1) return v;
            double edge = v < v0 ? v0 : v1;
            if (Math.Abs(edge - v) < distance) { distance = Math.Abs(edge - v); best = edge; }
        }
        return best;
    }

    /// <summary>Суммарная ширина материала на станции, м.</summary>
    public static double MaterialWidth(PlateStripBeamAnalogy analogy, double stationFraction) =>
        Intervals(analogy, stationFraction).Sum(i => i.V1 - i.V0);

    /// <summary>
    /// Доля материала в каждой ячейке квадратуры Гаусса по ширине (ячейка i — отрезок длиной в вес точки i,
    /// ячейки подряд от −b/2): Σ доля·вес = ширина материала.
    /// </summary>
    public static double[] CellFractions(PlateStripBeamAnalogy analogy, double stationFraction, int pointCount)
    {
        var (_, weights) = EquivalentSectionCalculator.WidthGaussPoints(analogy.ExplicitWidthM, pointCount);
        var intervals = Intervals(analogy, stationFraction);
        var result = new double[pointCount];
        double start = -analogy.ExplicitWidthM / 2.0;
        for (int i = 0; i < pointCount; i++)
        {
            double end = start + weights[i];
            double covered = 0.0;
            foreach (var (v0, v1) in intervals)
                covered += Math.Max(0.0, Math.Min(end, v1) - Math.Max(start, v0));
            result[i] = weights[i] > 0 ? Math.Clamp(covered / weights[i], 0.0, 1.0) : 1.0;
            start = end;
        }
        return result;
    }

    /// <summary>Доли материала в ячейках, усреднённые по элементу между станциями (отверстие короче
    /// элемента учитывается пропорционально своей длине).</summary>
    public static double[] ElementFractions(
        PlateStripBeamAnalogy analogy, double fromFraction, double toFraction, int pointCount, int samples = 9)
    {
        var result = new double[pointCount];
        for (int k = 0; k < samples; k++)
        {
            double s = fromFraction + (toFraction - fromFraction) * (k + 0.5) / samples;
            var cells = CellFractions(analogy, s, pointCount);
            for (int i = 0; i < pointCount; i++) result[i] += cells[i] / samples;
        }
        return result;
    }

    /// <summary>Наименьшая ширина материала вдоль оси и станция, на которой она достигается. Станции — середины
    /// 200 равных отрезков: сечение, совпадающее с ребром отверстия на конце оси, перерезанием не считается.</summary>
    public static (double MinWidthM, double AtFraction) Narrowest(PlateStripBeamAnalogy analogy)
    {
        double min = double.MaxValue, at = 0.0;
        const int count = 200;
        for (int i = 0; i < count; i++)
        {
            double s = (i + 0.5) / count;
            double w = MaterialWidth(analogy, s);
            if (w < min) { min = w; at = s; }
        }
        return (min, at);
    }

    static PlanarPoint2D Lerp(PlanarPoint2D a, PlanarPoint2D b, double t) =>
        new(a.U + (b.U - a.U) * t, a.V + (b.V - a.V) * t);

    /// <summary>Параметр t ∈ [0,1] пересечения отрезка right→left с отрезком p→q; null — нет пересечения.</summary>
    static double? Crossing(PlanarPoint2D right, PlanarPoint2D left, PlanarPoint2D p, PlanarPoint2D q)
    {
        double rx = left.U - right.U, ry = left.V - right.V;
        double sx = q.U - p.U, sy = q.V - p.V;
        double denom = rx * sy - ry * sx;
        if (Math.Abs(denom) < 1e-14) return null;
        double dx = p.U - right.U, dy = p.V - right.V;
        double t = (dx * sy - dy * sx) / denom;
        double u = (dx * ry - dy * rx) / denom;
        return u >= 0.0 && u <= 1.0 ? t : null;
    }

    static bool Inside(IReadOnlyList<PlanarPoint2D> loop, PlanarPoint2D p)
    {
        bool inside = false;
        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            var a = loop[i];
            var b = loop[j];
            if ((a.V > p.V) != (b.V > p.V) && p.U < (b.U - a.U) * (p.V - a.V) / (b.V - a.V) + a.U)
                inside = !inside;
        }
        return inside;
    }
}
