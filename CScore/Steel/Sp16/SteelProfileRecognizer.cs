namespace CScore.Sp16;

/// <summary>
/// Распознавание вида и размеров профиля по полигону сечения. Работает с шаблонами OpenCS и
/// профилями сортамента (с закруглениями и уклоном внутренних граней полок): толщины меряются
/// хордами полигона, средняя толщина полки с уклоном — посередине свеса (как в ГОСТ 8239/8240).
/// Если распознать не удалось — возвращает <see cref="SteelProfileKind.Generic"/>.
/// </summary>
public static class SteelProfileRecognizer
{
    const double RelTol = 0.02;

    /// <summary>Распознаёт профиль; при неудаче — Generic.</summary>
    public static SteelProfile Recognize(PolygonSection s)
    {
        if (s.Holes.Count == 1) return RecognizeClosed(s);
        if (s.Holes.Count > 1) return Generic;

        if (IsCircle(s.Outer, s.Xc, s.Yc, out double rOut)) return new SteelProfile { Kind = SteelProfileKind.Round, H = 2 * rOut };
        double w = s.XMax - s.XMin, h = s.YMax - s.YMin;
        if (s.Outer.Count == 4 && Math.Abs(s.A - w * h) <= 1e-3 * w * h)
            return new SteelProfile { Kind = SteelProfileKind.Rect, H = h, Bf1 = w, Fabrication = SteelFabrication.Rolled };

        var p = RecognizeOpen(s);
        if (p != null) return p;
        var swapped = s.SwapAxes();
        p = RecognizeOpen(swapped);
        if (p != null && p.Kind != SteelProfileKind.Angle) return p with { Rotated90 = true };
        return Generic;
    }

    static SteelProfile Generic => new() { Kind = SteelProfileKind.Generic };

    static bool IsCircle(IReadOnlyList<(double X, double Y)> ring, double xc, double yc, out double r)
    {
        r = 0;
        if (ring.Count < 12) return false;
        var rs = ring.Select(p => Math.Sqrt((p.X - xc) * (p.X - xc) + (p.Y - yc) * (p.Y - yc))).ToList();
        double max = rs.Max(), min = rs.Min();
        if (max <= 0 || (max - min) / max > RelTol) return false;
        r = rs.Average();
        return true;
    }

    static SteelProfile RecognizeClosed(PolygonSection s)
    {
        var hole = s.Holes[0];
        if (IsCircle(s.Outer, s.Xc, s.Yc, out double rOut))
        {
            double hx = hole.Average(p => p.X), hy = hole.Average(p => p.Y);
            if (IsCircle(hole, hx, hy, out double rIn) && Math.Abs(hx - s.Xc) < 0.01 * rOut && Math.Abs(hy - s.Yc) < 0.01 * rOut)
                return new SteelProfile { Kind = SteelProfileKind.Pipe, H = 2 * rOut, Tw = rOut - rIn, Fabrication = SteelFabrication.Rolled };
            return Generic;
        }

        double w = s.XMax - s.XMin, h = s.YMax - s.YMin;
        double xm = (s.XMin + s.XMax) / 2, ym = (s.YMin + s.YMax) / 2;
        var hor = s.ChordSegments(ym, true);
        var ver = s.ChordSegments(xm, false);
        if (hor.Count != 2 || ver.Count != 2) return Generic;
        double twL = hor[0].To - hor[0].From, twR = hor[1].To - hor[1].From;
        double tfB = ver[0].To - ver[0].From, tfT = ver[1].To - ver[1].From;
        if (Math.Abs(twL - twR) > RelTol * Math.Max(twL, twR) || Math.Abs(tfB - tfT) > RelTol * Math.Max(tfB, tfT))
            return Generic;

        // Гнутый замкнутый профиль — скруглённые наружные углы (угол габарита вне материала).
        double eps = 1e-4 * Math.Min(w, h);
        bool sharp = s.Contains(s.XMin + eps, s.YMin + eps) && s.Contains(s.XMax - eps, s.YMax - eps);
        double r = 0;
        if (!sharp)
        {
            // Наружный радиус по отступу контура от угла габарита по диагонали: d = r(√2 − 1).
            double d = 0;
            for (double t = 0; t < Math.Min(w, h) / 2; t += Math.Min(w, h) / 2000)
            {
                if (s.Contains(s.XMin + t, s.YMin + t)) { d = t * Math.Sqrt(2); break; }
            }
            r = d / (Math.Sqrt(2) - 1);
        }
        return new SteelProfile
        {
            Kind = SteelProfileKind.Box, H = h, Bf1 = w, Tf1 = (tfB + tfT) / 2, Tw = (twL + twR) / 2, R = r,
            Fabrication = sharp ? SteelFabrication.Welded : SteelFabrication.Bent,
        };
    }

    /// <summary>Двутавр, швеллер, тавр или уголок в каноническом положении; null — не подошло.</summary>
    static SteelProfile? RecognizeOpen(PolygonSection s)
    {
        return TryIBeam(s) ?? TryChannel(s) ?? TryTee(s) ?? TryAngle(s);
    }

    static double Len((double From, double To) seg) => seg.To - seg.From;

    static SteelProfile? TryIBeam(PolygonSection s)
    {
        double h = s.YMax - s.YMin, xm = (s.XMin + s.XMax) / 2;
        var mid = s.ChordSegments((s.YMin + s.YMax) / 2, true);
        if (mid.Count != 1) return null;
        double tw = Len(mid[0]);
        if (Math.Abs((mid[0].From + mid[0].To) / 2 - xm) > 0.01 * (s.XMax - s.XMin)) return null;

        // Толщина полок посередине свеса: вертикальная хорда пересекает оба пояса.
        double bfMax = s.XMax - s.XMin;
        double xOut = xm + tw / 2 + (bfMax / 2 - tw / 2) / 2;
        var ver = s.ChordSegments(xOut, false);
        if (ver.Count != 2) return null;
        double tf2 = Len(ver[0]), tf1 = Len(ver[1]);
        if (ver[0].From > s.YMin + 1e-6 * h || ver[1].To < s.YMax - 1e-6 * h) return null;

        var top = s.ChordSegments(s.YMax - tf1 / 2, true);
        var bot = s.ChordSegments(s.YMin + tf2 / 2, true);
        if (top.Count != 1 || bot.Count != 1) return null;
        double bf1 = Len(top[0]), bf2 = Len(bot[0]);
        if (bf1 < 1.5 * tw || bf2 < 1.5 * tw) return null;
        if (Math.Abs((top[0].From + top[0].To) / 2 - xm) > 0.01 * bfMax) return null;
        if (Math.Abs((bot[0].From + bot[0].To) / 2 - xm) > 0.01 * bfMax) return null;

        var (hefTop, hefBot) = StraightWeb(s, xm, tw, (s.YMin + s.YMax) / 2);
        double r = FilletRadius(s, xm, tw, bf1, s.YMax, hefTop, top: true);
        double area = bf1 * tf1 + bf2 * tf2 + (h - tf1 - tf2) * tw;
        if (Math.Abs(area - s.A) > 0.08 * s.A) return null;
        bool rolled = r > 1e-4;
        return new SteelProfile
        {
            Kind = SteelProfileKind.IBeam, H = h, Bf1 = bf1, Tf1 = tf1, Bf2 = bf2, Tf2 = tf2, Tw = tw,
            R = rolled ? r : 0, Fabrication = rolled ? SteelFabrication.Rolled : SteelFabrication.Welded,
        };
    }

    /// <summary>Границы прямолинейного участка стенки (где ширина хорды ≈ tw) вверх и вниз от уровня y0.</summary>
    static (double Top, double Bottom) StraightWeb(PolygonSection s, double xWebCenter, double tw, double y0)
    {
        double h = s.YMax - s.YMin, step = h / 4000;
        double top = y0, bot = y0;
        for (double y = y0; y < s.YMax; y += step)
        {
            if (s.ChordLength(y, true) > tw * 1.01) break;
            top = y;
        }
        for (double y = y0; y > s.YMin; y -= step)
        {
            if (s.ChordLength(y, true) > tw * 1.01) break;
            bot = y;
        }
        return (top, bot);
    }

    /// <summary>
    /// Радиус внутреннего закругления: от конца прямолинейного участка стенки до внутренней
    /// грани полки у стенки (толщина полки у стенки экстраполируется по уклону грани).
    /// </summary>
    static double FilletRadius(PolygonSection s, double xWeb, double tw, double bf, double yFace, double straightEnd, bool top)
    {
        double x0 = xWeb + tw / 2;
        double out1 = x0 + (bf / 2 - tw / 2) * 0.5, out2 = x0 + (bf / 2 - tw / 2) * 0.8;
        double T(double x)
        {
            var segs = s.ChordSegments(x, false);
            if (segs.Count == 0) return 0;
            var seg = top ? segs[^1] : segs[0];
            return Len(seg);
        }
        double t1 = T(out1), t2 = T(out2);
        double k = (t1 - t2) / (out2 - out1);
        double tAtWeb = t1 + k * (out1 - x0);
        double innerFace = top ? yFace - tAtWeb : yFace + tAtWeb;
        double r = top ? innerFace - straightEnd : straightEnd - innerFace;
        return r > 0 ? r : 0;
    }

    static SteelProfile? TryChannel(PolygonSection s)
    {
        double h = s.YMax - s.YMin, b = s.XMax - s.XMin;
        var mid = s.ChordSegments((s.YMin + s.YMax) / 2, true);
        if (mid.Count != 1) return null;
        bool left = Math.Abs(mid[0].From - s.XMin) < 1e-6 * b;
        bool right = Math.Abs(mid[0].To - s.XMax) < 1e-6 * b;
        if (left == right) return null;
        double tw = Len(mid[0]);
        if (tw > 0.6 * b) return null;
        double xOut = left ? s.XMin + tw + (b - tw) / 2 : s.XMax - tw - (b - tw) / 2;
        var ver = s.ChordSegments(xOut, false);
        if (ver.Count != 2) return null;
        double tfB = Len(ver[0]), tfT = Len(ver[1]);
        if (Math.Abs(tfB - tfT) > RelTol * Math.Max(tfB, tfT)) return null;
        var top = s.ChordSegments(s.YMax - tfT / 2, true);
        if (top.Count != 1 || Math.Abs(Len(top[0]) - b) > RelTol * b) return null;
        double tf = (tfB + tfT) / 2;
        double area = 2 * b * tf + (h - 2 * tf) * tw;
        if (Math.Abs(area - s.A) > 0.08 * s.A) return null;
        double xWebCenter = left ? s.XMin + tw / 2 : s.XMax - tw / 2;
        var (hefTop, _) = StraightWeb(s, xWebCenter, tw, (s.YMin + s.YMax) / 2);
        // Толщина полки у стенки — экстраполяцией по уклону внутренней грани.
        double x0 = left ? s.XMin + tw : s.XMax - tw, dir = left ? 1 : -1;
        double T(double x) { var segs = s.ChordSegments(x, false); return segs.Count == 0 ? 0 : Len(segs[^1]); }
        double xa = x0 + dir * (b - tw) * 0.5, xb = x0 + dir * (b - tw) * 0.8;
        double ta = T(xa), tb = T(xb);
        double tAtWeb = ta + (ta - tb) / Math.Abs(xb - xa) * Math.Abs(xa - x0);
        double r = Math.Max(0, s.YMax - tAtWeb - hefTop);
        bool rolled = r > 1e-4;
        return new SteelProfile
        {
            Kind = SteelProfileKind.Channel, H = h, Bf1 = b, Tf1 = tf, Tw = tw, R = rolled ? r : 0,
            Fabrication = rolled ? SteelFabrication.Rolled : SteelFabrication.Bent, Flipped = right,
        };
    }

    static SteelProfile? TryTee(PolygonSection s)
    {
        double h = s.YMax - s.YMin, b = s.XMax - s.XMin, xm = (s.XMin + s.XMax) / 2;
        var mid = s.ChordSegments((s.YMin + s.YMax) / 2, true);
        if (mid.Count != 1 || Math.Abs((mid[0].From + mid[0].To) / 2 - xm) > 0.01 * b) return null;
        double tw = Len(mid[0]);
        var ver = s.ChordSegments(xm + tw / 2 + (b / 2 - tw / 2) / 2, false);
        if (ver.Count != 1) return null;
        bool flangeTop = Math.Abs(ver[0].To - s.YMax) < 1e-6 * h;
        bool flangeBot = Math.Abs(ver[0].From - s.YMin) < 1e-6 * h;
        if (flangeTop == flangeBot) return null;
        double tf = Len(ver[0]);
        double yFl = flangeTop ? s.YMax - tf / 2 : s.YMin + tf / 2;
        var fl = s.ChordSegments(yFl, true);
        if (fl.Count != 1 || Len(fl[0]) < 1.5 * tw) return null;
        double area = b * tf + (h - tf) * tw;
        if (Math.Abs(area - s.A) > 0.08 * s.A) return null;
        var (up, down) = StraightWeb(s, xm, tw, (s.YMin + s.YMax) / 2);
        double r = FilletRadius(s, xm, tw, b, flangeTop ? s.YMax : s.YMin, flangeTop ? up : down, top: flangeTop);
        bool rolled = r > 1e-4;
        return new SteelProfile
        {
            Kind = SteelProfileKind.Tee, H = h, Bf1 = b, Tf1 = tf, Tw = tw, R = rolled ? r : 0,
            Fabrication = rolled ? SteelFabrication.Rolled : SteelFabrication.Welded, Flipped = !flangeTop,
        };
    }

    static SteelProfile? TryAngle(PolygonSection s)
    {
        double w = s.XMax - s.XMin, h = s.YMax - s.YMin, e = 1e-4 * Math.Min(w, h);
        bool bl = s.Contains(s.XMin + e, s.YMin + e), br = s.Contains(s.XMax - e, s.YMin + e);
        bool tl = s.Contains(s.XMin + e, s.YMax - e), tr = s.Contains(s.XMax - e, s.YMax - e);
        // Пятка — единственный заполненный угол, у которого заполнены оба соседних.
        bool heelLeft, heelBottom;
        if (bl && br && tl && !tr) { heelLeft = true; heelBottom = true; }
        else if (br && bl && tr && !tl) { heelLeft = false; heelBottom = true; }
        else if (tl && tr && bl && !br) { heelLeft = true; heelBottom = false; }
        else if (tr && tl && br && !bl) { heelLeft = false; heelBottom = false; }
        else return null;
        // Толщина вертикальной полки — горизонтальная хорда на середине высоты, горизонтальной — вертикальная.
        var hs = s.ChordSegments((s.YMin + s.YMax) / 2, true);
        var vs = s.ChordSegments((s.XMin + s.XMax) / 2, false);
        if (hs.Count != 1 || vs.Count != 1) return null;
        double tv = Len(hs[0]), th = Len(vs[0]);
        if (heelLeft ? Math.Abs(hs[0].From - s.XMin) > e * 10 : Math.Abs(hs[0].To - s.XMax) > e * 10) return null;
        if (heelBottom ? Math.Abs(vs[0].From - s.YMin) > e * 10 : Math.Abs(vs[0].To - s.YMax) > e * 10) return null;
        if (Math.Abs(tv - th) > RelTol * Math.Max(tv, th)) return null;
        double t = (tv + th) / 2;
        double area = (w + h - t) * t;
        if (Math.Abs(area - s.A) > 0.08 * s.A) return null;
        // Радиус закругления у пятки: по площади сверх идеального уголка r² (1 − π/4) ≈ ΔA (скругления перьев не учитываются).
        double dA = s.A - area;
        double r = dA > 0 ? Math.Sqrt(dA / (1 - Math.PI / 4)) : 0;
        bool rolled = r > 1e-4;
        return new SteelProfile
        {
            Kind = SteelProfileKind.Angle, H = h, Bf1 = w, Tw = t, Tf1 = t, R = rolled ? r : 0,
            Fabrication = rolled ? SteelFabrication.Rolled : SteelFabrication.Bent,
        };
    }
}
