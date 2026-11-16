namespace CScore.Sp63Punching;

/// <summary>Счёт рядов поперечной арматуры, пересекающих пирамиду продавливания (п. 8.1.48, п. 10.3.17).</summary>
/// <param name="Counted">Число учитываемых рядов (стержней в одной линии поперёк контура).</param>
/// <param name="Total">Всего рядов.</param>
/// <param name="NearFace">Ряды ближе h0/3 к грани — не учитываются.</param>
/// <param name="Outside">Ряды дальше h0 от грани (вне пирамиды у грани) — не учитываются у основного контура.</param>
/// <param name="WorstWindow">Шаг рядов больше h0/3: взято наиневыгоднейшее положение пирамиды.</param>
/// <param name="WindowStart">Начало наиневыгоднейшего окна шириной h0 от грани, м (0 — у грани).</param>
public sealed record PunchingRowCount(int Counted, int Total, int NearFace, int Outside, bool WorstWindow, double WindowStart);

/// <summary>
/// Армированные участки контура при крестообразной раскладке и их характеристики относительно центра тяжести
/// контура бетона (пп. 8.1.48, 8.1.52): лучи вдоль X дают участки у граней ±x (Lsw,y по рис. 8.11г),
/// лучи вдоль Y — у граней ±y (Lsw,x).
/// </summary>
/// <param name="ElementsX">Участки контура под лучами вдоль X.</param>
/// <param name="ElementsY">Участки под лучами вдоль Y.</param>
/// <param name="LX">Длина участков под лучами вдоль X, м.</param>
/// <param name="LY">Длина участков под лучами вдоль Y, м.</param>
/// <param name="IbxX">∫(y − yc)² по участкам лучей вдоль X, м³.</param>
/// <param name="IbyX">∫(x − xc)² по участкам лучей вдоль X, м³.</param>
/// <param name="IbxY">∫(y − yc)² по участкам лучей вдоль Y, м³.</param>
/// <param name="IbyY">∫(x − xc)² по участкам лучей вдоль Y, м³.</param>
public sealed record PunchingArmedSegments(
    IReadOnlyList<ContourElement> ElementsX, IReadOnlyList<ContourElement> ElementsY,
    double LX, double LY, double IbxX, double IbyX, double IbxY, double IbyY)
{
    /// <summary>Пусто.</summary>
    public static PunchingArmedSegments Empty { get; } = new([], [], 0, 0, 0, 0, 0, 0);
}

/// <summary>Итог конструктивной проверки п. 10.3.17.</summary>
public enum PunchingDetailingState
{
    /// <summary>Выполнено.</summary>
    Ok,

    /// <summary>Допускается с учётом только стержней, пересекающих пирамиду (шаг рядов до h0/2).</summary>
    Allowed,

    /// <summary>Нарушено.</summary>
    Violated,

    /// <summary>Не проверяется.</summary>
    NotChecked
}

/// <summary>Конструктивная проверка поперечной арматуры по п. 10.3.17 СП 63.</summary>
/// <param name="Code">Код: first_row_min, first_row_max, row_step, zone_width, line_step_x, line_step_y, line_step, anchorage.</param>
/// <param name="Quantity">Обозначение величины (LaTeX).</param>
/// <param name="Value">Значение, м.</param>
/// <param name="Relation">«≥» или «≤».</param>
/// <param name="Limit">Предел, м.</param>
/// <param name="LimitFormula">Формула предела (LaTeX).</param>
/// <param name="State">Итог.</param>
/// <param name="Text">Русское описание требования.</param>
public sealed record PunchingDetailingCheck(string Code, string Quantity, double Value, string Relation, double Limit,
    string LimitFormula, PunchingDetailingState State, string Text);

/// <summary>Габарит грузовой площади от её центра тяжести: расстояния до граней в стороны ±x, ±y, м.</summary>
public readonly record struct PunchingAreaExtents(PlanPoint O, double XPlus, double XMinus, double YPlus, double YMinus);

/// <summary>Раскладка поперечной арматуры: ряды в пирамиде, лучи креста, контур за крестом, стержни для рисунка.</summary>
public static class PunchingSwLayout
{
    const double Tol = 1e-9;

    /// <summary>
    /// Ряды, пересекающие пирамиду продавливания у грани: расстояние от грани t ∈ [h0/3; h0]
    /// (полоса ±0,5h0 у контура на h0/2; ближе h0/3 — не учитываются, Пособие 2025, п. 2.8).
    /// При шаге sd &gt; h0/3 (п. 10.3.17, абзац 1) — наименьшее число рядов в окне шириной h0,
    /// начало которого сдвигается в пределах [0; h0/2] от грани.
    /// </summary>
    public static PunchingRowCount CountRows(PunchingStirrupRows rows, double h0)
    {
        var t = rows.Positions.ToList();
        int nearFace = t.Count(x => x < h0 / 3 - Tol);
        var eligible = t.Where(x => x >= h0 / 3 - Tol).ToList();
        int InWindow(double a) => eligible.Count(x => x >= a - Tol && x <= a + h0 + Tol);
        if (rows.Step <= h0 / 3 + Tol || rows.Count <= 1)
        {
            int c = InWindow(0);
            return new PunchingRowCount(c, t.Count, nearFace, eligible.Count - c, false, 0);
        }

        // Счёт меняется, когда край окна проходит через ряд: a = t_i и a = t_i − h0.
        var bp = new List<double> { 0, h0 / 2 };
        foreach (var x in eligible)
            foreach (var a in new[] { x, x - h0 })
                if (a > 0 && a < h0 / 2) bp.Add(a);
        bp.Sort();
        var probes = new List<double>(bp);
        for (int i = 0; i + 1 < bp.Count; i++) probes.Add((bp[i] + bp[i + 1]) / 2);
        double worst = 0; int min = int.MaxValue;
        foreach (var a in probes.OrderBy(a => a))
        {
            // Строго внутри окна: ряд на самой границе окна трещину не пересекает.
            int c = eligible.Count(x => x > a + Tol && x < a + h0 - Tol);
            if (c < min) { min = c; worst = a; }
        }
        return new PunchingRowCount(min, t.Count, nearFace, Math.Max(0, eligible.Count - InWindow(0)), true, worst);
    }

    /// <summary>Площадь стержней одной линии, пересекающих пирамиду, м²; без рядов — заданная Asw.</summary>
    public static double AswPerLine(PunchingShearReinforcement r, double h0)
        => r.Rows is { } rows ? rows.BarArea * CountRows(rows, h0).Counted : r.Asw;

    /// <summary>qsw равномерной раскладки (8.92), кН/м.</summary>
    public static double QUniform(PunchingShearReinforcement r, double h0)
        => r.Sw > 0 ? r.Rsw * AswPerLine(r, h0) / r.Sw : 0;

    /// <summary>qsw лучей вдоль X и вдоль Y (8.92), кН/м; нет лучей — 0.</summary>
    public static (double QX, double QY) QCross(PunchingShearReinforcement r, double h0)
    {
        double asw = AswPerLine(r, h0);
        double Q(PunchingCrossArm? a) => a is { Any: true } ? r.Rsw * asw / a.Sw : 0;
        return (Q(r.Cross?.AlongX), Q(r.Cross?.AlongY));
    }

    /// <summary>Габарит площади от её центра тяжести.</summary>
    public static PunchingAreaExtents Extents(PunchingLoadArea area)
    {
        var o = PunchingContourBuilder.AreaCentroid(area);
        var pts = PunchingContourBuilder.AreaPolygon(area);
        return new PunchingAreaExtents(o,
            pts.Max(p => p.X) - o.X, o.X - pts.Min(p => p.X),
            pts.Max(p => p.Y) - o.Y, o.Y - pts.Min(p => p.Y));
    }

    /// <summary>Луч: направление вдоль оси, перпендикуляр, грань и боковые габариты площади.</summary>
    internal readonly record struct ArmFrame(bool AlongX, PlanPoint E, PlanPoint N, double Face, double SidePlus, double SideMinus, PunchingCrossArm Arm);

    internal static IEnumerable<ArmFrame> Arms(PunchingAreaExtents ex, PunchingCrossArms cross)
    {
        if (cross.AlongX is { Any: true } ax)
        {
            if (ax.Positive) yield return new ArmFrame(true, new(1, 0), new(0, 1), ex.XPlus, ex.YPlus, ex.YMinus, ax);
            if (ax.Negative) yield return new ArmFrame(true, new(-1, 0), new(0, -1), ex.XMinus, ex.YMinus, ex.YPlus, ax);
        }
        if (cross.AlongY is { Any: true } ay)
        {
            if (ay.Positive) yield return new ArmFrame(false, new(0, 1), new(-1, 0), ex.YPlus, ex.XMinus, ex.XPlus, ay);
            if (ay.Negative) yield return new ArmFrame(false, new(0, -1), new(1, 0), ex.YMinus, ex.XPlus, ex.XMinus, ay);
        }
    }

    /// <summary>
    /// Полоса луча полуплоскостями a·p ≥ k: за гранью площади, в пределах ширины луча, в углу —
    /// по диагонали от угла площади (чтобы соседние лучи не делили один участок).
    /// </summary>
    static (PlanPoint A, double K)[] Region(PlanPoint o, ArmFrame f)
    {
        var e = f.E; var n = f.N; double w2 = f.Arm.Width / 2;
        var emn = e - n; var epn = e + n;
        return
        [
            (e, e.Dot(o) + f.Face),
            (new PlanPoint(-n.X, -n.Y), -n.Dot(o) - w2),
            (n, n.Dot(o) - w2),
            (emn, emn.Dot(o) + f.Face - f.SidePlus),
            (epn, epn.Dot(o) + f.Face - f.SideMinus)
        ];
    }

    /// <summary>Участки контура под лучами креста и их характеристики относительно центра тяжести c.</summary>
    public static PunchingArmedSegments Armed(PunchingContour contour, PunchingLoadArea area, PunchingCrossArms cross, PlanPoint c)
    {
        var ex = Extents(area);
        var xs = new List<ContourElement>();
        var ys = new List<ContourElement>();
        foreach (var f in Arms(ex, cross))
        {
            IEnumerable<ContourElement> parts = contour.Elements.ToList();
            foreach (var (a, k) in Region(ex.O, f))
                parts = parts.SelectMany(el => el.ClipToHalfPlane(a, k)).ToList();
            (f.AlongX ? xs : ys).AddRange(parts.Where(p => p.Length > 1e-12));
        }
        double lx = xs.Sum(e => e.Length), ly = ys.Sum(e => e.Length);
        double ibxX = 0, ibyX = 0, ibxY = 0, ibyY = 0;
        foreach (var e in xs) { var (ixx, iyy) = e.SecondMoments(c); ibyX += ixx; ibxX += iyy; }
        foreach (var e in ys) { var (ixx, iyy) = e.SecondMoments(c); ibyY += ixx; ibxY += iyy; }
        return new PunchingArmedSegments(xs, ys, lx, ly, ibxX, ibyX, ibxY, ibyY);
    }

    /// <summary>
    /// Граница контура за крестом (п. 8.1.48, рис. 8.11г): выпуклая оболочка концевых отрезков лучей шириной Lsw
    /// на h0/2 за внешним рядом и площади, смещённой на h0/2 (стороны без лучей); против часовой стрелки.
    /// </summary>
    public static List<PlanPoint> CrossBeyondPolygon(PunchingLoadArea area, PunchingCrossArms cross, double extent, double h0)
    {
        var ex = Extents(area);
        var pts = new List<PlanPoint>(PunchingContourBuilder.OffsetAreaPolygon(area, h0 / 2));
        foreach (var f in Arms(ex, cross))
        {
            var end = ex.O + (f.Face + extent + h0 / 2) * f.E;
            pts.Add(end + f.Arm.Width / 2 * f.N);
            pts.Add(end - f.Arm.Width / 2 * f.N);
        }
        return PunchingContourBuilder.ConvexHullCcw(pts);
    }

    /// <summary>
    /// Короткое русское название требования п. 10.3.17 по коду <see cref="PunchingDetailingCheck.Code"/>
    /// (для перечня нарушений в предупреждении); неизвестный код — сам код.
    /// </summary>
    public static string DetailingName(string code) => code switch
    {
        "first_row_min" => "первый ряд не ближе h0/3",
        "first_row_max" => "первый ряд не далее h0/2",
        "row_step" => "шаг рядов",
        "zone_width" => "ширина зоны",
        "line_step" => "шаг вдоль контура",
        "line_step_x" => "шаг линий лучей вдоль X",
        "line_step_y" => "шаг линий лучей вдоль Y",
        "anchorage" => "анкеровка",
        _ => code,
    };

    /// <summary>
    /// Конструктивные требования п. 10.3.17 к рядам и шагу линий. Пусто — ряды не заданы (проверка невозможна).
    /// Стороны контура для шага вдоль контура — по габариту площади + h0 (у прямоугольника — стороны контура на h0/2).
    /// </summary>
    public static List<PunchingDetailingCheck> Detailing(PunchingInput input)
    {
        var list = new List<PunchingDetailingCheck>();
        var r = input.Reinforcement;
        if (r?.Rows is not { } rows) return list;
        double h0 = input.H0;
        const double eps = 1e-9;
        PunchingDetailingState Ge(double v, double lim) => v >= lim - eps ? PunchingDetailingState.Ok : PunchingDetailingState.Violated;
        PunchingDetailingState Le(double v, double lim) => v <= lim + eps ? PunchingDetailingState.Ok : PunchingDetailingState.Violated;

        list.Add(new("first_row_min", "s_1", rows.FirstRow, "≥", h0 / 3, @"h_0/3", Ge(rows.FirstRow, h0 / 3),
            "Первый ряд — не ближе h0/3 от грани грузовой площади"));
        list.Add(new("first_row_max", "s_1", rows.FirstRow, "≤", h0 / 2, @"h_0/2", Le(rows.FirstRow, h0 / 2),
            "Первый ряд — не далее h0/2 от грани грузовой площади"));
        if (rows.Count > 1)
        {
            double lim = Math.Min(h0 / 3, 0.3);
            var st = rows.Step <= lim + eps ? PunchingDetailingState.Ok
                : rows.Step <= Math.Min(h0 / 2, 0.3) + eps ? PunchingDetailingState.Allowed
                : PunchingDetailingState.Violated;
            list.Add(new("row_step", "s_d", rows.Step, "≤", lim, @"\min(h_0/3;\ 300\ мм)", st,
                "Шаг рядов поперёк сторон контура — не более h0/3 и 300 мм (допускается до h0/2 с учётом только стержней, пересекающих пирамиду)"));
        }
        list.Add(new("zone_width", @"s_1 + (n_r - 1)\,s_d", rows.Extent, "≥", 1.5 * h0, @"1{,}5\,h_0", Ge(rows.Extent, 1.5 * h0),
            "Ширина зоны постановки поперечной арматуры от грани грузовой площади — не менее 1,5h0"));

        if (input.LoadArea is not (PunchingLoadArea.Circle or PunchingLoadArea.Ring))
        {
            var ex = Extents(input.LoadArea);
            double sideX = ex.XPlus + ex.XMinus + h0, sideY = ex.YPlus + ex.YMinus + h0;   // стороны контура вдоль X и вдоль Y
            if (r.Cross is { } cross)
            {
                // Лучи вдоль X пересекают стороны контура, параллельные Y (длина sideY), и наоборот.
                if (cross.AlongX is { Any: true } ax)
                    list.Add(new("line_step_x", @"s_{w}", ax.Sw, "≤", sideY / 4, @"L_y/4", Le(ax.Sw, sideY / 4),
                        "Шаг линий лучей вдоль X вдоль стороны контура — не более 1/4 её длины"));
                if (cross.AlongY is { Any: true } ay)
                    list.Add(new("line_step_y", @"s_{w}", ay.Sw, "≤", sideX / 4, @"L_x/4", Le(ay.Sw, sideX / 4),
                        "Шаг линий лучей вдоль Y вдоль стороны контура — не более 1/4 её длины"));
            }
            else if (r.Sw > 0)
            {
                double side = Math.Min(sideX, sideY);
                list.Add(new("line_step", @"s_{w}", r.Sw, "≤", side / 4, @"L_{min}/4", Le(r.Sw, side / 4),
                    "Шаг стержней вдоль сторон контура — не более 1/4 длины стороны"));
            }
        }
        list.Add(new("anchorage", "", 0, "", 0, "", PunchingDetailingState.NotChecked,
            "Анкеровка поперечной арматуры (пп. 8.1.48, 10.3) не проверяется"));
        return list;
    }

    /// <summary>Стержни для рисунка: центры в плане, м. Без рядов — пусто.</summary>
    public static List<PlanPoint> BarPoints(PunchingInput input)
    {
        var r = input.Reinforcement;
        var list = new List<PlanPoint>();
        if (r?.Rows is not { Count: > 0 } rows) return list;
        if (r.Cross is { } cross)
        {
            var ex = Extents(input.LoadArea);
            foreach (var f in Arms(ex, cross))
            {
                int lines = (int)Math.Round(f.Arm.Width / f.Arm.Sw) + 1;
                if (lines > 400) continue;
                foreach (var t in rows.Positions)
                    for (int j = 0; j < lines; j++)
                        list.Add(ex.O + (f.Face + t) * f.E + (-f.Arm.Width / 2 + j * f.Arm.Sw) * f.N);
            }
            return list;
        }
        if (r.Sw <= 0) return list;
        foreach (var t in rows.Positions)
        {
            var poly = PunchingContourBuilder.OffsetAreaPolygon(input.LoadArea, t);
            double per = 0;
            for (int i = 0; i < poly.Count; i++) per += (poly[(i + 1) % poly.Count] - poly[i]).Length;
            if (per / r.Sw > 2000) continue;
            // Стержни вдоль ряда с шагом Sw, начиная с середины первой стороны.
            double next = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                double len = (b - a).Length, s = next;
                while (s <= len + Tol) { list.Add(a + s / len * (b - a)); s += r.Sw; }
                next = s - len;
            }
        }
        return list;
    }
}
