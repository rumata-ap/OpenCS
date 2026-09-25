namespace CScore.Sp16;

/// <summary>
/// Раздел 7 СП 16: центральное растяжение и сжатие элементов сплошного сечения —
/// прочность 7.1.1 (5), устойчивость 7.1.3 (7)/(7а), местная устойчивость стенок и полок
/// 7.3.2, 7.3.8, 7.3.9 (табл. 9, 10), уменьшенная площадь 7.3.5–7.3.6 (31)–(36).
/// Усилия — в канонических осях, кН; сопротивления — кПа.
/// </summary>
public static class Sp16Section7
{
    /// <summary>Коэффициент надёжности γu (4.3.2).</summary>
    public const double GammaU = 1.3;

    /// <summary>
    /// Расчётное сопротивление для формулы (5): Ry, либо Ru/γu для сталей с Ryn &gt; 440 Н/мм² и
    /// растянутых элементов, эксплуатация которых возможна после достижения предела текучести.
    /// </summary>
    public static double StrengthResistance(Sp16Member m, bool tension, out string? note)
    {
        note = null;
        var mat = m.S.Mat;
        bool high = mat.RynOrRy > 440000;
        if (high || (tension && m.P.TensionYieldAllowed))
        {
            note = high ? "Ryn > 440 Н/мм²: в формуле (5) Ry заменено на Ru/γu" : "эксплуатация после достижения предела текучести: Ry заменено на Ru/γu";
            return mat.Ru / GammaU;
        }
        if (mat.Ryn == null && mat.Ry > 400000)
            note = "нормативное сопротивление Ryn не задано — условие Ryn ≤ 440 Н/мм² проверено по Ry";
        return mat.Ry;
    }

    /// <summary>7.1.1, формула (5): N/(An·Ry·γc) ≤ 1.</summary>
    public static Sp16CheckResult Strength(Sp16Member m, SteelForces f)
    {
        bool tension = f.N > 0;
        double r = StrengthResistance(m, tension, out var note);
        double an = m.S.A * m.P.NetAreaRatio;
        double u = Math.Abs(f.N) / (an * r * m.P.GammaC);
        return Sp16CheckResult.Of("7.1.1", "(5)", tension ? "Прочность при центральном растяжении" : "Прочность при центральном сжатии",
            u, [("N", f.N), ("An", an), ("R", r), ("γc", m.P.GammaC)],
            Math.Abs(f.N), an * r * m.P.GammaC, [note]);
    }

    /// <summary>
    /// 7.1.3, формула (7): N/(φ·A·Ry·γc) ≤ 1 относительно осей x и y (для уголка — относительно оси
    /// минимальной жёсткости); для прокатных двутавров относительно x — (7а) с γres при
    /// <see cref="SteelDesignParams.UseGammaRes"/>. При λ̄uw &lt; λ̄w ≤ 2λ̄uw вместо A принимается Ad (7.3.5).
    /// </summary>
    public static List<Sp16CheckResult> Stability(Sp16Member m, SteelForces f)
    {
        var res = new List<Sp16CheckResult>();
        double n = Math.Abs(f.N);
        var (aUsed, aNote) = EffectiveArea(m, n);
        if (m.IsAngle)
        {
            var curve = m.Curve(true) ?? SectionCurve.b;
            double lb = m.LambdaBarMinAxis;
            double phi = Sp16Stability.Phi(lb, curve);
            res.Add(Sp16CheckResult.Of("7.1.3", "(7)", "Устойчивость при центральном сжатии (ось минимальной жёсткости уголка)",
                n / (phi * aUsed * m.S.Mat.Ry * m.P.GammaC),
                [("N", f.N), ("lef", Math.Max(m.P.LefX, m.P.LefY)), ("imin", m.S.iMin), ("λ", m.LambdaMinAxis), ("λ̄", lb), ("φ", phi), ("A", aUsed), ("Ry", m.S.Mat.Ry), ("γc", m.P.GammaC)],
                n, phi * aUsed * m.S.Mat.Ry * m.P.GammaC,
                [$"тип сечения {curve} (табл. 7); lef принята как большая из lef,x и lef,y; радиус инерции — относительно оси минимальной жёсткости (7.1.4, 10.1.4)", aNote]));
            return res;
        }
        foreach (bool aboutX in new[] { true, false })
        {
            string axis = m.Axis(aboutX);
            var curve = m.Curve(aboutX);
            if (curve == null)
            {
                res.Add(Sp16CheckResult.NotApplicableFor("7.1.3", "(7)", $"Устойчивость при центральном сжатии (ось {axis})",
                    "Сечение не приведено в табл. 7 — задайте тип сечения (a, b, c) в параметрах"));
                continue;
            }
            double lambda = m.Lambda(aboutX), lb = m.LambdaBar(aboutX);
            double phi = Sp16Stability.Phi(lb, curve.Value);
            double gammaRes = 1;
            string formula = "(7)";
            string? resNote = null;
            if (aboutX && m.P.UseGammaRes && m.S.Kind == SteelProfileKind.IBeam && m.S.Profile.Fabrication == SteelFabrication.Rolled)
            {
                gammaRes = Sp16Stability.GammaRes(lb, m.S.Mat.Ry, out resNote);
                formula = "(7а)";
            }
            double cap = gammaRes * phi * aUsed * m.S.Mat.Ry * m.P.GammaC;
            var vars = new List<(string, double)> { ("N", f.N), ("lef", aboutX ? m.P.LefX : m.P.LefY), ("i", aboutX ? m.S.ix : m.S.iy), ("λ", lambda), ("λ̄", lb), ("φ", phi) };
            if (formula == "(7а)") vars.Add(("γres", gammaRes));
            vars.AddRange([("A", aUsed), ("Ry", m.S.Mat.Ry), ("γc", m.P.GammaC)]);
            res.Add(Sp16CheckResult.Of("7.1.3", formula, $"Устойчивость при центральном сжатии (ось {axis})", n / cap, vars, n, cap,
                [$"тип сечения {curve} (табл. 7)", resNote, aNote]));
        }
        return res;
    }

    /// <summary>
    /// Площадь для формулы (7): A, либо Ad по 7.3.5–7.3.6 при λ̄uw &lt; λ̄w ≤ 2λ̄uw.
    /// </summary>
    public static (double Area, string? Note) EffectiveArea(Sp16Member m, double nAbs)
    {
        var s = m.S;
        if (s.Kind is not (SteelProfileKind.IBeam or SteelProfileKind.Box or SteelProfileKind.Channel)) return (s.A, null);
        var gov = m.GoverningPhi();
        if (gov == null) return (s.A, null);
        double lb = gov.Value.LambdaBar, sq = Math.Sqrt(s.Mat.Ry / s.Mat.E), sqInv = 1 / sq;
        double lbw = s.Hef / s.Tw * sq;
        double? luw0 = Sp16Tables.WebLimitCentral(s, s.Kind == SteelProfileKind.Box ? m.LambdaBar(true) : lb, out _);
        if (luw0 == null) return (s.A, null);
        double luw = luw0.Value * Increase7311(m, gov.Value.Phi, nAbs);
        double ad = s.A;
        bool reduced = false;
        if (lbw > luw && lbw <= 2 * luw)
        {
            double hd = s.Kind switch
            {
                SteelProfileKind.IBeam => s.Tw * (luw - (lbw / luw - 1) * (luw - 1.2 - 0.15 * Math.Min(lb, 3.5))) * sqInv,
                SteelProfileKind.Box => s.Tw * (luw - (lbw / luw - 1) * (luw - 2.9 - 0.2 * Math.Min(m.LambdaBar(true), 2.3) + 0.7 * lbw)) * sqInv,
                _ => s.Tw * luw * sqInv,
            };
            hd = Math.Min(hd, s.Hef);
            ad -= (s.Hef - hd) * s.Tw * s.WebCount;
            reduced = true;
        }
        if (s.Kind == SteelProfileKind.Box)
        {
            // Поясные листы короба: вместо hd, tw, λ̄uw, λ̄w — bd, tf, λ̄uf,1, λ̄f,1 (7.3.6); λ̄uf,1 — по табл. 9 (7.3.9).
            double lbf = s.BefBoxFlange / s.Profile.Tf1 * sq;
            double lby = m.LambdaBar(false);
            double luf = (Sp16Tables.WebLimitCentral(s, lby, out _) ?? 0) * Increase7311(m, gov.Value.Phi, nAbs);
            if (lbf > luf && lbf <= 2 * luf)
            {
                double bd = s.Profile.Tf1 * (luf - (lbf / luf - 1) * (luf - 2.9 - 0.2 * Math.Min(lby, 2.3) + 0.7 * lbf)) * sqInv;
                bd = Math.Min(bd, s.BefBoxFlange);
                ad -= 2 * (s.BefBoxFlange - bd) * s.Profile.Tf1;
                reduced = true;
            }
        }
        return reduced ? (ad, $"λ̄w > λ̄uw: в формуле (7) принята уменьшенная площадь Ad = {ad * 1e4:0.##} см² (7.3.5, 7.3.6)") : (s.A, null);
    }

    /// <summary>Множитель 7.3.11: √(φARy/N) ≤ 1,25, если определяющей является проверка по предельной гибкости.</summary>
    static double Increase7311(Sp16Member m, double phi, double nAbs)
    {
        if (!m.P.SlendernessGovernsSection || nAbs <= 0) return 1;
        return Math.Clamp(Math.Sqrt(phi * m.S.A * m.S.Mat.Ry / nAbs), 1, 1.25);
    }

    /// <summary>Местная устойчивость стенок (7.3.2, табл. 9) и полок (7.3.8, табл. 10; 7.3.9 — пояса короба).</summary>
    public static List<Sp16CheckResult> LocalStability(Sp16Member m, SteelForces f)
    {
        var res = new List<Sp16CheckResult>();
        var s = m.S;
        var gov = m.GoverningPhi();
        if (gov == null)
        {
            res.Add(Sp16CheckResult.NotApplicableFor("7.3.2", "табл. 9", "Местная устойчивость стенки", "Тип сечения не определён"));
            return res;
        }
        double nAbs = Math.Abs(f.N);
        double k = Increase7311(m, gov.Value.Phi, nAbs);
        double sq = Math.Sqrt(s.Mat.Ry / s.Mat.E);
        string incNote = k > 1 ? $"пределы увеличены по 7.3.11 в {k:0.###} раза" : "";

        if (s.Kind is SteelProfileKind.IBeam or SteelProfileKind.Box or SteelProfileKind.Channel or SteelProfileKind.Tee)
        {
            double lbForWeb = s.Kind == SteelProfileKind.Box ? m.LambdaBar(true) : gov.Value.LambdaBar;
            double lbw = s.Hef / s.Tw * sq;
            double luw = Sp16Tables.WebLimitCentral(s, lbForWeb, out var tNote)!.Value * k;
            var r = Sp16CheckResult.Limit("7.3.2", "табл. 9", "Местная устойчивость стенки при центральном сжатии", lbw, luw,
                [("hef", s.Hef), ("tw", s.Tw), ("λ̄w", lbw), ("λ̄", lbForWeb), ("λ̄uw", luw)], [tNote, incNote]);
            if (r.Status == CheckStatus.Fail && lbw <= 2 * luw && s.Kind != SteelProfileKind.Tee)
                r = Sp16CheckResult.Of(r.Clause, r.Formula, r.Description, 1.0, r.Variables.Select(v => (v.Key, v.Value)), lbw, luw,
                    [.. r.Notes, "λ̄w > λ̄uw, но не более 2λ̄uw: устойчивость стенки учтена уменьшенной площадью Ad в формуле (7) (7.3.5)"]);
            res.Add(r);
        }
        if (s.Kind == SteelProfileKind.Box)
        {
            double lby = m.LambdaBar(false);
            double lbf = s.BefBoxFlange / s.Profile.Tf1 * sq;
            double luf = Sp16Tables.WebLimitCentral(s, lby, out _)!.Value * k;
            var r = Sp16CheckResult.Limit("7.3.9", "табл. 9", "Местная устойчивость поясного листа коробчатого сечения", lbf, luf,
                [("bef,1", s.BefBoxFlange), ("tf", s.Profile.Tf1), ("λ̄f,1", lbf), ("λ̄", lby), ("λ̄uf,1", luf)], [incNote]);
            if (r.Status == CheckStatus.Fail && lbf <= 2 * luf)
                r = Sp16CheckResult.Of(r.Clause, r.Formula, r.Description, 1.0, r.Variables.Select(v => (v.Key, v.Value)), lbf, luf,
                    [.. r.Notes, "λ̄f,1 > λ̄uf,1, но не более 2λ̄uf,1: учтено уменьшенной площадью Ad (7.3.6)"]);
            res.Add(r);
        }
        if (s.Kind is SteelProfileKind.IBeam or SteelProfileKind.Tee or SteelProfileKind.Channel or SteelProfileKind.Angle)
        {
            double luf = Sp16Tables.FlangeLimitCentral(s, gov.Value.LambdaBar)!.Value * k;
            var flanges = new List<(string Name, double Bef, double Tf, string Note)>();
            if (s.Kind == SteelProfileKind.Angle)
            {
                // Проверяется большая полка: bef — от начала закругления (гиба) до пера (7.3.7).
                bool equal = Math.Abs(s.Profile.H - s.Profile.Bf1) <= 1e-6;
                double leg = Math.Max(s.Profile.H, s.Profile.Bf1);
                double r = s.Profile.Fabrication == SteelFabrication.Welded ? 0 : s.Profile.R;
                flanges.Add((equal ? "полка уголка" : "большая полка уголка", leg - s.Profile.Tw - r, s.Profile.Tw,
                    equal ? "равнополочный уголок — формула (39)" : "неравнополочный уголок — формула (38)"));
            }
            else
            {
                if (s.TfTop > 0) flanges.Add(("верхний пояс", s.BefTop, s.TfTop, ""));
                bool bottomDiffers = s.Kind == SteelProfileKind.Tee || (s.Kind == SteelProfileKind.IBeam && !s.SymmetricAboutX);
                if (s.TfBottom > 0 && bottomDiffers) flanges.Add(("нижний пояс", s.BefBottom, s.TfBottom, ""));
            }
            foreach (var (name, bef, tf, fNote) in flanges)
            {
                double lbf = bef / tf * sq;
                res.Add(Sp16CheckResult.Limit("7.3.8", "табл. 10", $"Местная устойчивость свеса полки ({name})", lbf, luf,
                    [("bef", bef), ("tf", tf), ("λ̄f", lbf), ("λ̄", gov.Value.LambdaBar), ("λ̄uf", luf)], [fNote, incNote]));
            }
        }
        if (res.Count == 0)
            res.Add(Sp16CheckResult.NotApplicableFor("7.3", "табл. 9, 10", "Местная устойчивость",
                s.Kind is SteelProfileKind.Pipe ? "Для труб п. 7.3 не устанавливает проверку (стенка трубы — раздел 11)" : "Для сечения не требуется / не приведено в табл. 9, 10"));
        return res;
    }
}
