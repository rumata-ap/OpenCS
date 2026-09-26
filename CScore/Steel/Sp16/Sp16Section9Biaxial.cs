namespace CScore.Sp16;

public static partial class Sp16Section9
{
    // ── 9.2.9, 9.2.10 Сжатие с изгибом в двух главных плоскостях ────────

    /// <summary>
    /// Устойчивость при сжатии с изгибом в двух главных плоскостях: коробчатое сечение — 9.2.10, (120), (121)
    /// либо (121а); двутавры, тавры, швеллеры — 9.2.9, (116), (117) и дополнительные проверки по (109), (111)
    /// при ey = 0. Эпюра моментов (9.2.3, 9.2.6) из параметров относится к Mx; My принимается заданным.
    /// </summary>
    public static List<Sp16CheckResult> BiaxialStability(Sp16Member m, SteelForces f)
    {
        var res = new List<Sp16CheckResult>();
        if (f.N >= 0 || Math.Abs(f.Mx) <= Eps || Math.Abs(f.My) <= Eps) return res;
        var s = m.S;
        if (s.Kind == SteelProfileKind.Box) return Box9210(m, f);
        const string title = "Устойчивость при сжатии с изгибом в двух главных плоскостях";
        string? reason = s.Kind switch
        {
            SteelProfileKind.IBeam or SteelProfileKind.Tee or SteelProfileKind.Channel => null,
            SteelProfileKind.Angle => "одиночный уголок: оси сечения не главные — 9.2.9 не распространяется",
            SteelProfileKind.Generic => "профиль не распознан — задайте профиль вручную",
            _ => "сечение не приведено в табл. 21 — коэффициент c по 9.2.5 не определён, 9.2.9 не распространяется",
        };
        if (reason != null) { res.Add(Sp16CheckResult.NotApplicableFor("9.2.9", "(116)", title, reason)); return res; }
        return Formula116(m, f, title);
    }

    /// <summary>Параметры для φ по оси y при двух моментах: эпюра и η из параметров относятся к Mx.</summary>
    static Sp16Member ForMy(Sp16Member m) =>
        m.P.MomentShape == MomentShape.AsGiven && m.P.EtaOverride == null
            ? m : m.WithParams(m.P with { MomentShape = MomentShape.AsGiven, EtaOverride = null });

    static string? MyShapeNote(Sp16Member m) => m.P.MomentShape != MomentShape.AsGiven || m.P.EtaOverride != null
        ? "вид эпюры моментов и η из параметров отнесены к Mx; для My принят заданный момент, η — по табл. Д.2" : null;

    static Sp16CheckResult Fail(string clause, string formula, string desc, string note, IEnumerable<(string Name, double Value)>? vars = null) => new()
    {
        Clause = clause, Formula = formula, Description = desc, Utilization = double.PositiveInfinity, Status = CheckStatus.Fail,
        Variables = (vars ?? []).Select(v => new KeyValuePair<string, double>(v.Name, v.Value)).ToList(),
        Notes = [note],
    };

    /// <summary>9.2.9, (116), (117) и дополнительные проверки по (109), (111) при ey = 0.</summary>
    static List<Sp16CheckResult> Formula116(Sp16Member m, SteelForces f, string title)
    {
        var res = new List<Sp16CheckResult>();
        var s = m.S;
        double nAbs = -f.N;
        bool type3 = s.Kind == SteelProfileKind.Channel;
        bool coincide = type3 || s.Ix > s.Iy;                 // плоскость наибольшей жёсткости = плоскость симметрии (Mx)
        var c = CoefficientC(m, nAbs, f.Mx, coincide ? 1.0 : 1.25);
        if (c.NotApplicable != null) { res.Add(Sp16CheckResult.NotApplicableFor("9.2.9", "(116)", title, "c по 9.2.5: " + c.NotApplicable)); return res; }
        if (c.Failure != null) { res.Add(Fail("9.2.9", "(116)", title, "c по 9.2.5: " + c.Failure, c.Vars)); return res; }

        var pe = PhiE(ForMy(m), nAbs, f.My, false, unequalFlangesType8: true);
        if (pe.NotApplicable != null) { res.Add(Sp16CheckResult.NotApplicableFor("9.2.9", "(116)", title, "φey: " + pe.NotApplicable)); return res; }
        if (pe.Failure != null) { res.Add(Fail("9.2.9", "(116)", title, "φey: " + pe.Failure, pe.Vars)); return res; }

        double k = 0.6 * Math.Cbrt(c.C) + 0.4 * Math.Pow(c.C, 0.25);
        double phiExy = pe.PhiE * k;
        double cap = phiExy * s.A * s.Mat.Ry * m.P.GammaC;
        var notes = new List<string?> { "c — по 9.2.5 для момента Mx; φey — по 9.2.2 с my, λ̄y" };
        notes.AddRange(c.Notes);
        notes.AddRange(pe.Notes.Select(n => n == null ? null : "φey: " + n));
        notes.Add(MyShapeNote(m));
        res.Add(Sp16CheckResult.Of("9.2.9", "(116)", title, nAbs / cap,
            [("N", f.N), ("Mx", f.Mx), ("My", f.My), ("mx", c.Mx), ("c", c.C), ("λ̄y", m.LambdaBar(false)), ("my", pe.M), ("ηy", pe.Eta),
             ("mef,y", pe.Mef), ("φey", pe.PhiE), ("φexy", phiExy), ("A", s.A), ("Ry", s.Mat.Ry), ("γc", m.P.GammaC)],
            nAbs, cap, notes));

        // Дополнительные проверки при ey = 0.
        bool byMef = pe.Mef < c.Mx, byLambda = m.Lambda(true) > m.Lambda(false);
        if (!byMef && !byLambda) return res;
        var why = new List<string>();
        if (byMef) why.Add($"mef,y = {pe.Mef:0.###} < mx = {c.Mx:0.###}");
        if (byLambda) why.Add($"λ{m.Axis(true)} = {m.Lambda(true):0.#} > λ{m.Axis(false)} = {m.Lambda(false):0.#}");
        string note = $"9.2.9: дополнительная проверка при ey = 0 ({string.Join("; ", why)})";
        var fx = f with { My = 0 };
        var extra = InPlaneStability(m, fx);
        if (byMef) extra.AddRange(OutOfPlaneStability(m, fx));
        foreach (var r in extra) r.Notes.Insert(0, note);
        res.AddRange(extra);
        return res;
    }

    /// <summary>δ по (122): 1 − 0,1·N·λ̄²/(A·Ry), N — со знаком «+»; 1,0 при λ̄ ≤ 1.</summary>
    static double Delta122(Sp16Member m, double nAbs, bool aboutX)
    {
        double lb = m.LambdaBar(aboutX);
        return lb <= 1 ? 1.0 : 1 - 0.1 * nAbs * lb * lb / (m.S.A * m.S.Mat.Ry);
    }

    /// <summary>9.2.10: коробчатое сечение при изгибе в двух плоскостях — (120) и (121) либо (121а).</summary>
    static List<Sp16CheckResult> Box9210(Sp16Member m, SteelForces f)
    {
        var res = new List<Sp16CheckResult>();
        var s = m.S; var p = m.P;
        const string title = "Устойчивость коробчатого стержня при сжатии с изгибом в двух плоскостях";
        double nAbs = -f.N;
        var pex = PhiE(m, nAbs, f.Mx, true);
        var pey = PhiE(ForMy(m), nAbs, f.My, false);
        foreach (var (pe, name) in new[] { (pex, "φex"), (pey, "φey") })
        {
            if (pe.NotApplicable != null) { res.Add(Sp16CheckResult.NotApplicableFor("9.2.10", "(120)", title, $"{name}: {pe.NotApplicable}")); return res; }
            if (pe.Failure != null) { res.Add(Fail("9.2.10", "(120)", title, $"{name}: {pe.Failure}", pe.Vars)); return res; }
        }
        double rg = s.Mat.Ry * p.GammaC, a = s.A;
        var common = new List<string?> { MyShapeNote(m) };
        common.AddRange(pex.Notes.Select(n => n == null ? null : "φex: " + n));
        common.AddRange(pey.Notes.Select(n => n == null ? null : "φey: " + n));

        if (p.UseFormula121a && s.SymmetricAboutX && s.SymmetricAboutY)
        {
            double u = nAbs / (a * rg) * (1 / pex.PhiE + 1 / pey.PhiE - 1);
            res.Add(Sp16CheckResult.Of("9.2.10", "(121а)", title + " (одно условие для двухсимметричного сечения)", u,
                [("N", f.N), ("Mx", f.Mx), ("My", f.My), ("mef,x", pex.Mef), ("mef,y", pey.Mef), ("φex", pex.PhiE), ("φey", pey.PhiE),
                 ("A", a), ("Ry", s.Mat.Ry), ("γc", p.GammaC)], notes: common));
            return res;
        }

        var e1 = Sp16Tables.TableE1(s);
        if (e1 == null) { res.Add(Sp16CheckResult.NotApplicableFor("9.2.10", "(120)", title, "сечение не приведено в табл. Е.1")); return res; }
        foreach (bool aboutX in new[] { true, false })
        {
            double mom = aboutX ? f.Mx : f.My;
            double phiN = aboutX ? pey.PhiE : pex.PhiE;
            string ax = m.Axis(aboutX), other = m.Axis(!aboutX);
            res.Add(BoxInteraction(m, nAbs, mom, aboutX, phiN, $"φe{other}", aboutX ? "(120)" : "(121)",
                $"{title} (момент M{ax})", e1, [.. common]));
        }
        return res;
    }

    /// <summary>
    /// Условие вида (120)/(121): N/(φ·A·Ry·γc) + M/(c·δ·Wmin·Ry·γc) ≤ 1, c — по табл. Е.1 (с ограничением
    /// прим. 2), δ — по (122).
    /// </summary>
    static Sp16CheckResult BoxInteraction(Sp16Member m, double nAbs, double moment, bool aboutX, double phiN, string phiName,
        string formula, string desc, PlasticCoefficients e1, List<string?> notes)
    {
        var s = m.S; var p = m.P;
        double rg = s.Mat.Ry * p.GammaC;
        double c = Sp16Tables.CapPlastic(aboutX ? e1.Cx : e1.Cy, p.GammaFEq);
        double delta = Delta122(m, nAbs, aboutX), wMin = aboutX ? s.WxMin : s.WyMin;
        string ax = m.Axis(aboutX);
        var vars = new List<(string, double)> { ("N", -nAbs), ($"M{ax}", moment), (phiName, phiN), ($"λ̄{ax}", m.LambdaBar(aboutX)),
            ($"c{ax}", c), ($"δ{ax}", delta), ($"W{ax},min", wMin), ("A", s.A), ("Ry", s.Mat.Ry), ("γc", p.GammaC) };
        notes.Add($"c{ax} — по табл. Е.1 ({e1.Note})" + (c < (aboutX ? e1.Cx : e1.Cy) ? $", ограничен 1,15γf = {1.15 * p.GammaFEq:0.###}" : ""));
        notes.Add($"δ{ax} — по (122), N со знаком «+»" + (m.LambdaBar(aboutX) <= 1 ? $"; λ̄{ax} ≤ 1 — δ = 1" : ""));
        if (delta <= 0)
            return Fail("9.2.10", formula, desc, $"δ{ax} = {delta:0.###} ≤ 0 по (122) — сжимающая сила превышает допустимую для гибкости элемента", vars);
        double u = nAbs / (phiN * s.A * rg) + Math.Abs(moment) / (c * delta * wMin * rg);
        return Sp16CheckResult.Of("9.2.10", formula, desc, u, vars, notes: notes);
    }

    /// <summary>
    /// 9.2.10, последний абзац: коробчатое сечение при изгибе только в плоскости наибольшей жёсткости —
    /// (120) с φy вместо φey (при моменте My и Iy &gt; Ix — (121) с φx вместо φex, по аналогии).
    /// </summary>
    static Sp16CheckResult BoxSingleMoment120(Sp16Member m, double nAbs, double moment, bool aboutX, string desc)
    {
        var s = m.S;
        string formula = aboutX ? "(120)" : "(121)";
        if (m.Phi(!aboutX) is not { } phi)
            return Sp16CheckResult.NotApplicableFor("9.2.10", formula, desc, $"тип сечения по табл. 7 относительно оси {m.Axis(!aboutX)} не определён");
        var e1 = Sp16Tables.TableE1(s);
        if (e1 == null) return Sp16CheckResult.NotApplicableFor("9.2.10", formula, desc, "сечение не приведено в табл. Е.1");
        string other = m.Axis(!aboutX);
        return BoxInteraction(m, nAbs, moment, aboutX, phi, $"φ{other}", formula, desc, e1,
        [
            $"9.2.10: коробчатое сечение, изгиб в плоскости наибольшей жёсткости — вместо φe{other} принят φ{other}"
            + (aboutX ? "" : " (по аналогии: Iy > Ix)"),
        ]);
    }
}
