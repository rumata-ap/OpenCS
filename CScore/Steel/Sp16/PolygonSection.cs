namespace CScore.Sp16;

/// <summary>
/// Геометрия многосвязного полигонального сечения (внешний контур + отверстия) для
/// стальных проверок: площадь, центр тяжести, моменты инерции, статический момент части
/// сечения по одну сторону от прямой и суммарная длина хорды (толщина, пересекаемая прямой).
/// Координаты — м. Ориентация колец не важна: отверстия всегда вычитаются.
/// </summary>
public sealed class PolygonSection
{
    readonly List<(double X, double Y)> _outer;
    readonly List<List<(double X, double Y)>> _holes;

    /// <summary>Площадь, м².</summary>
    public double A { get; }
    /// <summary>Центр тяжести, м.</summary>
    public double Xc { get; }
    /// <summary>Центр тяжести, м.</summary>
    public double Yc { get; }
    /// <summary>Момент инерции относительно центральной оси, параллельной x, м⁴.</summary>
    public double Ix { get; }
    /// <summary>Момент инерции относительно центральной оси, параллельной y, м⁴.</summary>
    public double Iy { get; }
    /// <summary>Центробежный момент инерции относительно центральных осей, м⁴.</summary>
    public double Ixy { get; }
    /// <summary>Габарит, м.</summary>
    public double XMin { get; }
    /// <summary>Габарит, м.</summary>
    public double XMax { get; }
    /// <summary>Габарит, м.</summary>
    public double YMin { get; }
    /// <summary>Габарит, м.</summary>
    public double YMax { get; }

    /// <summary>Внешний контур.</summary>
    public IReadOnlyList<(double X, double Y)> Outer => _outer;
    /// <summary>Отверстия.</summary>
    public IReadOnlyList<List<(double X, double Y)>> Holes => _holes;

    public PolygonSection(IEnumerable<(double X, double Y)> outer,
        IEnumerable<IEnumerable<(double X, double Y)>>? holes = null)
    {
        _outer = Clean(outer);
        _holes = (holes ?? []).Select(Clean).Where(h => h.Count >= 3).ToList();
        if (_outer.Count < 3) throw new ArgumentException("Контур сечения должен иметь не менее трёх вершин");

        var (a, sx, sy, ixx, iyy, ixy) = RingMoments(_outer);
        foreach (var h in _holes)
        {
            var (ha, hsx, hsy, hixx, hiyy, hixy) = RingMoments(h);
            a -= ha; sx -= hsx; sy -= hsy; ixx -= hixx; iyy -= hiyy; ixy -= hixy;
        }
        if (a <= 0) throw new ArgumentException("Площадь сечения должна быть положительной");
        A = a;
        Xc = sy / a;
        Yc = sx / a;
        Ix = ixx - a * Yc * Yc;
        Iy = iyy - a * Xc * Xc;
        Ixy = ixy - a * Xc * Yc;
        XMin = _outer.Min(p => p.X); XMax = _outer.Max(p => p.X);
        YMin = _outer.Min(p => p.Y); YMax = _outer.Max(p => p.Y);
    }

    static List<(double X, double Y)> Clean(IEnumerable<(double X, double Y)> ring)
    {
        var pts = ring.ToList();
        if (pts.Count > 1 && Math.Abs(pts[0].X - pts[^1].X) < 1e-12 && Math.Abs(pts[0].Y - pts[^1].Y) < 1e-12)
            pts.RemoveAt(pts.Count - 1);
        return pts;
    }

    /// <summary>Интегралы по кольцу (по модулю, т.е. независимо от обхода): A, ∫y, ∫x, ∫y², ∫x², ∫xy.</summary>
    static (double A, double Sx, double Sy, double Ixx, double Iyy, double Ixy) RingMoments(
        IReadOnlyList<(double X, double Y)> p)
    {
        double a = 0, sx = 0, sy = 0, ixx = 0, iyy = 0, ixy = 0;
        int n = p.Count;
        for (int i = 0; i < n; i++)
        {
            var (x0, y0) = p[i];
            var (x1, y1) = p[(i + 1) % n];
            double c = x0 * y1 - x1 * y0;
            a += c;
            sy += (x0 + x1) * c;
            sx += (y0 + y1) * c;
            iyy += (x0 * x0 + x0 * x1 + x1 * x1) * c;
            ixx += (y0 * y0 + y0 * y1 + y1 * y1) * c;
            ixy += (x0 * y1 + 2 * x0 * y0 + 2 * x1 * y1 + x1 * y0) * c;
        }
        double s = a >= 0 ? 1 : -1;
        return (s * a / 2, s * sx / 6, s * sy / 6, s * ixx / 12, s * iyy / 12, s * ixy / 24);
    }

    /// <summary>Главные центральные моменты инерции (Imax, Imin), м⁴.</summary>
    public (double IMax, double IMin) PrincipalInertia()
    {
        double c = (Ix + Iy) / 2, r = Math.Sqrt(Math.Pow((Ix - Iy) / 2, 2) + Ixy * Ixy);
        return (c + r, c - r);
    }

    /// <summary>
    /// Площадь и статический момент (относительно центральной оси, параллельной прямой) части
    /// сечения выше горизонтальной прямой y = y0 (<paramref name="axisX"/> = true) или правее
    /// вертикальной прямой x = x0.
    /// </summary>
    public (double Area, double S) PartAbove(double level, bool axisX)
    {
        // Полуплоскость (v - p)·n ≥ 0.
        double px = axisX ? 0 : level, py = axisX ? level : 0;
        double nx = axisX ? 0 : 1, ny = axisX ? 1 : 0;
        double area = 0, moment = 0;
        void Add(IReadOnlyList<(double X, double Y)> ring, double sign)
        {
            var clipped = GridSplit.ClipByHalfPlane(ring.ToList(), px, py, nx, ny);
            if (clipped.Count < 3) return;
            var (a, sx, sy, _, _, _) = RingMoments(clipped);
            area += sign * a;
            moment += sign * (axisX ? sx - a * Yc : sy - a * Xc);
        }
        Add(_outer, 1);
        foreach (var h in _holes) Add(h, -1);
        return (area, moment);
    }

    /// <summary>
    /// Суммарная длина отрезков, по которым прямая y = level (axisX = true) или x = level
    /// пересекает материал сечения (толщина стенок на этом уровне), м.
    /// </summary>
    public double ChordLength(double level, bool axisX) =>
        ChordSegments(level, axisX).Sum(s => s.To - s.From);

    /// <summary>
    /// Отрезки материала на прямой y = level (axisX = true, координаты отрезков — x) или
    /// x = level (координаты — y), упорядоченные по возрастанию.
    /// </summary>
    public List<(double From, double To)> ChordSegments(double level, bool axisX)
    {
        var cuts = new List<double>();
        void Collect(IReadOnlyList<(double X, double Y)> ring)
        {
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % n];
                double ua = axisX ? a.Y : a.X, ub = axisX ? b.Y : b.X;
                double va = axisX ? a.X : a.Y, vb = axisX ? b.X : b.Y;
                if ((ua <= level && ub > level) || (ub <= level && ua > level))
                    cuts.Add(va + (level - ua) / (ub - ua) * (vb - va));
            }
        }
        Collect(_outer);
        foreach (var h in _holes) Collect(h);
        cuts.Sort();
        var segs = new List<(double From, double To)>();
        for (int i = 0; i + 1 < cuts.Count; i += 2)
            if (cuts[i + 1] - cuts[i] > 1e-12) segs.Add((cuts[i], cuts[i + 1]));
        return segs;
    }

    /// <summary>Точка внутри материала сечения (правило чёт-нечет по всем кольцам).</summary>
    public bool Contains(double x, double y)
    {
        bool inside = false;
        void Test(IReadOnlyList<(double X, double Y)> ring)
        {
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var (xi, yi) = ring[i]; var (xj, yj) = ring[j];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
        }
        Test(_outer);
        foreach (var h in _holes) Test(h);
        return inside;
    }

    /// <summary>Копия сечения с переставленными осями x ↔ y.</summary>
    public PolygonSection SwapAxes() =>
        new(_outer.Select(p => (p.Y, p.X)), _holes.Select(h => h.Select(p => (p.Y, p.X))));
}
