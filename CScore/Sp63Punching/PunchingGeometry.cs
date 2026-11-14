namespace CScore.Sp63Punching;

/// <summary>Точка или вектор в плоскости плиты, м.</summary>
public readonly record struct PlanPoint(double X, double Y)
{
    public static PlanPoint operator +(PlanPoint a, PlanPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static PlanPoint operator -(PlanPoint a, PlanPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static PlanPoint operator *(double k, PlanPoint a) => new(k * a.X, k * a.Y);

    /// <summary>Скалярное произведение.</summary>
    public double Dot(PlanPoint o) => X * o.X + Y * o.Y;

    /// <summary>Псевдоскалярное произведение (z-компонента векторного).</summary>
    public double Cross(PlanPoint o) => X * o.Y - Y * o.X;

    /// <summary>Длина вектора.</summary>
    public double Length => Math.Sqrt(X * X + Y * Y);

    /// <summary>Единичный вектор того же направления.</summary>
    public PlanPoint Normalized()
    {
        double l = Length;
        return l > 0 ? new PlanPoint(X / l, Y / l) : this;
    }

    /// <summary>Единичный вектор под углом φ к оси X.</summary>
    public static PlanPoint FromAngle(double phi) => new(Math.Cos(phi), Math.Sin(phi));
}

/// <summary>
/// Элемент расчётного контура продавливания: отрезок или дуга окружности.
/// Контур имеет условную единичную ширину (п. 8.1.51 СП 63): интегралы берутся по длине.
/// </summary>
public abstract record ContourElement
{
    /// <summary>Длина элемента, м.</summary>
    public abstract double Length { get; }

    /// <summary>∫x ds, ∫y ds — статические моменты, м².</summary>
    public abstract (double Sx, double Sy) FirstMoments();

    /// <summary>∫(x−xc)² ds и ∫(y−yc)² ds относительно точки c, м³.</summary>
    public abstract (double Ixx, double Iyy) SecondMoments(PlanPoint c);

    /// <summary>Наибольшие и наименьшие координаты точек элемента: (xmin, xmax, ymin, ymax).</summary>
    public abstract (double XMin, double XMax, double YMin, double YMax) Bounds();

    /// <summary>Части элемента, лежащие в полуплоскости a·p ≥ k.</summary>
    public abstract IEnumerable<ContourElement> ClipToHalfPlane(PlanPoint a, double k);

    /// <summary>Начальная точка.</summary>
    public abstract PlanPoint Start { get; }

    /// <summary>Конечная точка.</summary>
    public abstract PlanPoint End { get; }

    internal const double Eps = 1e-12;
}

/// <summary>Отрезок контура.</summary>
public sealed record LineElement(PlanPoint A, PlanPoint B) : ContourElement
{
    public override double Length => (B - A).Length;
    public override PlanPoint Start => A;
    public override PlanPoint End => B;

    public override (double Sx, double Sy) FirstMoments()
    {
        double l = Length;
        return (l * (A.X + B.X) / 2.0, l * (A.Y + B.Y) / 2.0);
    }

    public override (double Ixx, double Iyy) SecondMoments(PlanPoint c)
    {
        double l = Length;
        double x1 = A.X - c.X, x2 = B.X - c.X, y1 = A.Y - c.Y, y2 = B.Y - c.Y;
        return (l * (x1 * x1 + x1 * x2 + x2 * x2) / 3.0, l * (y1 * y1 + y1 * y2 + y2 * y2) / 3.0);
    }

    public override (double XMin, double XMax, double YMin, double YMax) Bounds()
        => (Math.Min(A.X, B.X), Math.Max(A.X, B.X), Math.Min(A.Y, B.Y), Math.Max(A.Y, B.Y));

    public override IEnumerable<ContourElement> ClipToHalfPlane(PlanPoint a, double k)
    {
        double fa = a.Dot(A) - k, fb = a.Dot(B) - k;
        double tol = Eps * Math.Max(1.0, a.Length);
        if (fa >= -tol && fb >= -tol) { yield return this; yield break; }
        if (fa < -tol && fb < -tol) yield break;
        double t = fa / (fa - fb);
        var p = A + t * (B - A);
        var piece = fa >= -tol ? new LineElement(A, p) : new LineElement(p, B);
        if (piece.Length > Eps) yield return piece;
    }
}

/// <summary>Дуга окружности контура: от угла Phi1 к Phi2 против часовой стрелки (Phi2 &gt; Phi1).</summary>
public sealed record ArcElement(PlanPoint Center, double R, double Phi1, double Phi2) : ContourElement
{
    /// <summary>Центральный угол дуги, рад.</summary>
    public double Theta => Phi2 - Phi1;

    public override double Length => R * Theta;
    public override PlanPoint Start => Center + R * PlanPoint.FromAngle(Phi1);
    public override PlanPoint End => Center + R * PlanPoint.FromAngle(Phi2);

    public override (double Sx, double Sy) FirstMoments()
        => (R * (Center.X * Theta + R * (Math.Sin(Phi2) - Math.Sin(Phi1))),
            R * (Center.Y * Theta - R * (Math.Cos(Phi2) - Math.Cos(Phi1))));

    public override (double Ixx, double Iyy) SecondMoments(PlanPoint c)
    {
        double dx = Center.X - c.X, dy = Center.Y - c.Y, th = Theta;
        double s = Math.Sin(Phi2) - Math.Sin(Phi1);
        double co = Math.Cos(Phi2) - Math.Cos(Phi1);
        double s2 = Math.Sin(2 * Phi2) - Math.Sin(2 * Phi1);
        double ixx = R * (dx * dx * th + 2 * dx * R * s + R * R * (th / 2 + s2 / 4));
        double iyy = R * (dy * dy * th - 2 * dy * R * co + R * R * (th / 2 - s2 / 4));
        return (ixx, iyy);
    }

    public override (double XMin, double XMax, double YMin, double YMax) Bounds()
    {
        var pts = new List<PlanPoint> { Start, End };
        for (int q = (int)Math.Ceiling(Phi1 / (Math.PI / 2)); q * (Math.PI / 2) <= Phi2; q++)
            pts.Add(Center + R * PlanPoint.FromAngle(q * Math.PI / 2));
        return (pts.Min(p => p.X), pts.Max(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.Y));
    }

    public override IEnumerable<ContourElement> ClipToHalfPlane(PlanPoint a, double k)
    {
        // a·(c + R(cosφ, sinφ)) ≥ k  ⇔  cos(φ − α) ≥ g
        double al = a.Length;
        if (al <= 0) { yield return this; yield break; }
        double g = (k - a.Dot(Center)) / (R * al);
        if (g <= -1 + Eps) { yield return this; yield break; }
        if (g >= 1 - Eps) yield break;
        double alpha = Math.Atan2(a.Y, a.X);
        double w = Math.Acos(g);
        // Допустимый интервал углов: [α − w, α + w] (+ 2πn).
        for (int n = -2; n <= 2; n++)
        {
            double lo = Math.Max(Phi1, alpha - w + 2 * Math.PI * n);
            double hi = Math.Min(Phi2, alpha + w + 2 * Math.PI * n);
            if (hi - lo > Eps) yield return this with { Phi1 = lo, Phi2 = hi };
        }
    }
}

/// <summary>Кусок контура — непрерывная цепочка элементов.</summary>
public sealed record PunchingContourPiece(IReadOnlyList<ContourElement> Elements)
{
    /// <summary>Длина куска, м.</summary>
    public double Length => Elements.Sum(e => e.Length);
}

/// <summary>Расчётный контур: один или несколько кусков (разорванный контур, вырез у отверстия).</summary>
public sealed record PunchingContour(IReadOnlyList<PunchingContourPiece> Pieces)
{
    /// <summary>Все элементы контура.</summary>
    public IEnumerable<ContourElement> Elements => Pieces.SelectMany(p => p.Elements);

    /// <summary>Пустой контур.</summary>
    public static PunchingContour Empty { get; } = new(Array.Empty<PunchingContourPiece>());

    /// <summary>Контур из одного списка элементов: соседние по концам элементы собираются в куски.</summary>
    public static PunchingContour FromElements(IEnumerable<ContourElement> elements)
    {
        var pieces = new List<PunchingContourPiece>();
        var current = new List<ContourElement>();
        foreach (var e in elements)
        {
            if (e.Length <= ContourElement.Eps) continue;
            if (current.Count > 0 && (current[^1].End - e.Start).Length > 1e-9)
            {
                pieces.Add(new PunchingContourPiece(current));
                current = new List<ContourElement>();
            }
            current.Add(e);
        }
        if (current.Count > 0) pieces.Add(new PunchingContourPiece(current));
        // Замкнутый контур, разрезанный в середине, склеивается: последний кусок продолжает первый.
        if (pieces.Count > 1 && (pieces[^1].Elements[^1].End - pieces[0].Elements[0].Start).Length <= 1e-9)
        {
            var merged = pieces[^1].Elements.Concat(pieces[0].Elements).ToList();
            pieces[0] = new PunchingContourPiece(merged);
            pieces.RemoveAt(pieces.Count - 1);
        }
        return new PunchingContour(pieces);
    }
}

/// <summary>Геометрические характеристики контура по п. 8.1.51 СП 63.</summary>
/// <param name="U">Периметр, м.</param>
/// <param name="Xc">Центр тяжести по X (8.99), м.</param>
/// <param name="Yc">Центр тяжести по Y, м.</param>
/// <param name="Ibx">Момент инерции относительно оси X через центр тяжести, ∫(y−yc)² ds, м³.</param>
/// <param name="Iby">Момент инерции относительно оси Y, ∫(x−xc)² ds, м³.</param>
/// <param name="XMinus">Наибольшее расстояние до точек контура в сторону −x, м.</param>
/// <param name="XPlus">То же в сторону +x, м.</param>
/// <param name="YMinus">То же в сторону −y, м.</param>
/// <param name="YPlus">То же в сторону +y, м.</param>
public sealed record ContourProperties(
    double U, double Xc, double Yc, double Ibx, double Iby,
    double XMinus, double XPlus, double YMinus, double YPlus)
{
    /// <summary>x_max (8.98) — наибольшее расстояние от центра тяжести вдоль X.</summary>
    public double XMax => Math.Max(XMinus, XPlus);

    /// <summary>y_max — наибольшее расстояние от центра тяжести вдоль Y.</summary>
    public double YMax => Math.Max(YMinus, YPlus);

    /// <summary>Наименьший момент сопротивления относительно оси X: Ibx / ymax (8.98).</summary>
    public double WbxMin => YMax > 0 ? Ibx / YMax : 0;

    /// <summary>Наименьший момент сопротивления относительно оси Y: Iby / xmax.</summary>
    public double WbyMin => XMax > 0 ? Iby / XMax : 0;

    /// <summary>Считает характеристики контура.</summary>
    public static ContourProperties Compute(PunchingContour contour)
    {
        var els = contour.Elements.ToList();
        double u = els.Sum(e => e.Length);
        if (u <= 0) return new ContourProperties(0, 0, 0, 0, 0, 0, 0, 0, 0);
        double sx = 0, sy = 0;
        foreach (var e in els) { var (a, b) = e.FirstMoments(); sx += a; sy += b; }
        var c = new PlanPoint(sx / u, sy / u);
        double ixx = 0, iyy = 0;
        double xmin = double.MaxValue, xmax = double.MinValue, ymin = double.MaxValue, ymax = double.MinValue;
        foreach (var e in els)
        {
            var (i1, i2) = e.SecondMoments(c);
            ixx += i1; iyy += i2;
            var b = e.Bounds();
            xmin = Math.Min(xmin, b.XMin); xmax = Math.Max(xmax, b.XMax);
            ymin = Math.Min(ymin, b.YMin); ymax = Math.Max(ymax, b.YMax);
        }
        // Ibx — относительно оси X: интеграл квадрата расстояния по y.
        return new ContourProperties(u, c.X, c.Y, iyy, ixx,
            c.X - xmin, xmax - c.X, c.Y - ymin, ymax - c.Y);
    }
}
