namespace CScore.Sp16;

/// <summary>Результат расчёта φb по приложению Ж: значение, промежуточные величины и примечания; при неприменимости — причина.</summary>
public sealed record PhiBResult(double PhiB, List<(string Name, double Value)> Vars, List<string> Notes, string? NotApplicable = null)
{
    /// <summary>Неприменимо с причиной.</summary>
    public static PhiBResult Fail(string reason) => new(double.NaN, [], [], reason);
}

/// <summary>
/// Приложение Ж СП 16: коэффициент устойчивости при изгибе φb для балок двутаврового, таврового и
/// швеллерного сечения с опорными сечениями, закреплёнными от боковых смещений и поворота.
/// Ж.2–Ж.3 (табл. Ж.1, Ж.2) — двутавр с двумя осями симметрии (балка и консоль);
/// Ж.4–Ж.6 (табл. Ж.3–Ж.5) — разрезная балка двутаврового сечения с одной осью симметрии и тавр;
/// Ж.7 — швеллер. Нагрузка — в плоскости наибольшей жёсткости (изгиб относительно канонической оси x).
/// </summary>
public static class Sp16PhiB
{
    /// <summary>
    /// φb для элемента; topCompressed — сжат верхний (y &gt; 0) пояс, т. е. Mx &lt; 0 (Mx = ∫σ·y dA).
    /// </summary>
    public static PhiBResult Compute(Sp16Member m, bool topCompressed)
    {
        var s = m.S;
        if (m.P.LefBOrY <= 0) return PhiBResult.Fail("не задана расчётная длина балки lef (8.4.2)");
        return s.Kind switch
        {
            SteelProfileKind.IBeam when s.Profile.IsDoublySymmetricIBeam => DoublySymmetric(m, channel: false),
            SteelProfileKind.IBeam or SteelProfileKind.Tee => SinglySymmetric(m, topCompressed),
            SteelProfileKind.Channel => DoublySymmetric(m, channel: true),
            _ => PhiBResult.Fail("приложение Ж распространяется на двутавровые, тавровые и швеллерные сечения"),
        };
    }

    // ── Двутавр с двумя осями симметрии (Ж.2, Ж.3) и швеллер (Ж.7) ──

    static PhiBResult DoublySymmetric(Sp16Member m, bool channel)
    {
        var s = m.S; var p = m.P;
        double lef = p.LefBOrY;
        bool restrained = p.Cantilever || p.LtbRestraints != LtbRestraints.None;
        var notes = new List<string>();
        var vars = new List<(string, double)> { ("lef", lef) };
        bool rolledFormula = channel || s.Profile.Fabrication == SteelFabrication.Rolled;
        double alpha, h;
        if (rolledFormula)
        {
            // (Ж.4) с исправлением по письму ФАУ «ФЦС» от 15.12.2023 № Исх-9147.
            double k = restrained ? 1.54 : 1.0;
            h = s.Profile.H;
            alpha = AlphaRolled(s, lef, k);
            vars.AddRange([("k", k), ("It", s.It), ("Iy", s.Iy), ("h", h), ("α", alpha)]);
            notes.Add("α по (Ж.4)" + (channel ? " с Ix, Iy, It швеллера (Ж.7)" : ""));
            if (channel && s.Profile.Fabrication != SteelFabrication.Rolled)
                notes.Add("гнутый швеллер: φb по Ж.7 с It по прил. Д как для прокатного");
        }
        else
        {
            double k = restrained ? 8 : 4;
            double tf = s.TfTop, bf = s.BfTop, tw = s.Tw;
            double hm = restrained ? s.Profile.H - tf : s.Profile.H;
            h = s.Profile.H - tf;                      // расстояние между осями поясов составного двутавра
            alpha = k * Math.Pow(lef * tf / (hm * bf), 2) * (1 + 0.5 * hm * Math.Pow(tw, 3) / (bf * Math.Pow(tf, 3)));
            vars.AddRange([("k", k), ("hm", hm), ("bf", bf), ("tf", tf), ("tw", tw), ("h", h), ("α", alpha)]);
            notes.Add("α по (Ж.5) — составной двутавр со сварными (фрикционными) поясными соединениями");
        }

        var psi = p.Cantilever ? PsiCantilever(p, alpha, notes) : PsiBeam(p, alpha, notes);
        if (psi.Reason != null) return PhiBResult.Fail(psi.Reason);
        vars.Add(("ψ", psi.Value));

        double phi1 = psi.Value * s.Iy / s.Ix * Math.Pow(h / lef, 2) * s.Mat.E / s.Mat.Ry;   // (Ж.3)
        vars.Add(("φ1", phi1));
        double phiB;
        if (channel)
        {
            phiB = 0.7 * phi1;                                                             // Ж.7
            if (phiB > 1) { phiB = 1; notes.Add("φb = 0,7φ1 > 1 — принято φb = 1"); }
        }
        else
            phiB = phi1 <= 0.85 ? phi1 : Math.Min(1.0, 0.68 + 0.21 * phi1);             // (Ж.1), (Ж.2)
        return new PhiBResult(phiB, vars, notes);
    }

    /// <summary>(Ж.4) в редакции письма № Исх-9147: α = k·It/Iy·(lef/h)², h — полная высота.</summary>
    public static double AlphaRolled(Sp16Section s, double lef, double k) => k * s.It / s.Iy * Math.Pow(lef / s.Profile.H, 2);

    /// <summary>ψ по табл. Ж.1 (балка, опорные сечения раскреплены от опрокидывания).</summary>
    public static (double Value, string? Reason) PsiBeam(SteelDesignParams p, double alpha, List<string> notes)
    {
        bool onTension = p.LtbLoadOnTensionFlange;
        double Psi1() => alpha <= 40 ? 2.25 + 0.07 * alpha : 3.6 + 0.04 * alpha - 3.5e-5 * alpha * alpha;
        if (alpha < 0.1 || alpha > 400) notes.Add($"α = {alpha:0.###} вне диапазона табл. Ж.1 (0,1…400) — формула экстраполирована");
        switch (p.LtbRestraints)
        {
            case LtbRestraints.TwoOrMore:
                notes.Add("табл. Ж.1: два и более закреплений сжатого пояса, нагрузка любая");
                return (Psi1(), null);
            case LtbRestraints.OneAtMid:
                notes.Add("табл. Ж.1: одно закрепление в середине, ψ1 — как при двух и более (прим. 1)");
                return p.LtbLoad switch
                {
                    LtbLoadKind.ConcentratedMid => (1.75 * Psi1(), null),
                    LtbLoadKind.ConcentratedQuarter => ((onTension ? 1.60 : 1.14) * Psi1(), null),
                    LtbLoadKind.Uniform => ((onTension ? 1.30 : 1.14) * Psi1(), null),
                    _ => (double.NaN, "в табл. Ж.1 для одного закрепления в середине нет такой схемы нагрузки — рассчитайте участок между закреплениями как балку без закреплений с соответствующей эпюрой (lef — длина участка)"),
                };
        }
        if (p.LtbFixedEnds && p.LtbLoad is not (LtbLoadKind.ConcentratedMid or LtbLoadKind.Uniform
                or LtbLoadKind.PureBending or LtbLoadKind.EndMomentOneSide or LtbLoadKind.EndMomentsOpposite))
            notes.Add("в табл. Ж.1 нет схемы с защемлёнными концами для этой нагрузки — принята шарнирная (в запас)");
        (double C1, double C2)? c = p.LtbLoad switch
        {
            LtbLoadKind.ConcentratedMid => p.LtbFixedEnds ? (1.73, 1.4) : (1.37, 0.55),
            LtbLoadKind.ConcentratedQuarter => (1.49, 0.41),
            LtbLoadKind.TwoConcentratedThirds => (1.1, 0.5),
            LtbLoadKind.Uniform => p.LtbFixedEnds ? (1.25, 1.01) : (1.13, 0.46),
            _ => null,
        };
        if (c is { } cc)
        {
            // Знак «−» — нагрузка на сжатом (верхнем) поясе, «+» — на растянутом (нижнем).
            double sign = onTension ? 1 : -1;
            notes.Add($"табл. Ж.1 без закреплений: C1 = {cc.C1:0.##}, C2 = {cc.C2:0.##}, нагрузка на {(onTension ? "растянутом" : "сжатом")} поясе");
            return (cc.C1 * (Math.Sqrt(0.95 * alpha + 6.09 * cc.C2 * cc.C2 + 5.78) + sign * 2.47 * cc.C2), null);
        }
        double? c1 = p.LtbLoad switch
        {
            LtbLoadKind.PureBending => 1.0,
            LtbLoadKind.EndMomentOneSide => 1.88,
            LtbLoadKind.EndMomentsOpposite => 2.77,
            _ => null,
        };
        if (c1 == null) return (double.NaN, "схема нагрузки относится к консоли (табл. Ж.2) — отметьте «консоль»");
        notes.Add($"табл. Ж.1 без закреплений, концевые моменты: C1 = {c1:0.##}");
        return (c1.Value * Math.Sqrt(0.95 * alpha + 5.78), null);
    }

    /// <summary>ψ по табл. Ж.2 (жёстко заделанная консоль) с учётом Ж.3 для консоли с закреплённым сжатым поясом.</summary>
    public static (double Value, string? Reason) PsiCantilever(SteelDesignParams p, double alpha, List<string> notes)
    {
        bool onTension = p.LtbLoadOnTensionFlange;
        if (p.LtbRestraints != LtbRestraints.None && p.LtbLoad == LtbLoadKind.ConcentratedEnd && onTension)
        {
            double psi1 = alpha <= 40 ? 2.25 + 0.07 * alpha : 3.6 + 0.04 * alpha - 3.5e-5 * alpha * alpha;
            notes.Add("Ж.3: консоль с закреплённым сжатым поясом, сила на растянутом поясе на конце — ψ = 1,75ψ1");
            return (1.75 * psi1, null);
        }
        if (p.LtbRestraints != LtbRestraints.None)
            notes.Add("Ж.3: консоль с закреплённым сжатым поясом — ψ как для консоли без закреплений");
        notes.Add("табл. Ж.2: α — с k как для схем с раскреплениями (прим.)");
        if (alpha < 4 || alpha > 100) notes.Add($"α = {alpha:0.###} вне диапазона табл. Ж.2 (4…100) — формула экстраполирована");
        return p.LtbLoad switch
        {
            LtbLoadKind.ConcentratedEnd => onTension
                ? (alpha <= 28 ? 1.0 + 0.16 * alpha : 4.0 + 0.05 * alpha, null)
                : (alpha <= 28 ? 6.2 + 0.08 * alpha : 7.0 + 0.05 * alpha, null),
            LtbLoadKind.Uniform => onTension
                ? (1.42 * Math.Sqrt(alpha), null)
                : (double.NaN, "в табл. Ж.2 равномерная нагрузка на консоль приведена только для растянутого пояса"),
            _ => (double.NaN, "в табл. Ж.2 для консоли — только сосредоточенная сила на конце или равномерная нагрузка"),
        };
    }

    // ── Двутавр с одной осью симметрии и тавр (Ж.4–Ж.6) ──

    static PhiBResult SinglySymmetric(Sp16Member m, bool topCompressed)
    {
        var s = m.S; var p = m.P;
        if (p.Cantilever || p.LtbFixedEnds)
            return PhiBResult.Fail("Ж.4 — только для разрезных балок; для консолей и балок с защемлёнными концами с одной осью симметрии φb в СП 16 не приведён");
        int load = p.LtbLoad switch
        {
            LtbLoadKind.ConcentratedMid => 0,
            LtbLoadKind.Uniform => 1,
            LtbLoadKind.PureBending => 2,
            _ => -1,
        };
        if (load < 0) return PhiBResult.Fail("табл. Ж.4, Ж.5 — только сосредоточенная в середине, равномерная нагрузка или чистый изгиб");

        double lef = p.LefBOrY;
        var notes = new List<string>();
        // Пояса: оси, моменты инерции относительно оси симметрии y (у тавра второй «пояс» отсутствует).
        double tTop = s.TfTop, tBot = s.TfBottom;
        double iTop = tTop * Math.Pow(s.BfTop, 3) / 12, iBot = tBot * Math.Pow(s.BfBottom, 3) / 12;
        double yTop = s.YTop - tTop / 2, yBot = s.YBottom - tBot / 2;
        bool topMore = iTop >= iBot;
        double i1 = topMore ? iTop : iBot, i2 = topMore ? iBot : iTop;
        double h1 = topMore ? yTop : yBot, h2 = topMore ? yBot : yTop;
        double b1 = topMore ? s.BfTop : s.BfBottom, b2 = topMore ? s.BfBottom : s.BfTop;
        double h = yTop + yBot;
        double n = i1 / (i1 + i2);                                                          // (Ж.8)
        bool moreCompressed = topMore == topCompressed;
        bool onTension = p.LtbLoadOnTensionFlange;
        bool tee = s.Kind == SteelProfileKind.Tee || i2 <= 0;

        // α по (Ж.4) с k = 1,54 — так (Ж.13) переходит в столбец «тавр» табл. Ж.5 при n → 1.
        double alpha = AlphaRolled(s, lef, 1.54);
        notes.Add("α по (Ж.4) с k = 1,54 — согласовано с (Ж.13) и столбцом «тавр» табл. Ж.5");
        notes.Add("(Ж.13): отношение I1/I2 в опубликованном тексте принято как It/I2 (экспликация и предельный переход n → 1 к столбцу «тавр» табл. Ж.5)");

        double PsiA(double nn, bool teeColumn)
        {
            double r = b1 / h;
            double beta = (2 * nn - 1) * (0.47 - 0.035 * r * (1 + r - 0.072 * r * r));      // (Ж.12)
            double delta = nn + 0.734 * beta, mu = nn + 1.145 * beta;                        // (Ж.10), (Ж.11)
            double bConc, bUni, bPure;
            if (moreCompressed)
            {
                bPure = beta;
                (bConc, bUni) = onTension ? (delta, mu) : (delta - 1, mu - 1);
            }
            else
            {
                bPure = -beta;
                (bConc, bUni) = onTension ? (1 - delta, 1 - mu) : (-delta, -mu);
            }
            double B = load switch { 0 => bConc, 1 => bUni, _ => bPure };
            double C;
            if (teeColumn)
            {
                C = load switch { 0 => 0.0826, 1 => 0.1202, _ => 0.0253 } * alpha;
            }
            else
            {
                double eta = (1 - nn) * (9.87 * nn + 0.385 * s.It / i2 * Math.Pow(lef / h, 2));   // (Ж.13)
                C = load switch { 0 => 0.330, 1 => 0.481, _ => 0.101 } * eta;
            }
            double D = load switch { 0 => 3.265, 1 => 2.247, _ => 4.315 };
            double psi = (B + Math.Sqrt(B * B + C)) * D;                                      // (Ж.9)
            if (teeColumn && load < 2 && alpha < 40) psi *= 0.8 + 0.004 * alpha;              // Ж.6
            return psi;
        }

        double psiA;
        if (tee || n >= 1 - 1e-9) psiA = PsiA(1, true);
        else if (n <= 0.9) psiA = PsiA(n, false);
        else
        {
            double pI = PsiA(0.9, false), pT = PsiA(1, true);
            psiA = pI + (pT - pI) * (n - 0.9) / 0.1;
            notes.Add("Ж.6: 0,9 < n < 1 — ψa интерполирован между двутавром (n = 0,9) и тавром (n = 1)");
        }
        if (tee && load < 2 && alpha < 40) notes.Add("Ж.6: тавр, α < 40 — ψa умножен на (0,8 + 0,004α)");

        double k0 = s.Iy / s.Ix * 2 * h / (lef * lef) * s.Mat.E / s.Mat.Ry;
        double phi1 = psiA * k0 * h1, phi2 = psiA * k0 * h2;                                   // (Ж.6), (Ж.7)
        var vars = new List<(string, double)>
        {
            ("lef", lef), ("h", h), ("h1", h1), ("h2", h2), ("I1", i1), ("I2", i2), ("n", n),
            ("It", s.It), ("α", alpha), ("ψa", psiA), ("φ1", phi1), ("φ2", phi2),
        };

        if (!moreCompressed && n > 0.7)
        {
            double r = b2 > 0 ? lef / b2 : double.PositiveInfinity;
            if (r > 25)
                return PhiBResult.Fail($"Ж.6: сжатый пояс менее развит, n > 0,7 и lef/b2 = {(double.IsInfinity(r) ? "∞" : r.ToString("0.#"))} > 25 — не допускается");
            if (r >= 5)
            {
                phi2 = Math.Min(0.95, phi2 * (1.025 - 0.015 * r));
                vars.Add(("φ2,red", phi2));
                notes.Add("Ж.6: φ2 уменьшен умножением на (1,025 − 0,015lef/b2), не более 0,95");
            }
        }

        double phiB = moreCompressed
            ? (phi2 <= 0.85 ? Math.Min(1.0, phi1) : Math.Min(1.0, phi1 * (0.21 + 0.68 * (n / phi1 + (1 - n) / phi2))))
            : (phi2 <= 0.85 ? phi2 : Math.Min(1.0, 0.68 + 0.21 * phi2));
        notes.Add($"табл. Ж.3: сжат {(moreCompressed ? "более" : "менее")} развитый пояс");
        if (p.LtbRestraints != LtbRestraints.None)
            notes.Add("закрепления сжатого пояса учтены только через lef (8.4.2); B, C, D — по виду нагрузки пролёта");
        return new PhiBResult(phiB, vars, notes);
    }
}
