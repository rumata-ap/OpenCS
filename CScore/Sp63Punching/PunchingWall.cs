namespace CScore.Sp63Punching;

/// <summary>Вид узла стены (СП 430.1325800.2018, п. 6.3.8).</summary>
public enum PunchingWallKind
{
    /// <summary>Торец стены (рис. 6.1): П-контур, у края плиты — также контур 1′.</summary>
    End,

    /// <summary>Угол двух стен под 90° (рис. 6.2): только сила, без моментов.</summary>
    Corner
}

/// <summary>Редакция рисунка 6.2 (угол стены).</summary>
public enum PunchingWallFigure
{
    /// <summary>СП 430.1325800.2018 с Изменением № 1 (действующая редакция).</summary>
    Sp430Amd1
}

/// <summary>
/// Узел стены для расчёта плиты на продавливание по СП 430 п. 6.3.8. Оси узла: у торца — начало в центре торца,
/// стена идёт вдоль −X (толщина <see cref="A1"/> по Y); у угла — начало в наружном углу, стена толщиной
/// <see cref="A1"/> идёт вдоль +X (занимает 0 ≤ y ≤ a1), стена толщиной <see cref="A2"/> — вдоль +Y (0 ≤ x ≤ a2).
/// </summary>
/// <param name="Kind">Торец или угол.</param>
/// <param name="A1">Толщина стены (у угла — стены вдоль X), м.</param>
/// <param name="A2">У угла — толщина стены вдоль Y, м; у торца не используется.</param>
/// <param name="Figure">Редакция рисунка 6.2.</param>
public sealed record PunchingWall(PunchingWallKind Kind, double A1, double? A2 = null,
    PunchingWallFigure Figure = PunchingWallFigure.Sp430Amd1)
{
    /// <summary>Толщина второй стены угла (по умолчанию — как первой), м.</summary>
    public double T2 => A2 ?? A1;
}

/// <summary>Геометрия узлов стены: кусок стены, контуры на отступе от граней, основание пирамиды.</summary>
public static class PunchingWallGeometry
{
    const double Tol = 1e-9;

    /// <summary>
    /// Кусок стены в зоне контура (для рисунка, отверстий и шага арматуры), против часовой стрелки. Торец — прямоугольник
    /// длиной a1 + h0 от торца; угол — Г с полками a1 + a2 + h0/2 от наружного угла.
    /// </summary>
    public static List<PlanPoint> AreaPolygon(PunchingWall wall, double h0)
    {
        double a1 = wall.A1;
        if (wall.Kind == PunchingWallKind.End)
        {
            double l = a1 + h0;
            return [new(-l, -a1 / 2), new(0, -a1 / 2), new(0, a1 / 2), new(-l, a1 / 2)];
        }
        double a2 = wall.T2, ext = a1 + a2 + h0 / 2;
        return [new(0, 0), new(ext, 0), new(ext, a1), new(a2, a1), new(a2, ext), new(0, ext)];
    }

    /// <summary>
    /// Центр «площадки передачи нагрузки» узла: торец — центр квадрата a1 × a1 у торца, угол — центр квадрата
    /// a2 × a1 в углу. Из него проводятся касательные к отверстиям (п. 8.1.46 СП 63).
    /// </summary>
    public static PlanPoint LoadCenter(PunchingWall wall)
        => wall.Kind == PunchingWallKind.End ? new PlanPoint(-wall.A1 / 2, 0) : new PlanPoint(wall.T2 / 2, wall.A1 / 2);

    /// <summary>
    /// Контур узла на отступе <paramref name="d"/> от граней стены (при d = h0/2 — рис. 6.1, а и 6.2 СП 430). Торец —
    /// П: торцевой отрезок x = d, |y| ≤ a1/2 + d и две ветви той же длины a1 + 2d назад вдоль стены. Угол — наружный Г
    /// (ветви до a1 + a2 от наружных граней) и диагональ у внутреннего угла (рис. 6.2 ред. с Изм. № 1).
    /// </summary>
    public static List<ContourElement> Lines(PunchingWall wall, double d)
    {
        double a1 = wall.A1;
        if (wall.Kind == PunchingWallKind.End)
        {
            double w = a1 / 2 + d, x0 = d - (a1 + 2 * d);
            // Обход против часовой стрелки: нижняя ветвь к торцу, торцевой отрезок, верхняя ветвь назад.
            return [new LineElement(new(x0, -w), new(d, -w)), new LineElement(new(d, -w), new(d, w)), new LineElement(new(d, w), new(x0, w))];
        }
        double a2 = wall.T2, leg = a1 + a2;
        return
        [
            new LineElement(new(-d, leg), new(-d, -d)),
            new LineElement(new(-d, -d), new(leg, -d)),
            new LineElement(new(a2 + d + a1, a1 + d), new(a2 + d, a1 + d + a2)),
        ];
    }

    /// <summary>
    /// Контур 1′ у торца стены возле края плиты (рис. 6.1, б): два отрезка на отступе <paramref name="d"/> (по норме
    /// h0/2) от граней стены — от точки на расстоянии a1 от торца (внутрь стены) до края. null — край не пересекает
    /// обе линии за торцом.
    /// </summary>
    public static List<ContourElement>? EdgeLines(PunchingWall wall, PunchingSlabEdge edge, double d)
    {
        if (wall.Kind != PunchingWallKind.End) return null;
        var n = edge.N;
        if (n.X > -Tol) return null;    // край должен лежать за торцом (плита — со стороны стены)
        double a1 = wall.A1, w = a1 / 2 + d, x0 = -a1;
        var list = new List<ContourElement>();
        foreach (double y in new[] { -w, w })
        {
            // Пересечение y = const с краем: (p − P)·n = 0.
            double x = edge.PointOnEdge.X + (edge.PointOnEdge.Y - y) * n.Y / n.X;
            if (x <= x0 + Tol || x < -Tol) return null;
            list.Add(new LineElement(new(x0, y), new(x, y)));
        }
        return list;
    }

    /// <summary>
    /// Точка внутри стены узла (в зоне за началом осей, без торцевой грани): торец — x &lt; 0, |y| &lt; a1/2; угол — полки Г.
    /// </summary>
    public static bool InsideWall(PunchingWall wall, PlanPoint p, double tol = 1e-9)
    {
        double a1 = wall.A1;
        if (wall.Kind == PunchingWallKind.End) return p.X < -tol && Math.Abs(p.Y) < a1 / 2 - tol;
        double a2 = wall.T2;
        return (p.X > tol && p.Y > tol && p.Y < a1 - tol) || (p.Y > tol && p.X > tol && p.X < a2 - tol);
    }

    /// <summary>
    /// Отрезок пересекает стену узла: грань основания пирамиды поперёк стены (за ней стена продолжается). По норме контур
    /// поперёк стены не проводится — силы на таких гранях в силу продавливания не входят.
    /// </summary>
    public static bool CrossesWall(PunchingWall wall, PlanPoint a, PlanPoint b)
    {
        const int n = 64;
        for (int i = 1; i < n; i++)
            if (InsideWall(wall, a + (double)i / n * (b - a), 1e-9)) return true;
        return false;
    }

    /// <summary>
    /// Ветви П-контура торца, параллельные стене, на отступе <paramref name="d"/>: (−Y, +Y). На них п. 6.3.8 берёт
    /// изгибающие моменты плиты по обе стороны стены (ΔM).
    /// </summary>
    public static (LineElement Minus, LineElement Plus) EndBranches(PunchingWall wall, double d)
    {
        double w = wall.A1 / 2 + d, x0 = d - (wall.A1 + 2 * d);
        return (new LineElement(new(x0, -w), new(d, -w)), new LineElement(new(x0, w), new(d, w)));
    }

    /// <summary>
    /// Основание пирамиды узла (контур на отступе h0, замкнутый поперёк стены), обрезанное краями; против часовой
    /// стрелки. Торец — прямоугольник от начала ветвей до h0 за торцом; угол — пятиугольник по наружному Г и диагонали.
    /// </summary>
    public static List<PlanPoint> BasePolygon(PunchingWall wall, double h0, IReadOnlyList<PunchingSlabEdge> edges)
    {
        double a1 = wall.A1;
        List<PlanPoint> pts;
        if (wall.Kind == PunchingWallKind.End)
        {
            double w = a1 / 2 + h0, x0 = -(a1 + h0);
            pts = [new(x0, -w), new(h0, -w), new(h0, w), new(x0, w)];
        }
        else
        {
            double a2 = wall.T2;
            pts = [new(-h0, -h0), new(a1 + a2 + h0, -h0), new(a1 + a2 + h0, a1 + h0), new(a2 + h0, a1 + a2 + h0), new(-h0, a1 + a2 + h0)];
        }
        foreach (var e in edges)
        {
            var res = new List<PlanPoint>();
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i]; var q = pts[(i + 1) % pts.Count];
                double dp = e.SignedDistance(p), dq = e.SignedDistance(q);
                if (dp >= 0) res.Add(p);
                if ((dp >= 0) != (dq >= 0)) res.Add(p + dp / (dp - dq) * (q - p));
            }
            pts = res;
            if (pts.Count < 3) return [];
        }
        return pts;
    }
}
