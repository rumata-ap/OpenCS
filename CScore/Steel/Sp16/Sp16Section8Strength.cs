namespace CScore.Sp16;

/// <summary>
/// 8.2 СП 16: прочность изгибаемых элементов сплошного сечения — 8.2.1 (41)–(44) для
/// 1-го класса, 8.2.2 (46)–(48) — местное напряжение, 8.2.3 (50)–(55) — 2-й/3-й класс с табл. Е.1.
/// Усилия — канонические оси, кН, кН·м.
/// </summary>
public static class Sp16Section8Strength
{
    /// <summary>Нормальное напряжение от N, Mx, My в точке (x, y) относительно центра тяжести (общий случай с Ixy), кПа.</summary>
    public static double Sigma(Sp16Section s, SteelForces f, double x, double y)
    {
        double ix = s.Ix, iy = s.Iy, ixy = s.Poly.Ixy, d = ix * iy - ixy * ixy;
        double b = (f.Mx * iy - f.My * ixy) / d, a = (f.My * ix - f.Mx * ixy) / d;
        return f.N / s.A + a * x + b * y;
    }

    /// <summary>Наибольшее по модулю нормальное напряжение в вершинах контура и точка, где оно достигается.</summary>
    public static (double Sigma, double X, double Y) MaxSigma(Sp16Section s, SteelForces f)
    {
        double best = 0, bx = 0, by = 0;
        foreach (var (px, py) in s.Poly.Outer)
        {
            double x = px - s.Poly.Xc, y = py - s.Poly.Yc;
            double sg = Sigma(s, f, x, y);
            if (Math.Abs(sg) > Math.Abs(best)) { best = sg; bx = x; by = y; }
        }
        return (best, bx, by);
    }

    /// <summary>Полный набор проверок прочности при изгибе (с учётом N — для растяжения/сжатия с изгибом см. раздел 9).</summary>
    public static List<Sp16CheckResult> Check(Sp16Member m, SteelForces f)
    {
        var res = new List<Sp16CheckResult>();
        bool mx = Math.Abs(f.Mx) > 1e-9, my = Math.Abs(f.My) > 1e-9;
        var s = m.S;
        var plasticReason = PlasticNotApplicableReason(m, f);
        if (m.P.AllowPlastic && plasticReason == null)
            res.AddRange(Plastic(m, f));
        else
        {
            if (m.P.AllowPlastic && plasticReason != null)
                res.Add(Sp16CheckResult.NotApplicableFor("8.2.3", "(50)", "Прочность с учётом пластических деформаций", plasticReason + " — выполнен расчёт по 8.2.1"));
            res.AddRange(Elastic(m, f, mx, my));
        }
        res.AddRange(Shear(m, f));
        var r44 = WebCombined(m, f);
        if (r44 != null) res.Add(r44);
        var r46 = LocalStress(m);
        if (r46 != null) res.Add(r46);
        return res;
    }

    /// <summary>Причина неприменимости 8.2.3; null — применимо.</summary>
    static string? PlasticNotApplicableReason(Sp16Member m, SteelForces f)
    {
        var s = m.S;
        if (s.Kind is not (SteelProfileKind.IBeam or SteelProfileKind.Box)) return "8.2.3 распространяется только на двутавровое и коробчатое сечения";
        if (m.P.DynamicLoad) return "балки 2-го и 3-го классов — только для статических нагрузок (8.1)";
        if (s.Mat.RynOrRy > 440000) return "8.2.3 — только для сталей с Ryn ≤ 440 Н/мм²";
        if (Sp16Tables.TableE1(s) == null) return "сечение не приведено в табл. Е.1";
        return null;
    }

    /// <summary>8.2.1: (41) — один момент, (43) без бимомента — два момента.</summary>
    static List<Sp16CheckResult> Elastic(Sp16Member m, SteelForces f, bool mx, bool my)
    {
        var res = new List<Sp16CheckResult>();
        var s = m.S;
        double rg = s.Mat.Ry * m.P.GammaC;
        if (mx && !my || my && !mx)
        {
            bool aboutX = mx;
            double mom = Math.Abs(aboutX ? f.Mx : f.My);
            double w = aboutX ? s.WxMin : s.WyMin;
            if (Math.Abs(s.Poly.Ixy) > 1e-6 * Math.Max(s.Ix, s.Iy))
            {
                var (sg, _, _) = MaxSigma(s, f with { N = 0 });
                res.Add(Sp16CheckResult.Of("8.2.1", "(43)", $"Прочность при изгибе (ось {m.Axis(aboutX)}), оси не главные",
                    Math.Abs(sg) / rg, [("M", mom), ("σmax", sg), ("Ry", s.Mat.Ry), ("γc", m.P.GammaC)], Math.Abs(sg), rg,
                    ["оси сечения не главные — напряжение вычислено с учётом Ixy"]));
            }
            else
                res.Add(Sp16CheckResult.Of("8.2.1", "(41)", $"Прочность при изгибе (ось {m.Axis(aboutX)})",
                    mom / (w * rg), [("M", mom), ("Wn,min", w), ("Ry", s.Mat.Ry), ("γc", m.P.GammaC)], mom, w * rg,
                    ["ослабление сечения отверстиями в Wn,min не учитывается"]));
        }
        else if (mx && my)
        {
            var (sg, x, y) = MaxSigma(s, f with { N = 0 });
            res.Add(Sp16CheckResult.Of("8.2.1", "(43)", "Прочность при изгибе в двух главных плоскостях",
                Math.Abs(sg) / rg, [("Mx", f.Mx), ("My", f.My), ("x", x), ("y", y), ("σ", sg), ("Ry", s.Mat.Ry), ("γc", m.P.GammaC)],
                Math.Abs(sg), rg, ["бимомент B не учитывается (вне объёма)"]));
        }
        return res;
    }

    /// <summary>8.2.3: (50), (51) с β по (52), табл. Е.1; (54), (55) — опорное сечение (M = 0).</summary>
    static List<Sp16CheckResult> Plastic(Sp16Member m, SteelForces f)
    {
        var res = new List<Sp16CheckResult>();
        var s = m.S;
        var e1 = Sp16Tables.TableE1(s)!;
        double rs = s.Mat.Rs, rg = s.Mat.Ry * m.P.GammaC;
        double tauX = Math.Abs(f.Qy) / s.Aw;                       // τx = Qx/Aw в обозначениях СП (сила в плоскости стенки)
        double af = Math.Min(s.AfTop, s.AfBottom);
        double tauY = af > 0 ? Math.Abs(f.Qx) / (2 * af) : 0;      // τy = Qy/(2Af)
        bool mx = Math.Abs(f.Mx) > 1e-9, my = Math.Abs(f.My) > 1e-9;
        if (!mx && !my)
        {
            if (Math.Abs(f.Qy) > 1e-9)
                res.Add(Sp16CheckResult.Of("8.2.3", "(54)", "Прочность опорного сечения на сдвиг (в плоскости стенки)",
                    Math.Abs(f.Qy) / (s.Aw * rs * m.P.GammaC), [("Q", f.Qy), ("Aw", s.Aw), ("Rs", rs), ("γc", m.P.GammaC)],
                    Math.Abs(f.Qy), s.Aw * rs * m.P.GammaC));
            if (Math.Abs(f.Qx) > 1e-9 && af > 0)
                res.Add(Sp16CheckResult.Of("8.2.3", "(55)", "Прочность опорного сечения на сдвиг (из плоскости стенки)",
                    Math.Abs(f.Qx) / (2 * af * rs * m.P.GammaC), [("Q", f.Qx), ("Af", af), ("Rs", rs), ("γc", m.P.GammaC)],
                    Math.Abs(f.Qx), 2 * af * rs * m.P.GammaC));
            return res;
        }
        if (tauX > 0.9 * rs)
        {
            res.Add(Sp16CheckResult.NotApplicableFor("8.2.3", "(50)", "Прочность с учётом пластических деформаций",
                $"τx = {tauX:0} кПа > 0,9Rs = {0.9 * rs:0} кПа — 8.2.3 неприменим, выполнен расчёт по 8.2.1"));
            res.AddRange(Elastic(m, f, mx, my));
            return res;
        }
        if (my && tauY > 0.5 * rs)
        {
            res.Add(Sp16CheckResult.NotApplicableFor("8.2.3", "(51)", "Прочность с учётом пластических деформаций (два момента)",
                $"τy = {tauY:0} кПа > 0,5Rs — формула (51) неприменима, выполнен расчёт по 8.2.1"));
            res.AddRange(Elastic(m, f, mx, my));
            return res;
        }
        double afRatio = af / s.Aw;
        double beta = m.P.PureBendingZone || tauX <= 0.5 * rs ? 1.0 : 1 - 0.20 / (afRatio + 0.25) * Math.Pow(tauX / rs, 4);
        double cx = e1.Cx, cy = e1.Cy;
        if (m.P.PureBendingZone) { cx = 0.5 * (1 + cx); cy = 0.5 * (1 + cy); }
        double cxEff = Sp16Tables.CapPlastic(cx, m.P.GammaFEq), cyEff = Sp16Tables.CapPlastic(cy, m.P.GammaFEq);
        var notes = new List<string?>
        {
            $"табл. Е.1: {e1.Note}",
            cxEff < cx || cyEff < cy ? $"прим. 2 табл. Е.1: c ≤ 1,15γf = {1.15 * m.P.GammaFEq:0.###}" : null,
            m.P.PureBendingZone ? "зона чистого изгиба: β = 1, cxm = 0,5(1 + cx)" : null,
            "требуется соблюдение 8.4.6, 8.5.8, 8.5.9 и 8.5.18",
        };
        double wx = s.WxMin, wy = s.WyMin;
        double termX = Math.Abs(f.Mx) / (cxEff * beta * wx * rg);
        if (!my)
            res.Add(Sp16CheckResult.Of("8.2.3", "(50)", "Прочность при изгибе с учётом пластических деформаций",
                termX, [("Mx", f.Mx), ("cx", cxEff), ("β", beta), ("τx", tauX), ("Rs", rs), ("Wn,min", wx), ("Ry", s.Mat.Ry), ("γc", m.P.GammaC)],
                notes: notes));
        else
        {
            double termY = Math.Abs(f.My) / (cyEff * wy * rg);
            res.Add(Sp16CheckResult.Of("8.2.3", "(51)", "Прочность при изгибе в двух плоскостях с учётом пластических деформаций",
                termX + termY, [("Mx", f.Mx), ("My", f.My), ("cx", cxEff), ("cy", cyEff), ("β", beta), ("τx", tauX), ("τy", tauY), ("Wxn,min", wx), ("Wyn,min", wy), ("Ry", s.Mat.Ry), ("γc", m.P.GammaC)],
                notes: notes));
        }
        return res;
    }

    /// <summary>8.2.1, формула (42): QS/(I·tw·Rs·γc) ≤ 1 для каждой поперечной силы.</summary>
    public static List<Sp16CheckResult> Shear(Sp16Member m, SteelForces f)
    {
        var res = new List<Sp16CheckResult>();
        var s = m.S;
        foreach (bool aboutX in new[] { true, false })
        {
            double q = aboutX ? f.Qy : f.Qx;
            if (Math.Abs(q) <= 1e-9) continue;
            var (sStat, t) = s.ShearAtCentroid(aboutX);
            double inertia = aboutX ? s.Ix : s.Iy;
            double tau = Math.Abs(q) * sStat / (inertia * t);
            res.Add(Sp16CheckResult.Of("8.2.1", "(42)", $"Прочность на сдвиг (сила вдоль оси {m.Axis(!aboutX)})",
                tau / (s.Mat.Rs * m.P.GammaC), [("Q", q), ("S", sStat), ("I", inertia), ("t", t), ("τ", tau), ("Rs", s.Mat.Rs), ("γc", m.P.GammaC)],
                tau, s.Mat.Rs * m.P.GammaC,
                [aboutX ? "для разрезных балок на опоре формулу (42) следует применять без учёта работы поясов" : null]));
        }
        return res;
    }

    /// <summary>
    /// 8.2.1, формула (44): 0,87/(Ry·γc)·√(σx² − σxσy + σy² + 3τxy²) ≤ 1 в стенке у пояса
    /// (двутавр, швеллер, тавр, короб) при совместном действии Mx и Qy; σy = −σloc при местной нагрузке.
    /// </summary>
    public static Sp16CheckResult? WebCombined(Sp16Member m, SteelForces f)
    {
        var s = m.S;
        if (s.Kind is not (SteelProfileKind.IBeam or SteelProfileKind.Channel or SteelProfileKind.Box or SteelProfileKind.Tee)) return null;
        if (Math.Abs(f.Mx) <= 1e-9 || Math.Abs(f.Qy) <= 1e-9) return null;
        double sigmaLoc = LocalSigma(m) ?? 0;
        double worst = 0, sxW = 0, tW = 0, yW = 0;
        foreach (bool top in new[] { true, false })
        {
            double tf = top ? s.TfTop : s.TfBottom;
            double yEdge = top ? s.YTop - tf : -(s.YBottom - tf);         // уровень границы стенки у пояса (от ц.т.)
            if (tf <= 0) continue;
            double sx = f.Mx * yEdge / s.Ix;
            double sStat = Math.Abs(s.Poly.PartAbove(s.Poly.Yc + yEdge, true).S);
            double tau = Math.Abs(f.Qy) * sStat / (s.Ix * s.Tw * s.WebCount);
            // Местное напряжение действует у нагруженного (верхнего) пояса.
            double sy = top ? -sigmaLoc : 0;
            double eq = Math.Sqrt(sx * sx - sx * sy + sy * sy + 3 * tau * tau);
            if (eq > worst) { worst = eq; sxW = sx; tW = tau; yW = yEdge; }
        }
        if (worst <= 0) return null;
        double rg = s.Mat.Ry * m.P.GammaC;
        return Sp16CheckResult.Of("8.2.1", "(44)", "Прочность стенки при совместном действии M и Q (у пояса)",
            0.87 * worst / rg, [("y", yW), ("σx", sxW), ("σy", -(sigmaLoc)), ("τxy", tW), ("Ry", s.Mat.Ry), ("γc", m.P.GammaC)],
            notes: [sigmaLoc > 0 ? "σy = σloc по (47) у нагруженного пояса" : null]);
    }

    /// <summary>σloc = F/(lef·tw) по (47), lef = b + 2h (48); null — нет местной нагрузки или сечение без стенки.</summary>
    public static double? LocalSigma(Sp16Member m)
    {
        var s = m.S;
        if (m.P.LocalForce <= 0 || s.Kind is not (SteelProfileKind.IBeam or SteelProfileKind.Channel or SteelProfileKind.Box)) return null;
        double h = s.Profile.Fabrication == SteelFabrication.Rolled ? s.TfTop + s.Profile.R : s.TfTop + m.P.FlangeWeldLeg;
        double lef = m.P.BearingLength + 2 * h;
        return lef > 0 ? m.P.LocalForce / (lef * s.Tw * s.WebCount) : null;
    }

    /// <summary>8.2.2, формула (46): σloc/(Ry·γc) ≤ 1.</summary>
    public static Sp16CheckResult? LocalStress(Sp16Member m)
    {
        var sl = LocalSigma(m);
        if (sl == null) return null;
        var s = m.S;
        double h = s.Profile.Fabrication == SteelFabrication.Rolled ? s.TfTop + s.Profile.R : s.TfTop + m.P.FlangeWeldLeg;
        return Sp16CheckResult.Of("8.2.2", "(46)", "Прочность стенки при местном напряжении σloc",
            sl.Value / (s.Mat.Ry * m.P.GammaC), [("F", m.P.LocalForce), ("b", m.P.BearingLength), ("h", h), ("lef", m.P.BearingLength + 2 * h), ("tw", s.Tw), ("σloc", sl.Value), ("Ry", s.Mat.Ry), ("γc", m.P.GammaC)],
            sl.Value, s.Mat.Ry * m.P.GammaC,
            ["lef = b + 2h (48); стенка не укреплена ребром в месте приложения нагрузки",
             s.Profile.Fabrication == SteelFabrication.Rolled ? "h — от наружной грани пояса до начала закругления" : "h = tf + kf (катет поясного шва из параметров)"]);
    }
}
