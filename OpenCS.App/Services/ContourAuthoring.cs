using CScore;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Шаблон контура пула (команды дерева «Прямоугольник», «Тавр», «Двутавр», «Уголок», «Круг»).</summary>
public enum ContourTemplate { Rect, Tee, IBeam, Angle, Circle }

/// <summary>
/// Размеры шаблонного контура в метрах. Значение полей зависит от шаблона:
/// прямоугольник — <see cref="Width"/>×<see cref="Height"/>; тавр и уголок — ширина полки, высота, толщина стенки
/// <see cref="Tw"/> и полки <see cref="Tf"/>; двутавр — то же; круг — диаметр <see cref="Width"/> и число сегментов.
/// </summary>
public sealed record ContourTemplateSize(double Width, double Height = 0, double Tw = 0, double Tf = 0, int Segments = 32);

/// <summary>
/// Создание, правка и удаление контуров и кругов пула проекта: одна проверка и одно сохранение для страницы
/// контура, шаблонов, таблицы кругов и консоли (<c>opencs apply</c>). Координаты — в метрах (единицы базы).
/// </summary>
public static class ContourAuthoring
{
    /// <summary>Наименьшая площадь контура, м² (1 мм²): меньше — вырожденный контур.</summary>
    public const double MinArea = 1e-6;

    /// <summary>Наименьшее и наибольшее число сегментов круглого шаблона.</summary>
    public const int MinSegments = 8, MaxSegments = 720;

    /// <summary>
    /// Проверяет кольцо вершин (замыкающая вершина может быть, может не быть): конечные числа, ≥ 3 различных вершин,
    /// без повторов подряд, без самопересечений, площадь ≥ <see cref="MinArea"/>. Пути — <c>{path}[i]</c>.
    /// </summary>
    public static IReadOnlyList<FieldError> ValidateRing(IReadOnlyList<(double X, double Y)> points, string path = "points")
    {
        var errors = new List<FieldError>();
        for (int i = 0; i < points.Count; i++)
            if (!double.IsFinite(points[i].X) || !double.IsFinite(points[i].Y))
                errors.Add(new($"{path}[{i}]", Loc.S("ContourErrNotFinite")));
        if (errors.Count > 0) return errors;

        var ring = OpenRing(points);
        if (ring.Count < 3)
        {
            errors.Add(new(path, Loc.S("ContourErrTooFewVertices")));
            return errors;
        }
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % ring.Count];
            if (Same(a, b))
                errors.Add(new($"{path}[{(i + 1) % ring.Count}]", Loc.S("ContourErrDuplicateVertex")));
        }
        if (errors.Count > 0) return errors;

        int n = ring.Count;
        for (int i = 0; i < n && errors.Count == 0; i++)
            for (int j = i + 1; j < n; j++)
            {
                if (j == i + 1 || (i == 0 && j == n - 1)) continue; // смежные рёбра
                if (SegmentsIntersect(ring[i], ring[(i + 1) % n], ring[j], ring[(j + 1) % n]))
                {
                    errors.Add(new($"{path}[{j}]", string.Format(Loc.S("ContourErrSelfIntersection"), i + 1, j + 1)));
                    break;
                }
            }
        if (errors.Count > 0) return errors;

        if (Math.Abs(SignedArea(ring)) < MinArea)
            errors.Add(new(path, Loc.S("ContourErrZeroArea")));
        return errors;
    }

    /// <summary>Проверка контура перед записью: обозначение, тип, вершины (полилиния — только ≥ 2 конечных вершин).</summary>
    public static IReadOnlyList<FieldError> Validate(Contour c)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(c.Tag)) errors.Add(new("tag", Loc.S("ContourErrTagEmpty")));
        if (!Enum.IsDefined(c.Type)) errors.Add(new("type", Loc.S("ContourErrType")));
        var pts = c.Points.Select(p => (p.X, p.Y)).ToList();
        if (c.IsPolyline)
        {
            if (pts.Count < 2 || pts.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
                errors.Add(new("points", Loc.S("ContourErrTooFewVertices")));
        }
        else
            errors.AddRange(ValidateRing(pts));
        return errors;
    }

    /// <summary>
    /// Проверяет и сохраняет контур: замыкает кольцо, пересчитывает X/Y и WKT, пишет в базу; новый — добавляется в
    /// пул <see cref="DatabaseService.Contours"/>. Ошибки — ничего не записано и контур не изменён.
    /// </summary>
    public static IReadOnlyList<FieldError> Save(DatabaseService db, Contour c)
    {
        var errors = Validate(c);
        if (errors.Count > 0) return errors;
        if (!c.IsPolyline) c.TryClose();
        Renumber(c);
        c.PointsToXYs();
        c.SetWKT();
        db.SaveContour(c);
        if (!db.Contours.Contains(c))
        {
            db.Contours.Add(c);
            foreach (var p in c.Points) { p.Contour = c; db.Points.Add(p); }
        }
        return [];
    }

    /// <summary>Обозначения областей сечений, взявших контур из пула (<see cref="MaterialArea.PoolContourId"/>).</summary>
    public static List<string> UsedBy(IEnumerable<MaterialArea> areas, Contour c) =>
        areas.Where(a => (c.Id > 0 && a.PoolContourId == c.Id) || ReferenceEquals(a.PoolContour, c))
             .Select(a => string.IsNullOrWhiteSpace(a.Tag) ? $"#{a.Id}" : a.Tag)
             .Distinct().ToList();

    /// <summary>
    /// Удаляет контур из базы и пула, если ни одна область на него не ссылается; иначе — false и обозначения
    /// областей в <paramref name="usedBy"/>.
    /// </summary>
    public static bool TryDelete(DatabaseService db, Contour c, out List<string> usedBy)
    {
        usedBy = UsedBy(db.MaterialAreas, c);
        if (usedBy.Count > 0) return false;
        if (c.Id > 0) db.DeleteContour(c);
        db.Contours.Remove(c);
        foreach (var p in c.Points) db.Points.Remove(p);
        return true;
    }

    /// <summary>Проверка размеров шаблона (м): положительные конечные; стенка уже полки, полки не толще высоты.</summary>
    public static IReadOnlyList<FieldError> ValidateTemplate(ContourTemplate kind, ContourTemplateSize s)
    {
        var errors = new List<FieldError>();
        void Positive(double v, string path)
        {
            if (!double.IsFinite(v) || v <= 0) errors.Add(new(path, Loc.S("ContourErrPositive")));
        }
        switch (kind)
        {
            case ContourTemplate.Rect:
                Positive(s.Width, "width"); Positive(s.Height, "height");
                break;
            case ContourTemplate.Circle:
                Positive(s.Width, "d");
                if (s.Segments < MinSegments || s.Segments > MaxSegments)
                    errors.Add(new("segments", string.Format(Loc.S("ContourErrSegments"), MinSegments, MaxSegments)));
                break;
            default:
                Positive(s.Width, "width"); Positive(s.Height, "height"); Positive(s.Tw, "tw"); Positive(s.Tf, "tf");
                if (errors.Count > 0) break;
                if (s.Tw >= s.Width) errors.Add(new("tw", Loc.S("ContourErrWebTooThick")));
                double flanges = kind == ContourTemplate.IBeam ? 2 * s.Tf : s.Tf;
                if (flanges >= s.Height) errors.Add(new("tf", Loc.S("ContourErrFlangeTooThick")));
                break;
        }
        return errors;
    }

    /// <summary>
    /// Строит замкнутый контур по шаблону (размеры в м). Размеры должны пройти <see cref="ValidateTemplate"/>.
    /// </summary>
    public static Contour FromTemplate(ContourTemplate kind, ContourTemplateSize s, string tag)
    {
        var pts = kind switch
        {
            ContourTemplate.Rect => TemplatePoints.RectPoints(s.Width, s.Height),
            ContourTemplate.Tee => TemplatePoints.TeePoints(s.Width, s.Height, s.Tw, s.Tf),
            ContourTemplate.IBeam => TemplatePoints.IBeamPoints(s.Height, s.Width, s.Tw, s.Tf),
            ContourTemplate.Angle => TemplatePoints.AnglePoints(s.Width, s.Height, s.Tw, s.Tf),
            _ => TemplatePoints.CirclePoints(s.Width, s.Segments),
        };
        return FromPoints(pts, tag, ContourType.Hull);
    }

    /// <summary>Замкнутый контур из вершин (м): нумерация с 1, замыкающая вершина, X/Y и WKT.</summary>
    public static Contour FromPoints(IReadOnlyList<(double X, double Y)> pts, string tag, ContourType type)
    {
        var c = new Contour { Tag = tag, Type = type };
        int k = 1;
        foreach (var (x, y) in OpenRing(pts))
            c.Points.Add(new StressPoint(x, y) { Num = k++, Contour = c });
        if (c.Points.Count >= 3) c.TryClose();
        Renumber(c);
        return c;
    }

    /// <summary>Проверка круга пула: конечные координаты, радиус больше нуля.</summary>
    public static IReadOnlyList<FieldError> ValidateCircle(CircleP c)
    {
        var errors = new List<FieldError>();
        if (!double.IsFinite(c.X)) errors.Add(new("x", Loc.S("ContourErrNotFinite")));
        if (!double.IsFinite(c.Y)) errors.Add(new("y", Loc.S("ContourErrNotFinite")));
        if (!double.IsFinite(c.Radius) || c.Radius <= 0) errors.Add(new("d", Loc.S("ContourErrPositive")));
        return errors;
    }

    /// <summary>
    /// Проверяет и сохраняет круг: диаметр и площадь — из радиуса; новый — в пул <see cref="DatabaseService.Circles"/>.
    /// </summary>
    public static IReadOnlyList<FieldError> SaveCircle(DatabaseService db, CircleP c)
    {
        var errors = ValidateCircle(c);
        if (errors.Count > 0) return errors;
        c.Diameter = 2 * c.Radius;
        c.Area = Math.PI * c.Radius * c.Radius;
        db.SaveCircle(c);
        if (!db.Circles.Contains(c)) db.Circles.Add(c);
        return [];
    }

    /// <summary>Удаляет круг из базы и пула.</summary>
    public static void DeleteCircle(DatabaseService db, CircleP c)
    {
        if (c.Id > 0) db.DeleteCircle(c);
        db.Circles.Remove(c);
    }

    /// <summary>Вершины без замыкающей (совпадающей с первой).</summary>
    public static List<(double X, double Y)> OpenRing(IReadOnlyList<(double X, double Y)> pts)
    {
        var list = pts.ToList();
        if (list.Count >= 2 && Same(list[0], list[^1])) list.RemoveAt(list.Count - 1);
        return list;
    }

    static void Renumber(Contour c)
    {
        int i = 1;
        foreach (var p in c.Points) p.Num = i++;
    }

    static bool Same((double X, double Y) a, (double X, double Y) b) =>
        Math.Abs(a.X - b.X) < Contour.CloseTolerance && Math.Abs(a.Y - b.Y) < Contour.CloseTolerance;

    static double SignedArea(IReadOnlyList<(double X, double Y)> r)
    {
        double s = 0;
        for (int i = 0; i < r.Count; i++)
        {
            var a = r[i]; var b = r[(i + 1) % r.Count];
            s += a.X * b.Y - b.X * a.Y;
        }
        return s / 2;
    }

    static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
        (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    static bool OnSegment((double X, double Y) p, (double X, double Y) a, (double X, double Y) b) =>
        Math.Min(a.X, b.X) - 1e-12 <= p.X && p.X <= Math.Max(a.X, b.X) + 1e-12 &&
        Math.Min(a.Y, b.Y) - 1e-12 <= p.Y && p.Y <= Math.Max(a.Y, b.Y) + 1e-12;

    /// <summary>Отрезки p1p2 и q1q2 пересекаются или касаются (с учётом коллинеарного наложения).</summary>
    static bool SegmentsIntersect((double X, double Y) p1, (double X, double Y) p2, (double X, double Y) q1, (double X, double Y) q2)
    {
        double scale = Math.Max(1e-9, new[] { p1.X, p1.Y, p2.X, p2.Y, q1.X, q1.Y, q2.X, q2.Y }.Max(Math.Abs));
        double eps = 1e-12 * scale * scale;
        double d1 = Cross(q1, q2, p1), d2 = Cross(q1, q2, p2), d3 = Cross(p1, p2, q1), d4 = Cross(p1, p2, q2);
        if (((d1 > eps && d2 < -eps) || (d1 < -eps && d2 > eps)) && ((d3 > eps && d4 < -eps) || (d3 < -eps && d4 > eps)))
            return true;
        if (Math.Abs(d1) <= eps && OnSegment(p1, q1, q2)) return true;
        if (Math.Abs(d2) <= eps && OnSegment(p2, q1, q2)) return true;
        if (Math.Abs(d3) <= eps && OnSegment(q1, p1, p2)) return true;
        if (Math.Abs(d4) <= eps && OnSegment(q2, p1, p2)) return true;
        return false;
    }
}
