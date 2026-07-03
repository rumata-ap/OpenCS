namespace CScore;

public static class TemplatePoints
{
    public static List<(double X, double Y)> RectPoints(double w, double h)
    {
        double hw = w / 2, hh = h / 2;
        return [(-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh)];
    }

    public static List<(double X, double Y)> TeePoints(double w, double h, double tw, double tf)
    {
        double hw = w / 2, htw = tw / 2;
        var pts = new List<(double X, double Y)>
        {
            (-hw, 0), (hw, 0), (hw, -tf), (htw, -tf),
            (htw, -h), (-htw, -h), (-htw, -tf), (-hw, -tf),
        };
        var (cx, cy) = PolygonCentroid(pts);
        return pts.Select(p => (p.Item1 - cx, p.Item2 - cy)).ToList();
    }

    public static List<(double X, double Y)> IBeamPoints(double h, double w, double tw, double tf)
    {
        double hw = w / 2, htw = tw / 2, hh = h / 2;
        return
        [
            (-hw, -hh), (hw, -hh), (hw, -hh + tf), (htw, -hh + tf),
            (htw, hh - tf), (hw, hh - tf), (hw, hh), (-hw, hh),
            (-hw, hh - tf), (-htw, hh - tf), (-htw, -hh + tf), (-hw, -hh + tf),
        ];
    }

    public static List<(double X, double Y)> AnglePoints(double w, double h, double tw, double tf)
    {
        var pts = new List<(double X, double Y)>
        {
            (0, 0), (w, 0), (w, tf), (tw, tf), (tw, h), (0, h),
        };
        var (cx, cy) = PolygonCentroid(pts);
        return pts.Select(p => (p.Item1 - cx, p.Item2 - cy)).ToList();
    }

    public static List<(double X, double Y)> CirclePoints(double diameter, int nSegments = 32)
    {
        double r = diameter / 2;
        var pts = new List<(double X, double Y)>(nSegments);
        for (int i = 0; i < nSegments; i++)
        {
            double angle = 2 * Math.PI * i / nSegments;
            pts.Add((r * Math.Cos(angle), r * Math.Sin(angle)));
        }
        return pts;
    }

    static (double X, double Y) PolygonCentroid(List<(double X, double Y)> pts)
    {
        int n = pts.Count;
        double area = 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            area += pts[i].X * pts[j].Y - pts[j].X * pts[i].Y;
        }
        area /= 2;
        if (Math.Abs(area) < 1e-15)
            return (pts.Average(p => p.X), pts.Average(p => p.Y));
        double cx = 0, cy = 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            double cross = pts[i].X * pts[j].Y - pts[j].X * pts[i].Y;
            cx += (pts[i].X + pts[j].X) * cross;
            cy += (pts[i].Y + pts[j].Y) * cross;
        }
        return (cx / (6 * area), cy / (6 * area));
    }
}

static class ProfileHelpers
{
    public static List<(double X, double Y)> RemoveConsecutiveDuplicates(List<(double X, double Y)> pts, double tol = 1e-12)
    {
        if (pts.Count == 0) return pts;
        var result = new List<(double X, double Y)> { pts[0] };
        for (int i = 1; i < pts.Count; i++)
        {
            if (Math.Abs(pts[i].X - result[^1].X) > tol || Math.Abs(pts[i].Y - result[^1].Y) > tol)
                result.Add(pts[i]);
        }
        return result;
    }

    public static List<(double X, double Y)> ArcPoints(double cx, double cy, double r, double aStartDeg, double aEndDeg, int n)
    {
        if (r <= 0 || n <= 0) return [];
        double span = aEndDeg - aStartDeg;
        var result = new List<(double X, double Y)>(n);
        for (int i = 0; i < n; i++)
        {
            double a = (aStartDeg + span * i / n) * Math.PI / 180.0;
            result.Add((cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
        }
        return result;
    }
}

public class IBeamProfile
{
    public string Name { get; }
    public double H { get; } public double B { get; } public double tw { get; } public double tf { get; }
    public double R1 { get; } public double r2 { get; } public double A { get; }
    public const double DefaultSlope = 0.07;

    public IBeamProfile(string name, double h, double b, double tw, double tf, double r1, double r2, double a)
    {
        Name = name; H = h; B = b; this.tw = tw; this.tf = tf; R1 = r1; this.r2 = r2; A = a;
    }

    public List<(double X, double Y)> ToPolygonPoints(int nArc = 6, double? slope = null)
    {
        double hh = H / 2, hw = B / 2, htw = tw / 2;
        var pts = new List<(double X, double Y)>();

        double useSlope = slope ?? (r2 > 0 ? DefaultSlope : 0.0);
        double halfSpan = (B - tw) / 2;
        double tfW = tf + useSlope * halfSpan / 2;
        double tfE = tf - useSlope * halfSpan / 2;

        pts.Add((-hw, -hh));
        pts.Add((hw, -hh));

        if (r2 > 0 && r2 < tfE)
        {
            pts.Add((hw, -hh + tfE - r2));
            pts.AddRange(ProfileHelpers.ArcPoints(hw - r2, -hh + tfE - r2, r2, 0, 90, nArc));
        }
        else pts.Add((hw, -hh + tfE));

        if (R1 > 0)
        {
            if (r2 > 0 && r2 < tfE) pts.Add((hw - r2, -hh + tfE));
            pts.AddRange(ProfileHelpers.ArcPoints(htw + R1, -hh + tfW + R1, R1, 270, 180, nArc));
            pts.Add((htw, -hh + tfW + R1));
        }
        else pts.Add((htw, -hh + tfW));

        if (R1 > 0)
        {
            pts.AddRange(ProfileHelpers.ArcPoints(htw + R1, hh - tfW - R1, R1, 180, 90, nArc));
            pts.Add((htw + R1, hh - tfW));
        }
        else pts.Add((htw, hh - tfW));

        if (r2 > 0 && r2 < tfE)
        {
            pts.Add((hw - r2, hh - tfE));
            pts.AddRange(ProfileHelpers.ArcPoints(hw - r2, hh - tfE + r2, r2, 270, 360, nArc));
        }
        else pts.Add((hw, hh - tfE));

        pts.Add((hw, hh));
        pts.Add((-hw, hh));

        if (r2 > 0 && r2 < tfE)
        {
            pts.Add((-hw, hh - tfE + r2));
            pts.AddRange(ProfileHelpers.ArcPoints(-hw + r2, hh - tfE + r2, r2, 180, 270, nArc));
        }
        else pts.Add((-hw, hh - tfE));

        if (R1 > 0)
        {
            if (r2 > 0 && r2 < tfE) pts.Add((-hw + r2, hh - tfE));
            pts.AddRange(ProfileHelpers.ArcPoints(-htw - R1, hh - tfW - R1, R1, 90, 0, nArc));
            pts.Add((-htw, hh - tfW - R1));
        }
        else pts.Add((-htw, hh - tfW));

        if (R1 > 0)
        {
            pts.AddRange(ProfileHelpers.ArcPoints(-htw - R1, -hh + tfW + R1, R1, 0, -90, nArc));
            pts.Add((-htw - R1, -hh + tfW));
        }
        else pts.Add((-htw, -hh + tfW));

        if (r2 > 0 && r2 < tfE)
        {
            pts.Add((-hw + r2, -hh + tfE));
            pts.AddRange(ProfileHelpers.ArcPoints(-hw + r2, -hh + tfE - r2, r2, 90, 180, nArc));
        }
        else pts.Add((-hw, -hh + tfE));

        if (r2 > 0 && r2 < tfE) pts.Add((-hw, -hh + tfE - r2));

        return ProfileHelpers.RemoveConsecutiveDuplicates(pts);
    }
}

public class ChannelProfile
{
    public string Name { get; }
    public double H { get; } public double B { get; } public double tw { get; } public double tf { get; }
    public double R1 { get; } public double r2 { get; } public double A { get; }
    public double X0 { get; }
    public bool Bent { get; }
    public double Slope { get; }

    public ChannelProfile(string name, double h, double b, double tw, double tf, double r1, double r2, double a, double x0, bool bent = false, double slope = 0.0)
    {
        Name = name; H = h; B = b; this.tw = tw; this.tf = tf; R1 = r1; this.r2 = r2; A = a; X0 = x0; Bent = bent; Slope = slope;
    }

    public List<(double X, double Y)> ToPolygonPoints(int nArc = 6, double? slope = null)
    {
        double useSlope = slope ?? Slope;
        bool hasSlope = useSlope > 0;
        double rExt = R1 + tw;
        var pts = new List<(double X, double Y)>();

        if (Bent)
        {
            pts.AddRange(ProfileHelpers.ArcPoints(rExt, rExt, rExt, 180, 270, nArc));
            pts.Add((rExt, 0.0));
            pts.Add((B, 0.0));
        }
        else
        {
            pts.Add((0.0, 0.0));
            pts.Add((B, 0.0));
        }

        if (hasSlope)
        {
            double halfSpan = (B - tw) / 2;
            double tfW = tf + useSlope * halfSpan / 2;
            double tfE = tf - useSlope * halfSpan / 2;

            if (r2 > 0 && r2 < tfE)
            {
                pts.Add((B, tfE - r2));
                pts.AddRange(ProfileHelpers.ArcPoints(B - r2, tfE - r2, r2, 0, 90, nArc));
                pts.Add((B - r2, tfE));
            }
            else pts.Add((B, tfE));
            pts.Add((tw + R1, tfW));

            pts.AddRange(ProfileHelpers.ArcPoints(tw + R1, tfW + R1, R1, 270, 180, nArc));
            pts.Add((tw, tfW + R1));
            pts.Add((tw, H - tfW - R1));
            pts.AddRange(ProfileHelpers.ArcPoints(tw + R1, H - tfW - R1, R1, 180, 90, nArc));
            pts.Add((tw + R1, H - tfW));
            pts.Add((B - r2, H - tfE));

            if (r2 > 0 && r2 < tfE)
            {
                pts.AddRange(ProfileHelpers.ArcPoints(B - r2, H - tfE + r2, r2, 270, 360, nArc));
                pts.Add((B, H - tfE + r2));
            }
            pts.Add((B, H));
        }
        else
        {
            if (r2 > 0 && r2 < tf && !Bent)
            {
                pts.Add((B, tf - r2));
                pts.AddRange(ProfileHelpers.ArcPoints(B - r2, tf - r2, r2, 0, 90, nArc));
                pts.Add((B - r2, tf));
            }
            else pts.Add((B, tf));
            pts.Add((tw + R1, tf));

            pts.AddRange(ProfileHelpers.ArcPoints(tw + R1, tf + R1, R1, 270, 180, nArc));
            pts.Add((tw, tf + R1));
            pts.Add((tw, H - tf - R1));
            pts.AddRange(ProfileHelpers.ArcPoints(tw + R1, H - tf - R1, R1, 180, 90, nArc));
            pts.Add((tw + R1, H - tf));

            if (r2 > 0 && r2 < tf && !Bent)
            {
                pts.Add((B - r2, H - tf));
                pts.AddRange(ProfileHelpers.ArcPoints(B - r2, H - tf + r2, r2, 270, 360, nArc));
                pts.Add((B, H - tf + r2));
            }
            else pts.Add((B, H - tf));
            pts.Add((B, H));
        }

        if (Bent)
        {
            pts.AddRange(ProfileHelpers.ArcPoints(rExt, H - rExt, rExt, 90, 180, nArc));
            pts.Add((0.0, H - rExt));
        }
        else pts.Add((0.0, H));

        pts.Add(Bent ? (0.0, rExt) : (0.0, 0.0));

        pts = ProfileHelpers.RemoveConsecutiveDuplicates(pts);
        return pts.Select(p => (p.X - X0, p.Y - H / 2)).ToList();
    }
}

public class AngleProfile
{
    public string Name { get; }
    public double H { get; } public double Bf { get; } public double Tw { get; } public double Tf { get; }
    public double R { get; } public double r_ { get; } public double A { get; }
    public double Xo { get; } public double Yo { get; }
    public bool Bent { get; }

    public AngleProfile(string name, double h, double bf, double tw, double tf, double r, double r_, double a, double xo, double yo, bool bent = false)
    {
        Name = name; H = h; Bf = bf; Tw = tw; Tf = tf; R = r; this.r_ = r_; A = a; Xo = xo; Yo = yo; Bent = bent;
    }

    public List<(double X, double Y)> ToPolygonPoints(int nArc = 6)
    {
        var pts = new List<(double X, double Y)>();

        if (Bent)
        {
            double rExt = R + Tw;
            pts.AddRange(ProfileHelpers.ArcPoints(rExt, rExt, rExt, 180, 270, nArc));
            pts.Add((rExt, 0.0));
            pts.Add((Bf, 0.0));
            pts.Add((Bf, Tf));
            pts.Add((rExt, Tf));
            pts.AddRange(ProfileHelpers.ArcPoints(rExt, rExt, R, 270, 180, nArc));
            pts.Add((rExt - R, rExt));
            pts.Add((Tw, H));
            pts.Add((0.0, H));
            pts.Add((0.0, rExt));
        }
        else
        {
            pts.Add((0.0, 0.0));
            pts.Add((Bf, 0.0));
            if (r_ > 0)
            {
                pts.AddRange(ProfileHelpers.ArcPoints(Bf - r_, Tf - r_, r_, 0, 90, nArc));
                pts.Add((Bf - r_, Tf));
            }
            else pts.Add((Bf, Tf));
            if (R > 0)
            {
                pts.AddRange(ProfileHelpers.ArcPoints(Tw + R, Tf + R, R, 270, 180, nArc));
                pts.Add((Tw, Tf + R));
            }
            pts.Add((Tw, H - r_));
            if (r_ > 0)
            {
                pts.AddRange(ProfileHelpers.ArcPoints(Tw - r_, H - r_, r_, 0, 90, nArc));
                pts.Add((Tw - r_, H));
            }
            pts.Add((0.0, H));
        }

        pts = ProfileHelpers.RemoveConsecutiveDuplicates(pts);
        return pts.Select(p => (p.X - Xo, p.Y - Yo)).ToList();
    }
}

public class RectTubeProfile
{
    public string Name { get; }
    public double H { get; } public double B { get; } public double tw { get; } public double tf { get; }
    public double r { get; } public double A { get; }

    public RectTubeProfile(string name, double h, double b, double tw, double tf, double r, double a)
    {
        Name = name; H = h; B = b; this.tw = tw; this.tf = tf; this.r = r; A = a;
    }

    public List<(double X, double Y)> OuterPoints(int nArc = 6)
    {
        double rExt = r + Math.Min(tw, tf);
        double hh = H / 2, hw = B / 2;
        var pts = new List<(double X, double Y)>();
        if (rExt > 0)
        {
            pts.Add((hw - rExt, -hh));
            pts.AddRange(ProfileHelpers.ArcPoints(hw - rExt, -hh + rExt, rExt, 270, 360, nArc));
            pts.Add((hw, -hh + rExt));
            pts.Add((hw, hh - rExt));
            pts.AddRange(ProfileHelpers.ArcPoints(hw - rExt, hh - rExt, rExt, 0, 90, nArc));
            pts.Add((hw - rExt, hh));
            pts.Add((-hw + rExt, hh));
            pts.AddRange(ProfileHelpers.ArcPoints(-hw + rExt, hh - rExt, rExt, 90, 180, nArc));
            pts.Add((-hw, hh - rExt));
            pts.Add((-hw, -hh + rExt));
            pts.AddRange(ProfileHelpers.ArcPoints(-hw + rExt, -hh + rExt, rExt, 180, 270, nArc));
            pts.Add((-hw + rExt, -hh));
        }
        else
        {
            pts.Add((hw, hh)); pts.Add((-hw, hh)); pts.Add((-hw, -hh)); pts.Add((hw, -hh));
        }
        return ProfileHelpers.RemoveConsecutiveDuplicates(pts);
    }

    public List<(double X, double Y)> HolePoints(int nArc = 6)
    {
        double hh = (H - 2 * tf) / 2, hw = (B - 2 * tw) / 2;
        var pts = new List<(double X, double Y)>();
        if (r > 0)
        {
            pts.Add((hw - r, hh));
            pts.AddRange(ProfileHelpers.ArcPoints(hw - r, hh - r, r, 90, 0, nArc));
            pts.Add((hw, hh - r));
            pts.Add((hw, -hh + r));
            pts.AddRange(ProfileHelpers.ArcPoints(hw - r, -hh + r, r, 0, -90, nArc));
            pts.Add((hw - r, -hh));
            pts.Add((-hw + r, -hh));
            pts.AddRange(ProfileHelpers.ArcPoints(-hw + r, -hh + r, r, -90, -180, nArc));
            pts.Add((-hw, -hh + r));
            pts.Add((-hw, hh - r));
            pts.AddRange(ProfileHelpers.ArcPoints(-hw + r, hh - r, r, -180, -270, nArc));
            pts.Add((-hw + r, hh));
        }
        else
        {
            pts.Add((hw, hh)); pts.Add((hw, -hh)); pts.Add((-hw, -hh)); pts.Add((-hw, hh));
        }
        return ProfileHelpers.RemoveConsecutiveDuplicates(pts);
    }
}

public class RoundTubeProfile
{
    public string Name { get; }
    public double D { get; } public double t { get; } public double A { get; }

    public RoundTubeProfile(string name, double d, double t, double a)
    {
        Name = name; D = d; this.t = t; A = a;
    }

    public List<(double X, double Y)> OuterPoints(int nArc = 6)
    {
        double r = D / 2;
        return ProfileHelpers.ArcPoints(0, 0, r, 0, 360, nArc * 4);
    }

    public List<(double X, double Y)> HolePoints(int nArc = 6)
    {
        double r = D / 2 - t;
        return ProfileHelpers.ArcPoints(0, 0, r, 360, 0, nArc * 4);
    }
}
