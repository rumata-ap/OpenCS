namespace CScore.Sp16;

/// <summary>
/// Коэффициент c по 9.2.5 (формулы (112)–(114), табл. 21, ограничение cmax по прил. Д) для (111) и (117).
/// Если <see cref="NotApplicable"/> или <see cref="Failure"/> не null — c не определён.
/// </summary>
public sealed class CoefficientCResult
{
    /// <summary>Коэффициент c (с учётом c ≥ 0,3 и c ≤ cmax).</summary>
    public double C { get; init; } = double.NaN;
    /// <summary>Относительный эксцентриситет mx = (Mx/N)(A/Wc).</summary>
    public double Mx { get; init; } = double.NaN;
    /// <summary>Коэффициент φy при центральном сжатии (7.1.3).</summary>
    public double PhiY { get; init; } = double.NaN;
    /// <summary>Формула, по которой определён c: «(112)», «(113)» или «(114)».</summary>
    public string Formula { get; init; } = "";
    /// <summary>Переменные расчёта.</summary>
    public List<(string Name, double Value)> Vars { get; init; } = [];
    /// <summary>Примечания.</summary>
    public List<string?> Notes { get; init; } = [];
    /// <summary>Причина, по которой 9.2.4 не распространяется на элемент.</summary>
    public string? NotApplicable { get; init; }
    /// <summary>Причина, по которой c не может быть определён.</summary>
    public string? Failure { get; init; }
}

/// <summary>Коэффициент cmax по (Д.1), (Д.2) и табл. Д.6 (исправленной); при <see cref="Reason"/> ≠ null — не определён.</summary>
public sealed record CMaxResult(double? CMax, int Type, List<(string Name, double Value)> Vars, string? Reason = null);

public static partial class Sp16Section9
{
    // ── 9.2.4–9.2.8 Устойчивость из плоскости действия момента ──────────

    /// <summary>
    /// Устойчивость из плоскости действия момента при сжатии с изгибом в одной главной плоскости:
    /// изгиб в плоскости наибольшей жёсткости — 9.2.4, формула (111) (двутавры, тавры с плоскостью
    /// симметрии в плоскости момента, швеллеры); изгиб в плоскости наименьшей жёсткости — 9.2.8, формула (115).
    /// </summary>
    public static List<Sp16CheckResult> OutOfPlaneStability(Sp16Member m, SteelForces f)
    {
        var res = new List<Sp16CheckResult>();
        bool mx = Math.Abs(f.Mx) > Eps, my = Math.Abs(f.My) > Eps;
        if (f.N >= 0 || !mx && !my) return res;
        const string title = "Устойчивость из плоскости действия момента";
        if (mx && my)
        {
            res.Add(Sp16CheckResult.NotApplicableFor("9.2.4", "(111)", title,
                "сжатие с изгибом в двух главных плоскостях — расчёт по 9.2.9 (коробчатое сечение — 9.2.10)"));
            return res;
        }
        bool aboutX = mx;
        var s = m.S;
        string desc = $"{title} (изгиб относительно оси {m.Axis(aboutX)})";
        double nAbs = -f.N, moment = aboutX ? f.Mx : f.My;
        if (m.IsAngle)
        {
            res.Add(Sp16CheckResult.NotApplicableFor("9.2.4", "(111)", desc,
                "одиночный уголок: оси сечения не главные — 9.2.4 и 9.2.8 не распространяются"));
            return res;
        }
        double iIn = aboutX ? s.Ix : s.Iy, iOut = aboutX ? s.Iy : s.Ix;
        if (iIn <= iOut * (1 + 1e-9))
        {
            res.Add(Formula115(m, nAbs, aboutX, desc, iIn >= iOut * (1 - 1e-9)));
            return res;
        }
        string? reason = s.Kind switch
        {
            SteelProfileKind.Box => "коробчатое сечение: 9.2.4 не распространяется (сжатие с изгибом коробчатых стержней — 9.2.10)",
            SteelProfileKind.Generic => "профиль не распознан — тип сечения по табл. 21 не определён; задайте профиль вручную",
            SteelProfileKind.IBeam or SteelProfileKind.Tee or SteelProfileKind.Channel when !aboutX =>
                "плоскость наибольшей жёсткости не совпадает с плоскостью симметрии сечения — 9.2.4 не распространяется",
            SteelProfileKind.IBeam or SteelProfileKind.Tee or SteelProfileKind.Channel => null,
            _ => "сечение не приведено в табл. 21 (9.2.4 — двутавры, тавры и швеллеры при изгибе в плоскости наибольшей жёсткости)",
        };
        if (reason != null) { res.Add(Sp16CheckResult.NotApplicableFor("9.2.4", "(111)", desc, reason)); return res; }

        var c = CoefficientC(m, nAbs, moment);
        if (c.NotApplicable != null) { res.Add(Sp16CheckResult.NotApplicableFor("9.2.4", "(111)", desc, c.NotApplicable)); return res; }
        if (c.Failure != null)
        {
            res.Add(new Sp16CheckResult
            {
                Clause = "9.2.4", Formula = "(111)", Description = desc, Utilization = double.PositiveInfinity, Status = CheckStatus.Fail,
                Variables = c.Vars.Select(v => new KeyValuePair<string, double>(v.Name, v.Value)).ToList(),
                Notes = [c.Failure],
            });
            return res;
        }
        double cap = c.C * c.PhiY * s.A * s.Mat.Ry * m.P.GammaC;
        res.Add(Sp16CheckResult.Of("9.2.4", "(111)", desc, nAbs / cap,
            [.. c.Vars, ("A", s.A), ("Ry", s.Mat.Ry), ("γc", m.P.GammaC)], nAbs, cap, c.Notes));
        return res;
    }

    /// <summary>9.2.8, формула (115): изгиб в плоскости наименьшей жёсткости — центральное сжатие из плоскости при λвне &gt; λв.</summary>
    static Sp16CheckResult Formula115(Sp16Member m, double nAbs, bool aboutX, string desc, bool equalInertia)
    {
        var s = m.S;
        bool outX = !aboutX;
        string ain = m.Axis(aboutX), aout = m.Axis(outX);
        double lIn = m.Lambda(aboutX), lOut = m.Lambda(outX);
        string? eqNote = equalInertia ? $"I{ain} = I{aout} — принят расчёт как при изгибе в плоскости наименьшей жёсткости (9.2.8)" : null;
        if (lOut <= lIn)
            return Sp16CheckResult.NotApplicableFor("9.2.8", "(115)", desc,
                $"изгиб в плоскости наименьшей жёсткости, λ{aout} = {lOut:0.#} ≤ λ{ain} = {lIn:0.#} — проверка устойчивости из плоскости действия момента не требуется");
        if (m.Phi(outX) is not { } phi)
            return Sp16CheckResult.NotApplicableFor("9.2.8", "(115)", desc,
                $"тип сечения по табл. 7 относительно оси {aout} не определён — задайте его вручную");
        double cap = phi * s.A * s.Mat.Ry * m.P.GammaC;
        return Sp16CheckResult.Of("9.2.8", "(115)", desc, nAbs / cap,
            [("N", -nAbs), ($"λ{ain}", lIn), ($"λ{aout}", lOut), ($"λ̄{aout}", m.LambdaBar(outX)), ($"φ{aout}", phi),
             ("A", s.A), ("Ry", s.Mat.Ry), ("γc", m.P.GammaC)],
            nAbs, cap, [eqNote, "изгиб в плоскости наименьшей жёсткости: в плоскости момента — (109), из плоскости — как центрально сжатый элемент"]);
    }

    /// <summary>
    /// Коэффициент c по 9.2.5 для сжатия силой nAbs (кН, &gt; 0) с моментом moment (кН·м, со знаком)
    /// относительно канонической оси x (плоскость наибольшей жёсткости): mx — по 9.2.6, α и β — по табл. 21,
    /// φb для (113) — по прил. Ж как для балки с двумя и более закреплениями сжатого пояса.
    /// </summary>
    public static CoefficientCResult CoefficientC(Sp16Member m, double nAbs, double moment)
    {
        var s = m.S; var p = m.P;
        if (s.Kind is not (SteelProfileKind.IBeam or SteelProfileKind.Tee or SteelProfileKind.Channel))
            return new() { NotApplicable = "сечение не приведено в табл. 21" };
        if (m.Curve(false) is not { } curveY)
            return new() { NotApplicable = $"тип сечения по табл. 7 относительно оси {m.Axis(false)} не определён" };
        double lbY = m.LambdaBar(false), phiY = Sp16Stability.Phi(lbY, curveY);
        var notes = new List<string?>();

        // 9.2.6: расчётный момент для mx.
        double mAbs = Math.Abs(moment), mDesign;
        if (p.CantileverColumn)
        {
            mDesign = mAbs;
            notes.Add("9.2.6: стержень с защемлённым и свободным концами — заданный момент принят как момент в заделке");
        }
        else
        {
            double ratio = p.MomentShape == MomentShape.LinearEndMoments ? (2 + p.EndMomentRatio) / 3 : p.MiddleThirdMomentRatio;
            double r = Math.Clamp(ratio, 0.5, 1.0);
            mDesign = r * mAbs;
            notes.Add($"9.2.6: концы закреплены от смещения из плоскости — наибольший момент в средней трети длины {r:0.###}·Mmax (не менее 0,5Mmax)"
                      + (p.MomentShape == MomentShape.LinearEndMoments ? $", линейная эпюра, δ = {p.EndMomentRatio:0.##}" : ""));
        }
        bool topComp = moment < 0;                       // Mx > 0 растягивает сторону y > 0
        double wc = s.Wx(topComp), a = s.A;
        double mx = mDesign * a / (nAbs * wc);

        // Табл. 21: тип сечения, I1, I2 — большей и меньшей полок относительно оси y.
        int type;
        double i1 = 0, i2 = 0;
        if (s.Kind == SteelProfileKind.Channel) type = 3;
        else if (s.Profile.IsDoublySymmetricIBeam) type = 1;
        else
        {
            double iTop = s.TfTop * Math.Pow(s.BfTop, 3) / 12, iBot = s.TfBottom * Math.Pow(s.BfBottom, 3) / 12;
            bool topMore = iTop >= iBot;
            i1 = Math.Max(iTop, iBot); i2 = Math.Min(iTop, iBot);
            type = topMore == topComp ? 2 : 4;
        }
        double ratioI = i1 > 0 ? i2 / i1 : 0;
        double phiC = Sp16Stability.Phi(3.14, curveY);
        double Alpha(double mm) => type == 4
            ? (mm <= 1 ? 1 - 0.3 * ratioI : 1 - (0.35 - 0.05 * mm) * ratioI)
            : (mm <= 1 ? 0.7 : 0.65 + 0.05 * mm);
        double beta = lbY <= 3.14 ? 1
            : type == 4 ? (ratioI < 0.5 ? 1 : 1 - (1 - Math.Sqrt(phiC / phiY)) * (2 * ratioI - 1))
            : Math.Sqrt(phiC / phiY);
        double C112(double mm) => Math.Min(1.0, beta / (1 + Alpha(mm) * mm));

        var vars = new List<(string, double)> { ("N", -nAbs), ("Mx", moment), ("Mx (9.2.6)", mDesign), ("Wc", wc), ("mx", mx),
            ("λ̄y", lbY), ("φy", phiY) };
        if (type is 2 or 4) vars.AddRange([("I1", i1), ("I2", i2)]);
        notes.Add($"табл. 21: тип сечения {type}" + type switch
        {
            1 => " (двутавр с двумя осями симметрии)",
            2 => s.Kind == SteelProfileKind.Tee ? " (тавр, сжата полка)" : " (двутавр, сжат более развитый пояс)",
            3 => " (швеллер)",
            _ => s.Kind == SteelProfileKind.Tee ? " (тавр, сжат конец стенки)" : " (двутавр, сжат менее развитый пояс)",
        });

        double c;
        string formula;
        if (mx <= 5)
        {
            c = C112(mx); formula = "(112)";
            vars.AddRange([("α", Alpha(mx)), ("β", beta)]);
        }
        else
        {
            var pb = PhiBFor113(m, topComp, notes);
            if (pb.NotApplicable != null)
                return new() { Vars = vars, Mx = mx, PhiY = phiY, Failure = $"mx = {mx:0.##} > 5: φb для (113) не определён — {pb.NotApplicable}" };
            double C113(double mm) => 1 / (1 + mm * phiY / pb.PhiB);
            vars.Add(("φb", pb.PhiB));
            if (mx >= 10) { c = C113(mx); formula = "(113)"; }
            else
            {
                double c5 = C112(5), c10 = C113(10);
                c = c5 * (2 - 0.2 * mx) + c10 * (0.2 * mx - 1); formula = "(114)";
                vars.AddRange([("α (mx = 5)", Alpha(5)), ("β", beta), ("c5", c5), ("c10", c10)]);
            }
        }
        if (lbY > 3.14) vars.Add(("φc", phiC));
        vars.Add(($"c {formula}", c));
        if (c < 0.3) { c = 0.3; notes.Add("c < 0,3 — принято c = 0,3 (9.2.5)"); }

        if (lbY > 3.14)
        {
            double exUp = Math.Sign(moment) * mDesign / -nAbs;      // ex = Mx/N, «+» — сила смещена в сторону y > 0
            var cm = CMax(s, m.Lambda(false), exUp);
            if (cm.CMax is { } cmax)
            {
                vars.AddRange(cm.Vars);
                vars.Add(("cmax", cmax));
                if (c > cmax) { c = cmax; notes.Add($"λ̄y > 3,14, c > cmax — принято c = cmax (9.2.5, (Д.1), табл. Д.6, тип {cm.Type})"); }
            }
            else
                notes.Add($"λ̄y > 3,14: cmax не определён — {cm.Reason}; ограничение c ≤ cmax не выполнено");
        }
        vars.Add(("c", c));
        return new() { C = c, Mx = mx, PhiY = phiY, Formula = formula, Vars = vars, Notes = notes };
    }

    /// <summary>φb для (113): балка с двумя и более закреплениями сжатого пояса (разрезная, не консоль).</summary>
    static PhiBResult PhiBFor113(Sp16Member m, bool topComp, List<string?> notes)
    {
        var p = m.P with { LtbRestraints = LtbRestraints.TwoOrMore, Cantilever = false, LtbFixedEnds = false };
        bool singly = m.S.Kind == SteelProfileKind.Tee || m.S.Kind == SteelProfileKind.IBeam && !m.S.Profile.IsDoublySymmetricIBeam;
        if (singly && p.LtbLoad is not (LtbLoadKind.ConcentratedMid or LtbLoadKind.Uniform or LtbLoadKind.PureBending))
        {
            p = p with { LtbLoad = LtbLoadKind.PureBending };
            notes.Add("φb для (113): вид нагрузки вне табл. Ж.4 — принят чистый изгиб");
        }
        var pb = Sp16PhiB.Compute(m.WithParams(p), topComp);
        if (pb.NotApplicable == null)
            notes.Add("φb для (113) — по прил. Ж как для балки с двумя и более закреплениями сжатого пояса, lef = " + $"{p.LefBOrY:0.###} м");
        return pb;
    }

    /// <summary>
    /// cmax по (Д.1), (Д.2) для сечений типов 1–3 табл. Д.6 (двутавр с двумя и с одной осью симметрии, тавр):
    /// lambdaY — гибкость λy (не условная), exUp — эксцентриситет ex = Mx/N, м, «+» — сила смещена в сторону y &gt; 0.
    /// Направление «+» в (Д.1) — к большему поясу (полке тавра). h — расстояние между осями поясов
    /// (у тавра — от оси полки до конца стенки); It — по п. 1 прил. Д.
    /// </summary>
    public static CMaxResult CMax(Sp16Section s, double lambdaY, double exUp)
    {
        if (s.Kind == SteelProfileKind.Channel)
            return new(null, 0, [], "для швеллера при внецентренном сжатии cmax в прил. Д не приведён ((Д.2а) — для центрального сжатия)");
        if (s.Kind is not (SteelProfileKind.IBeam or SteelProfileKind.Tee))
            return new(null, 0, [], "сечение не приведено в табл. Д.6");
        double tTop = s.TfTop, tBot = s.TfBottom;
        double iTop = tTop * Math.Pow(s.BfTop, 3) / 12, iBot = tBot * Math.Pow(s.BfBottom, 3) / 12;
        double yTop = s.YTop - tTop / 2, yBot = s.YBottom - tBot / 2, h = yTop + yBot;
        bool topMore = iTop >= iBot;
        double i1 = topMore ? iTop : iBot, i2 = topMore ? iBot : iTop;
        double h1 = topMore ? yTop : yBot, h2 = topMore ? yBot : yTop;
        double b1 = topMore ? s.BfTop : s.BfBottom;
        double ex = topMore ? exUp : -exUp;
        int type;
        double omega, alpha, beta;
        if (s.Profile.IsDoublySymmetricIBeam) { type = 1; omega = 0.25; alpha = 0; beta = 0; }
        else if (s.Kind == SteelProfileKind.Tee || i2 <= 0) { type = 3; omega = 0; alpha = h1 / h; beta = Sp16PhiB.BetaZh12(1, b1 / h); }
        else
        {
            type = 2;
            omega = i1 * i2 / (s.Iy * s.Iy);
            alpha = (i1 * h1 - i2 * h2) / (s.Iy * h);
            beta = Sp16PhiB.BetaZh12(i1 / (i1 + i2), b1 / h);
        }
        double a = s.A, ah2 = a * h * h;
        double rho = (s.Ix + s.Iy) / ah2 + alpha * alpha;
        double mu = 8 * omega + 0.156 * s.It * lambdaY * lambdaY / ah2;
        double delta = 4 * rho / mu;
        double bb = 1 + 2 * beta / rho * ex / h;
        double e = alpha - ex / h;
        double cmax = 2 / (1 + delta * bb + Math.Sqrt(Math.Pow(1 - delta * bb, 2) + 16 / mu * e * e));
        return new(cmax, type, [("ex", ex), ("h", h), ("ω", omega), ("α (Д.6)", alpha), ("β (Д.6)", beta), ("It", s.It),
            ("ρ", rho), ("μ", mu), ("δ", delta), ("B", bb)]);
    }
}
