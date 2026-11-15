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
