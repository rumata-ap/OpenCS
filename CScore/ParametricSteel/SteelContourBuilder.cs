using CScore.Sp16;

namespace CScore.ParametricSteel;

/// <summary>
/// Канонические контуры стальных профилей: острый многоугольник с радиусами вершин, скругляемый
/// дугами, касательными к обоим рёбрам (грани полок с уклоном — прямые, закругления — касательные).
/// </summary>
internal static class SteelContourBuilder
{
    /// <summary>Число сегментов дуги на четверть окружности.</summary>
    public const int SegmentsPerQuarter = 8;
    /// <summary>Число сегментов окружности трубы и круга.</summary>
    public const int CircleSegments = 96;

    /// <summary>Вершина острого многоугольника с радиусом скругления (0 — острый угол).</summary>
    public readonly record struct Vertex(double X, double Y, double R = 0);

    /// <summary>Наружный контур (CCW) и отверстия канонического положения.</summary>
    public sealed record Shape(List<(double X, double Y)> Outer, List<List<(double X, double Y)>> Holes);

    /// <summary>
    /// Скругляет вершины с R &gt; 0. Возвращает null, если скругление не помещается на ребре
    /// (сумма касательных отрезков соседних вершин больше длины ребра).
    /// </summary>
    public static List<(double X, double Y)>? RoundCorners(IReadOnlyList<Vertex> v)
    {
        int n = v.Count;
        var tangent = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (v[i].R <= 0) continue;
            var (ux, uy, _) = Unit(v[i], v[(i - 1 + n) % n]);
            var (wx, wy, _) = Unit(v[i], v[(i + 1) % n]);
            double cos = Math.Clamp(ux * wx + uy * wy, -1, 1);
            double theta = Math.Acos(cos);                    // внутренний угол между рёбрами
            if (theta < 1e-9 || Math.PI - theta < 1e-9) return null;
            tangent[i] = v[i].R / Math.Tan(theta / 2);
        }
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            double len = Unit(v[i], v[j]).Len;
            if (tangent[i] + tangent[j] > len * (1 + 1e-9)) return null;
        }

        var pts = new List<(double X, double Y)>();
        for (int i = 0; i < n; i++)
        {
            if (v[i].R <= 0 || tangent[i] <= 0) { pts.Add((v[i].X, v[i].Y)); continue; }
            var (ux, uy, _) = Unit(v[i], v[(i - 1 + n) % n]);
            var (wx, wy, _) = Unit(v[i], v[(i + 1) % n]);
            double t = tangent[i];
            var a = (X: v[i].X + ux * t, Y: v[i].Y + uy * t);   // касание на входящем ребре
            var b = (X: v[i].X + wx * t, Y: v[i].Y + wy * t);   // касание на исходящем ребре
            double bx = ux + wx, by = uy + wy, bl = Math.Sqrt(bx * bx + by * by);
            double theta = Math.Acos(Math.Clamp(ux * wx + uy * wy, -1, 1));
            double dc = v[i].R / Math.Sin(theta / 2);
            var c = (X: v[i].X + bx / bl * dc, Y: v[i].Y + by / bl * dc);
            double a0 = Math.Atan2(a.Y - c.Y, a.X - c.X), a1 = Math.Atan2(b.Y - c.Y, b.X - c.X);
            double sweep = a1 - a0;
            while (sweep > Math.PI) sweep -= 2 * Math.PI;
            while (sweep < -Math.PI) sweep += 2 * Math.PI;
            int segs = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2) * SegmentsPerQuarter));
            for (int k = 0; k <= segs; k++)
            {
                double ang = a0 + sweep * k / segs;
                pts.Add((c.X + v[i].R * Math.Cos(ang), c.Y + v[i].R * Math.Sin(ang)));
            }
        }
        return pts;
    }

    static (double X, double Y, double Len) Unit(Vertex from, Vertex to)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y, l = Math.Sqrt(dx * dx + dy * dy);
        return l > 0 ? (dx / l, dy / l, l) : (0, 0, 0);
    }

    /// <summary>Правильный многоугольник с радиусом, дающим площадь круга диаметра d.</summary>
    public static List<(double X, double Y)> Circle(double d, bool clockwise = false)
    {
        int n = CircleSegments;
        double r = d / 2 * Math.Sqrt(2 * Math.PI / (n * Math.Sin(2 * Math.PI / n)));
        var pts = new List<(double X, double Y)>(n);
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n * (clockwise ? -1 : 1);
            pts.Add((r * Math.Cos(a), r * Math.Sin(a)));
        }
        return pts;
    }

    /// <summary>Строит канонический контур; null — радиусы не помещаются в геометрию.</summary>
    public static Shape? Build(ParametricSteelSectionDefinition d)
    {
        List<(double X, double Y)>? outer;
        var holes = new List<List<(double X, double Y)>>();
        switch (d.Kind)
        {
            case SteelProfileKind.IBeam:
                outer = RoundCorners(d.Fabrication == SteelFabrication.Rolled ? RolledIBeam(d) : WeldedIBeam(d));
                break;
            case SteelProfileKind.Channel:
                outer = RoundCorners(d.Fabrication switch
                {
                    SteelFabrication.Rolled => RolledChannel(d),
                    SteelFabrication.Bent => BentChannel(d),
                    _ => WeldedChannel(d),
                });
                break;
            case SteelProfileKind.Tee:
                outer = RoundCorners(WeldedTee(d));
                break;
            case SteelProfileKind.Angle:
                outer = RoundCorners(Angle(d));
                break;
            case SteelProfileKind.Box:
            {
                bool bent = d.Fabrication == SteelFabrication.Bent;
                double tw = d.Tw, tf = bent ? d.Tw : d.Tf1;
                outer = RoundCorners(Rect(d.Bf1, d.H, bent ? d.R1 + tw : 0));
                var hole = RoundCorners(Rect(d.Bf1 - 2 * tw, d.H - 2 * tf, bent ? d.R1 : 0));
                if (hole == null) return null;
                hole.Reverse();
                holes.Add(hole);
                break;
            }
            case SteelProfileKind.Pipe:
                outer = Circle(d.H);
                holes.Add(Circle(d.H - 2 * d.Tw, clockwise: true));
                break;
            case SteelProfileKind.Rect:
                outer = RoundCorners(Rect(d.Bf1, d.H, 0));
                break;
            case SteelProfileKind.Round:
                outer = Circle(d.H);
                break;
            default:
                return null;
        }
        return outer == null ? null : new Shape(outer, holes);
    }

    /// <summary>Прямоугольник w×h с центром в нуле, все углы с радиусом r (CCW).</summary>
    static List<Vertex> Rect(double w, double h, double r) =>
        [new(-w / 2, -h / 2, r), new(w / 2, -h / 2, r), new(w / 2, h / 2, r), new(-w / 2, h / 2, r)];

    /// <summary>
    /// Прокатный двутавр: толщина полки tf — посередине свеса L = (b − tw)/2 (ГОСТ 8239: на (b − s)/4
    /// от кромки); у стенки tf + i·L/2, у кромки tf − i·L/2.
    /// </summary>
    static List<Vertex> RolledIBeam(ParametricSteelSectionDefinition d)
    {
        double hh = d.H / 2, hb = d.Bf1 / 2, htw = d.Tw / 2;
        double l = (d.Bf1 - d.Tw) / 2, tW = d.Tf1 + d.FlangeSlope * l / 2, tE = d.Tf1 - d.FlangeSlope * l / 2;
        double r1 = d.R1, r2 = d.R2;
        return
        [
            new(-hb, -hh), new(hb, -hh), new(hb, -hh + tE, r2), new(htw, -hh + tW, r1),
            new(htw, hh - tW, r1), new(hb, hh - tE, r2), new(hb, hh), new(-hb, hh),
            new(-hb, hh - tE, r2), new(-htw, hh - tW, r1), new(-htw, -hh + tW, r1), new(-hb, -hh + tE, r2),
        ];
    }

    /// <summary>Сварной двутавр: верхний пояс Bf1/Tf1, нижний — BfBottom/TfBottom, стенка по оси.</summary>
    static List<Vertex> WeldedIBeam(ParametricSteelSectionDefinition d)
    {
        double hh = d.H / 2, ht = d.Bf1 / 2, hbot = d.BfBottom / 2, htw = d.Tw / 2;
        double tt = d.Tf1, tb = d.TfBottom;
        return
        [
            new(-hbot, -hh), new(hbot, -hh), new(hbot, -hh + tb), new(htw, -hh + tb),
            new(htw, hh - tt), new(ht, hh - tt), new(ht, hh), new(-ht, hh),
            new(-ht, hh - tt), new(-htw, hh - tt), new(-htw, -hh + tb), new(-hbot, -hh + tb),
        ];
    }

    /// <summary>
    /// Прокатный швеллер стенкой слева: tf — посередине свеса L = b − tw (ГОСТ 8240: на (b − s)/2 от
    /// кромки); у стенки tf + i·L/2, у кромки tf − i·L/2.
    /// </summary>
    static List<Vertex> RolledChannel(ParametricSteelSectionDefinition d)
    {
        double h = d.H, b = d.Bf1, tw = d.Tw;
        double l = b - tw, tW = d.Tf1 + d.FlangeSlope * l / 2, tE = d.Tf1 - d.FlangeSlope * l / 2;
        return
        [
            new(0, 0), new(b, 0), new(b, tE, d.R2), new(tw, tW, d.R1),
            new(tw, h - tW, d.R1), new(b, h - tE, d.R2), new(b, h), new(0, h),
        ];
    }

    /// <summary>Гнутый швеллер: постоянная толщина, внутренний радиус R1, наружный R1 + t.</summary>
    static List<Vertex> BentChannel(ParametricSteelSectionDefinition d)
    {
        double h = d.H, b = d.Bf1, t = d.Tw, ri = d.R1, ro = d.R1 + t;
        return
        [
            new(0, 0, ro), new(b, 0), new(b, t), new(t, t, ri),
            new(t, h - t, ri), new(b, h - t), new(b, h), new(0, h, ro),
        ];
    }

    /// <summary>Сварной швеллер стенкой слева.</summary>
    static List<Vertex> WeldedChannel(ParametricSteelSectionDefinition d)
    {
        double h = d.H, b = d.Bf1, tw = d.Tw, tf = d.Tf1;
        return [new(0, 0), new(b, 0), new(b, tf), new(tw, tf), new(tw, h - tf), new(b, h - tf), new(b, h), new(0, h)];
    }

    /// <summary>Сварной тавр полкой сверху.</summary>
    static List<Vertex> WeldedTee(ParametricSteelSectionDefinition d)
    {
        double hb = d.Bf1 / 2, htw = d.Tw / 2, h = d.H, tf = d.Tf1;
        return
        [
            new(-htw, 0), new(htw, 0), new(htw, h - tf), new(hb, h - tf),
            new(hb, h), new(-hb, h), new(-hb, h - tf), new(-htw, h - tf),
        ];
    }

    /// <summary>
    /// Уголок пяткой в (0, 0): вертикальная полка H, горизонтальная Bf1, толщина Tw. Прокат — R1 у
    /// корня, R2 у перьев; гнутый — внутренний R1, наружный R1 + t у пятки.
    /// </summary>
    static List<Vertex> Angle(ParametricSteelSectionDefinition d)
    {
        double h = d.H, b = d.Bf1, t = d.Tw;
        bool bent = d.Fabrication == SteelFabrication.Bent;
        double heel = bent ? d.R1 + t : 0, root = d.R1, toe = bent ? 0 : d.R2;
        return [new(0, 0, heel), new(b, 0), new(b, t, toe), new(t, t, root), new(t, h, toe), new(0, h)];
    }
}
