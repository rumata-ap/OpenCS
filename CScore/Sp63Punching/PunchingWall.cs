namespace CScore.Sp63Punching;

/// <summary>Вид узла стены (СП 430.1325800.2018, п. 6.3.8).</summary>
public enum PunchingWallKind
{
    /// <summary>Торец стены (рис. 6.1): П-контур, у края плиты — также контур 1′.</summary>
    End,

    /// <summary>Угол двух стен под 90° (рис. 6.2): только сила, без моментов.</summary>
    Corner
}

/// <summary>Длина ветвей П-контура подвешенной плиты (СП 430, рис. 6.3) вдоль стены.</summary>
public enum PunchingHangerLegs
{
    /// <summary>По рис. 6.3: ветви длиной a — толщина стены — от торцевого отрезка контура.</summary>
    WallThickness,

    /// <summary>Как у торца стены (рис. 6.1): ветви длиной a1 + h0 от торцевого отрезка (у торца — квадрат).</summary>
    AsWallEnd
}

/// <summary>
/// Подвешивающая плиту арматура у торца вышележащей стены (СП 430 п. 6.3.8, рис. 6.3): контур отстоит на h0/2 от
/// крайних стержней, а не от граней стены. Ряды стержней — симметрично оси стены.
/// </summary>
/// <param name="Width">Расстояние между осями крайних рядов поперёк стены, м (0 — один ряд по оси стены).</param>
/// <param name="EndOffset">Расстояние от торца стены до оси крайнего стержня вдоль стены, м.</param>
/// <param name="Legs">Длина ветвей П-контура.</param>
public sealed record PunchingHanger(double Width, double EndOffset, PunchingHangerLegs Legs = PunchingHangerLegs.WallThickness);

/// <summary>Редакция рисунка 6.2 (угол стены).</summary>
public enum PunchingWallFigure
{
    /// <summary>
    /// СП 430.1325800.2018 с Изменением № 1 (действующая редакция): наружные ветви Г — длиной a1 + a2 от угла контура,
    /// диагональ — от точек на a1 и a2 от угла контура у внутреннего угла (обе отложены от линии h0/2).
    /// </summary>
    Sp430Amd1,

    /// <summary>
    /// Проект Изменения № 2 к СП 430 (публичное обсуждение завершено 07.09.2026, не утверждён): наружные ветви — до a1 + a2
    /// от наружных граней стен, диагональ — от точек на a1 и a2 от внутренних граней стен.
    /// </summary>
    Sp430Amd2Draft
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
/// <param name="Hanger">У торца — подвешенная плита (рис. 6.3): контур от крайних подвешивающих стержней; null — обычный торец.</param>
public sealed record PunchingWall(PunchingWallKind Kind, double A1, double? A2 = null,
    PunchingWallFigure Figure = PunchingWallFigure.Sp430Amd1, PunchingHanger? Hanger = null)
{
    /// <summary>Подвешенная плита у торца стены (рис. 6.3).</summary>
    public bool IsHanging => Kind == PunchingWallKind.End && Hanger is not null;

    /// <summary>Толщина второй стены угла (по умолчанию — как первой), м.</summary>
    public double T2 => A2 ?? A1;

    /// <summary>Расчёт по проекту Изменения № 2 (рис. 6.2 новой редакции).</summary>
    public bool IsAmd2 => Figure == PunchingWallFigure.Sp430Amd2Draft;
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
            // У подвешенной плиты основание пирамиды может уходить вдоль стены дальше a1 + h0.
            double l = Math.Max(a1 + h0, -EndFrame(wall, h0, h0).X0);
            return [new(-l, -a1 / 2), new(0, -a1 / 2), new(0, a1 / 2), new(-l, a1 / 2)];
        }
        double a2 = wall.T2, ext = a1 + a2 + h0 / 2;
        return [new(0, 0), new(ext, 0), new(ext, a1), new(a2, a1), new(a2, ext), new(0, ext)];
    }

    /// <summary>
    /// Центр «площадки передачи нагрузки» узла: торец — центр квадрата a1 × a1 у торца, угол — центр квадрата
    /// a2 × a1 в углу, подвешенная плита — середина ветвей П-контура на оси стены. Из него проводятся касательные к
    /// отверстиям (п. 8.1.46 СП 63). <paramref name="h0"/> нужна только подвешенной плите.
    /// </summary>
    public static PlanPoint LoadCenter(PunchingWall wall, double h0 = 0)
    {
        if (wall.Kind != PunchingWallKind.End) return new PlanPoint(wall.T2 / 2, wall.A1 / 2);
        if (!wall.IsHanging) return new PlanPoint(-wall.A1 / 2, 0);
        var f = EndFrame(wall, h0 / 2, h0);
        return new PlanPoint((f.XEnd + f.X0) / 2, 0);
    }

    /// <summary>
    /// П-контур торца на отступе <paramref name="d"/>: полуширина (ветви при y = ±W), x торцевого отрезка и x начала
    /// ветвей. Обычный торец — от граней стены, ветви a1 + 2d. Подвешенная плита — от крайних стержней (ряды на
    /// ±Width/2, крайний стержень на x = −EndOffset); ветви при d = h0/2 длиной a1 (рис. 6.3) или a1 + h0 (как рис. 6.1)
    /// и на большем отступе удлиняются на 2(d − h0/2), как у торца.
    /// </summary>
    public static (double W, double XEnd, double X0) EndFrame(PunchingWall wall, double d, double h0)
    {
        double a1 = wall.A1;
        if (wall.Hanger is not { } hg) return (a1 / 2 + d, d, d - (a1 + 2 * d));
        double xe = -hg.EndOffset + d;
        double leg = (hg.Legs == PunchingHangerLegs.WallThickness ? a1 : a1 + h0) + 2 * (d - h0 / 2);
        return (hg.Width / 2 + d, xe, xe - leg);
    }

    /// <summary>Оси крайних подвешивающих стержней в плане (для рисунка): у торца, на ±Width/2; пусто — не подвешенная плита.</summary>
    public static IReadOnlyList<PlanPoint> HangerRods(PunchingWall wall)
    {
        if (!wall.IsHanging) return [];
        var hg = wall.Hanger!;
        double x = -hg.EndOffset, y = hg.Width / 2;
        return y > Tol ? [new(x, -y), new(x, y)] : [new(x, 0)];
    }

    /// <summary>
    /// Контур узла на отступе <paramref name="d"/> от граней стены (при d = h0/2 — рис. 6.1, а и 6.2 СП 430). Торец —
    /// П: торцевой отрезок x = d, |y| ≤ a1/2 + d и две ветви той же длины a1 + 2d назад вдоль стены. Угол — наружный Г
    /// и диагональ у внутреннего угла по редакции рис. 6.2 (<see cref="CornerLegEnd"/>, <see cref="CornerDiagonalLegs"/>).
    /// На отступе d &gt; h0/2 (ряды арматуры, контур за ней) концы ветвей Г остаются на месте, диагональ сдвигается на d.
    /// </summary>
    public static List<ContourElement> Lines(PunchingWall wall, double d, double h0)
    {
        double a1 = wall.A1;
        if (wall.Kind == PunchingWallKind.End)
        {
            var (w, xe, x0) = EndFrame(wall, d, h0);
            // Обход против часовой стрелки: нижняя ветвь к торцу, торцевой отрезок, верхняя ветвь назад.
            return [new LineElement(new(x0, -w), new(xe, -w)), new LineElement(new(xe, -w), new(xe, w)), new LineElement(new(xe, w), new(x0, w))];
        }
        double a2 = wall.T2, leg = CornerLegEnd(wall, h0);
        var (k1, k2) = CornerDiagonalLegs(wall, h0);
        var list = new List<ContourElement>
        {
            new LineElement(new(-d, leg), new(-d, -d)),
            new LineElement(new(-d, -d), new(leg, -d)),
        };
        if (k1 + k2 > Tol) list.Add(new LineElement(new(a2 + d + k1, a1 + d), new(a2 + d, a1 + d + k2)));
        return list;
    }

    /// <summary>
    /// Конец наружных ветвей Г угла от наружных граней стен, м: Изм. № 1 — a1 + a2 от угла контура (a1 + a2 − h0/2 от
    /// граней), проект Изм. № 2 — a1 + a2 от граней.
    /// </summary>
    public static double CornerLegEnd(PunchingWall wall, double h0)
        => wall.A1 + wall.T2 - (wall.IsAmd2 ? 0 : h0 / 2);

    /// <summary>
    /// Катеты диагонали угла от угла контура у внутреннего угла стен (точка (a2 + h0/2; a1 + h0/2)), м: вдоль X и вдоль Y.
    /// Изм. № 1 — a1 и a2; проект Изм. № 2 — a1 − h0/2 и a2 − h0/2 (концы на a1 и a2 от внутренних граней), не меньше 0.
    /// </summary>
    public static (double AlongX, double AlongY) CornerDiagonalLegs(PunchingWall wall, double h0)
        => wall.IsAmd2 ? (Math.Max(0, wall.A1 - h0 / 2), Math.Max(0, wall.T2 - h0 / 2)) : (wall.A1, wall.T2);

    /// <summary>Редакция рис. 6.2 для ссылок отчёта: «(ред. с Изм. № 1)» или «(проект Изм. № 2)».</summary>
    public static string FigureEdition(PunchingWall wall) => wall.IsAmd2 ? "(проект Изм. № 2)" : "(ред. с Изм. № 1)";

    /// <summary>
    /// Контур 1′ у торца стены возле края плиты (рис. 6.1, б): два отрезка на отступе <paramref name="d"/> (по норме
    /// h0/2) от граней стены — от точки на расстоянии a1 от торца (внутрь стены) до края. У подвешенной плиты — от
    /// крайних рядов стержней, начало — на a1 от крайнего стержня. null — край не пересекает обе линии за торцом.
    /// </summary>
    public static List<ContourElement>? EdgeLines(PunchingWall wall, PunchingSlabEdge edge, double d)
    {
        if (wall.Kind != PunchingWallKind.End) return null;
        var n = edge.N;
        if (n.X > -Tol) return null;    // край должен лежать за торцом (плита — со стороны стены)
        double a1 = wall.A1, w = a1 / 2 + d, x0 = -a1;
        if (wall.Hanger is { } hg) { w = hg.Width / 2 + d; x0 = -hg.EndOffset - a1; }
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
    public static (LineElement Minus, LineElement Plus) EndBranches(PunchingWall wall, double d, double h0 = 0)
    {
        var (w, xe, x0) = EndFrame(wall, d, h0);
        return (new LineElement(new(x0, -w), new(xe, -w)), new LineElement(new(x0, w), new(xe, w)));
    }

    /// <summary>
    /// Основание пирамиды узла (контур на отступе h0, замкнутый поперёк стены), обрезанное краями; против часовой
    /// стрелки. Торец — прямоугольник от начала ветвей до h0 за торцом; угол — пятиугольник по наружному Г и диагонали
    /// редакции рис. 6.2, сдвинутой на h0 от граней (наружные стороны — до концов диагонали).
    /// </summary>
    public static List<PlanPoint> BasePolygon(PunchingWall wall, double h0, IReadOnlyList<PunchingSlabEdge> edges)
    {
        double a1 = wall.A1;
        List<PlanPoint> pts;
        if (wall.Kind == PunchingWallKind.End)
        {
            var (w, xe, x0) = EndFrame(wall, h0, h0);
            pts = [new(x0, -w), new(xe, -w), new(xe, w), new(x0, w)];
        }
        else
        {
            double a2 = wall.T2;
            var (k1, k2) = CornerDiagonalLegs(wall, h0);
            double x1 = a2 + h0 + k1, y1 = a1 + h0 + k2;
            pts = [new(-h0, -h0), new(x1, -h0), new(x1, a1 + h0), new(a2 + h0, y1), new(-h0, y1)];
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
