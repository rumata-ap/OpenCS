namespace CScore.Sp16;

/// <summary>
/// 8.5 СП 16: местная устойчивость стенок и поясов изгибаемых элементов сплошного сечения —
/// 8.5.1 (λ̄w ≤ λ̄uw), 8.5.2–8.5.5 (78)–(84) с табл. 12–16 (стенки 1-го класса симметричного сечения,
/// укреплённые только поперечными рёбрами), 8.5.8 а) (86) с табл. 18 (стенки 2-го/3-го классов),
/// 8.5.18 (97), (98) и 8.5.19 (99), (100) — сжатые пояса. Усилия — канонические оси, кН, кН·м.
/// Местная нагрузка F (8.2.2) считается приложенной к верхнему поясу.
/// </summary>
public static class Sp16Section8Local
{
    const double Eps = 1e-9;
    const string WebTitle = "Устойчивость стенки балки";

    /// <summary>Проверки устойчивости стенки и сжатых поясов.</summary>
    public static List<Sp16CheckResult> Check(Sp16Member m, SteelForces f)
    {
        bool plastic = m.P.AllowPlastic && Sp16Section8Strength.PlasticNotApplicableReason(m) == null;
        var res = plastic ? Web2ndClass(m, f) : Web1stClass(m, f);
        res.AddRange(Flanges(m, f, plastic));
        return res;
    }

    /// <summary>Условная гибкость стенки λ̄w = (hef/tw)·√(Ry/E).</summary>
    public static double LambdaW(Sp16Section s) => s.Hef / s.Tw * Math.Sqrt(s.Mat.Ry / s.Mat.E);

    // ── Стенки балок 1-го класса (8.5.1–8.5.5) ──────────────────────────

    static List<Sp16CheckResult> Web1stClass(Sp16Member m, SteelForces f)
    {
        var res = new List<Sp16CheckResult>();
        var s = m.S; var p = m.P;
        double sloc = Sp16Section8Strength.LocalSigma(m) ?? 0;
        if (Math.Abs(f.Mx) <= Eps && Math.Abs(f.Qy) <= Eps && sloc <= 0) return res;
        if (s.Kind is not (SteelProfileKind.IBeam or SteelProfileKind.Channel or SteelProfileKind.Box))
        {
            if (s.Kind is SteelProfileKind.Generic or SteelProfileKind.Tee)
                res.Add(Sp16CheckResult.NotApplicableFor("8.5.1", "λ̄uw", WebTitle, s.Kind == SteelProfileKind.Generic
                    ? "профиль не распознан — задайте профиль вручную"
                    : "8.5 не устанавливает проверку устойчивости стенки таврового сечения"));
            return res;
        }

        double lw = LambdaW(s);
        var (luw, luwNote) = WebLimit851(m, sloc);

        var notes = new List<string?> { luwNote, RibNote(s, p, lw) };
        var vars = new List<(string, double)> { ("hef", s.Hef), ("tw", s.Tw), ("λ̄w", lw), ("λ̄uw", luw) };
        if (lw <= luw)
        {
            notes.Add("проверка не требуется при выполнении 8.2.1, 8.4.1–8.4.5 и постановке рёбер жёсткости по 8.5.9");
            res.Add(Sp16CheckResult.Limit("8.5.1", "λ̄uw", "Устойчивость стенки не требует проверки: λ̄w ≤ λ̄uw", lw, luw, vars, notes));
            return res;
        }
        if (p.RibSpacing <= 0)
        {
            notes.Add("λ̄w > λ̄uw: стенка должна быть укреплена поперечными рёбрами жёсткости и проверена по 8.5.3 — задайте шаг рёбер a");
            res.Add(Sp16CheckResult.Limit("8.5.1", "λ̄uw", WebTitle + ": λ̄w ≤ λ̄uw", lw, luw, vars, notes));
            return res;
        }
        if (!s.SymmetricAboutX)
        {
            res.Add(Sp16CheckResult.NotApplicableFor("8.5.3", "(80)", WebTitle,
                $"λ̄w = {lw:0.###} > λ̄uw = {luw:0.#}; сечение несимметрично относительно оси x — проверка по 8.5.6, 8.5.7 вне объёма"));
            return res;
        }
        res.AddRange(Check853(m, f, lw, sloc, [$"λ̄w = {lw:0.###} > λ̄uw = {luw:0.#} (8.5.1) — проверка по 8.5.3", luwNote, RibNote(s, p, lw)]));
        return res;
    }

    /// <summary>Предельная условная гибкость стенки λ̄uw по 8.5.1 (3,5; 3,2 — односторонние поясные швы; 2,5 — при σloc ≠ 0).</summary>
    internal static (double Luw, string Note) WebLimit851(Sp16Member m, double sloc)
    {
        bool welded = m.S.Profile.Fabrication == SteelFabrication.Welded;
        if (sloc > 0)
            return (2.5, "σloc ≠ 0: λ̄uw = 2,5" + (welded && m.P.OneSidedFlangeWelds
                ? "; для односторонних поясных швов при σloc ≠ 0 предел в 8.5.1 не установлен — принято 2,5" : ""));
        if (welded && m.P.OneSidedFlangeWelds) return (3.2, "σloc = 0, односторонние поясные швы: λ̄uw = 3,2");
        return (3.5, welded ? "σloc = 0, двусторонние поясные швы: λ̄uw = 3,5" : "σloc = 0, прокатный (гнутый) профиль: λ̄uw = 3,5 (как при двусторонних поясных швах)");
    }

    /// <summary>Конструктивные требования 8.5.9 к постановке и шагу поперечных рёбер (примечание).</summary>
    static string? RibNote(Sp16Section s, SteelDesignParams p, double lw)
    {
        if (p.RibSpacing <= 0)
            return lw > 3.2 ? "8.5.9: при λ̄w > 3,2 (2,2 — при подвижной нагрузке на поясе) стенка должна быть укреплена поперечными рёбрами жёсткости" : null;
        double k = lw >= 3.2 ? 2.0 : 2.5;
        return p.RibSpacing > k * s.Hef
            ? $"8.5.9: шаг рёбер a = {p.RibSpacing:0.###} м > {k:0.#}hef = {k * s.Hef:0.###} м (до 3hef — только для балок 1-го класса при выполнении 8.4.4 а) или б))"
            : null;
    }

    /// <summary>8.5.3–8.5.5: условие (80) для симметричного сечения, укреплённого поперечными рёбрами.</summary>
    static List<Sp16CheckResult> Check853(Sp16Member m, SteelForces f, double lw, double sloc, List<string?> baseNotes)
    {
        var res = new List<Sp16CheckResult>();
        var s = m.S; var p = m.P;
        double ry = s.Mat.Ry, sq = Math.Sqrt(ry / s.Mat.E);
        double hef = s.Hef, tw = s.Tw, a = p.RibSpacing, ah = a / hef;
        double sigma = Math.Abs(f.Mx) * (hef / 2) / s.Ix;                                 // (78), y — до расчётной границы стенки
        double tau = Math.Abs(f.Qy) / (tw * s.WebCount * s.Hw);                           // (79)

        if (sigma > 0)
        {
            double lim = 6 * Math.Sqrt(ry / sigma);
            if (lw > lim)
            {
                res.Add(Sp16CheckResult.Limit("8.5.3", "λ̄w ≤ 6√(Ry/σ)", WebTitle + ": применимость 8.5.3", lw, lim,
                    [("λ̄w", lw), ("σ", sigma), ("Ry", ry)],
                    [.. baseNotes, "стенку следует укрепить продольным ребром жёсткости (8.5.11) — расчёт по 8.5.12–8.5.15 вне объёма"]));
                return res;
            }
        }

        bool topCompressed = f.Mx <= 0;
        bool loadOnTension = sloc > 0 && !topCompressed;                                 // F — к верхнему поясу, а он растянут
        double deltaComp = Delta84(s, p, topFlange: topCompressed, out string deltaNote);

        double d = Math.Min(a, hef), mu = Math.Max(a, hef) / d;
        double lambdaD = d / tw * sq;
        double tauCr = 10.3 * (1 + 0.76 / (mu * mu)) * s.Mat.Rs / (lambdaD * lambdaD);   // (83)
        double ccr12 = Sp16Tables.Table12Ccr(deltaComp, p.FrictionFlangeJoints);

        var common = new List<(string, double)> { ("a", a), ("hef", hef), ("tw", tw), ("λ̄w", lw), ("σ", sigma), ("τ", tau), ("μ", mu), ("λ̄d", lambdaD), ("τcr", tauCr), ("δ", deltaComp) };
        var notes = new List<string?>(baseNotes)
        {
            deltaNote,
            a > hef ? "(78), (79): M и Q — средние на более напряжённом участке длиной hef в пределах отсека (задаются пользователем)" : "(78), (79): M и Q — средние значения в пределах отсека (задаются пользователем)",
            s.Kind == SteelProfileKind.Box ? "коробчатое сечение: в (84) принята половина ширины пояса на одну стенку" : null,
            s.Kind == SteelProfileKind.Channel ? "8.5.3 сформулирован для двутавровых балок; для швеллера применён по аналогии" : null,
        };

        if (sloc <= 0)
        {
            double sigmaCr = ccr12 * ry / (lw * lw);                                     // (81), 8.5.4
            res.Add(Eq80(m, "Устойчивость стенки, укреплённой поперечными рёбрами (σloc = 0)", sigma, sigmaCr, 0, double.PositiveInfinity, tau, tauCr,
                [.. common, ("ccr", ccr12), ("σcr", sigmaCr)], [.. notes, "ccr по табл. 12 (8.5.4)"]));
            return res;
        }

        double rho = 1.04 * Sp16Section8Strength.LocalLef(m) / hef;
        if (rho < 0.10 || rho > 0.40) notes.Add($"ρ = {rho:0.###} вне диапазона табл. 14 (0,10…0,40) — c1 по крайнему значению");
        double deltaC2 = p.FrictionFlangeJoints ? 10 : deltaComp;
        if (p.FrictionFlangeJoints) notes.Add("фрикционные поясные соединения: для c2 принято δ = 10 (8.5.5)");

        if (loadOnTension)
        {
            // 8.5.2: нагрузка к растянутому поясу — порознь σ и τ; σloc и τ (δ — по растянутому поясу, 8.5.5 а).
            double sigmaCr = ccr12 * ry / (lw * lw);
            res.Add(Eq80(m, "Устойчивость стенки: σ и τ (нагрузка приложена к растянутому поясу)", sigma, sigmaCr, 0, double.PositiveInfinity, tau, tauCr,
                [.. common, ("ccr", ccr12), ("σcr", sigmaCr)], [.. notes, "8.5.2: в отсеке с нагрузкой на растянутом поясе σ и σloc учитываются порознь; ccr по табл. 12"]));
            double deltaT = p.FrictionFlangeJoints ? 10 : Delta84(s, p, topFlange: true, out _, loadedTensionFlange: true);
            double ahc = Math.Min(ah, 2);
            double c1 = Sp16Tables.Table14C1(rho, ahc), c2 = Sp16Tables.Table15C2(deltaT, ahc);
            double slocCr = c1 * c2 * ry / (lw * lw);                                    // (82)
            res.Add(Eq80(m, "Устойчивость стенки: σloc и τ (нагрузка приложена к растянутому поясу)", 0, double.PositiveInfinity, sloc, slocCr, tau, tauCr,
                [.. common, ("σloc", sloc), ("ρ", rho), ("δраст", deltaT), ("c1", c1), ("c2", c2), ("σloc,cr", slocCr)],
                [.. notes, "8.5.5 а): δ по (84) — по ширине и толщине растянутого (нагруженного) пояса",
                    ah > 0.8 ? "a/hef > 0,8: c1, c2 — при фактическом a/hef (не более 2), в запас" : null]));
            return res;
        }

        if (ah <= 0.8)
        {
            double sigmaCr = ccr12 * ry / (lw * lw);
            double c1 = Sp16Tables.Table14C1(rho, ah), c2 = Sp16Tables.Table15C2(deltaC2, ah);
            double slocCr = c1 * c2 * ry / (lw * lw);
            res.Add(Eq80(m, "Устойчивость стенки, укреплённой поперечными рёбрами (σ, σloc, τ)", sigma, sigmaCr, sloc, slocCr, tau, tauCr,
                [.. common, ("ccr", ccr12), ("σcr", sigmaCr), ("σloc", sloc), ("ρ", rho), ("c1", c1), ("c2", c2), ("σloc,cr", slocCr)],
                [.. notes, "8.5.5 а): a/hef ≤ 0,8 — ccr по табл. 12"]));
            return res;
        }

        // 8.5.5 б): две проверки.
        double a1 = ah <= 1.33 ? 0.5 * a : 0.67 * hef, a1h = a1 / hef;
        {
            double sigmaCr = ccr12 * ry / (lw * lw);
            double c1 = Sp16Tables.Table14C1(rho, a1h), c2 = Sp16Tables.Table15C2(deltaC2, a1h);
            double slocCr = c1 * c2 * ry / (lw * lw);
            res.Add(Eq80(m, "Устойчивость стенки (σ, σloc, τ), 8.5.5 б) — проверка 1", sigma, sigmaCr, sloc, slocCr, tau, tauCr,
                [.. common, ("ccr", ccr12), ("σcr", sigmaCr), ("σloc", sloc), ("ρ", rho), ("a1", a1), ("c1", c1), ("c2", c2), ("σloc,cr", slocCr)],
                [.. notes, "ccr по табл. 12; c1, c2 — при a1/hef (a1 = 0,5a при a/hef ≤ 1,33, иначе 0,67hef)"]));
        }
        {
            double ahc = Math.Min(ah, 2);
            double ccr16 = Sp16Tables.Table16Ccr(ahc, ccr12);
            double sigmaCr = ccr16 * ry / (lw * lw);
            double c1 = Sp16Tables.Table14C1(rho, ahc), c2 = Sp16Tables.Table15C2(deltaC2, ahc);
            double slocCr = c1 * c2 * ry / (lw * lw);
            res.Add(Eq80(m, "Устойчивость стенки (σ, σloc, τ), 8.5.5 б) — проверка 2", sigma, sigmaCr, sloc, slocCr, tau, tauCr,
                [.. common, ("ccr", ccr16), ("σcr", sigmaCr), ("σloc", sloc), ("ρ", rho), ("c1", c1), ("c2", c2), ("σloc,cr", slocCr)],
                [.. notes, "ccr по табл. 16; c1, c2 — при фактическом a/hef" + (ah > 2 ? " (a/hef > 2 — принято 2)" : "")]));
        }
        return res;
    }

    /// <summary>
    /// δ по (84) для верхнего (<paramref name="topFlange"/>) или нижнего пояса с β по табл. 13 (прочие балки:
    /// ∞ при непрерывном опирании плит на сжатый пояс, иначе 0,8).
    /// </summary>
    static double Delta84(Sp16Section s, SteelDesignParams p, bool topFlange, out string note, bool loadedTensionFlange = false)
    {
        double beta = p.ContinuousRigidDeck && !loadedTensionFlange ? double.PositiveInfinity : 0.8;
        note = double.IsPositiveInfinity(beta)
            ? "табл. 13: β = ∞ (непрерывное опирание плит на сжатый пояс)" : "табл. 13: β = 0,8";
        double bf = topFlange ? s.BfTop : s.BfBottom, tf = topFlange ? s.TfTop : s.TfBottom;
        if (s.Kind == SteelProfileKind.Box) bf /= 2;
        double geo = bf / s.Hef * Math.Pow(tf / s.Tw, 3);
        return double.IsPositiveInfinity(beta) ? beta : beta * geo;
    }

    /// <summary>Условие (80): √((σ/σcr + σloc/σloc,cr)² + (τ/τcr)²)/γc ≤ 1.</summary>
    static Sp16CheckResult Eq80(Sp16Member m, string desc, double sigma, double sigmaCr, double sloc, double slocCr, double tau, double tauCr,
        List<(string, double)> vars, List<string?> notes)
    {
        double k = sigma / sigmaCr + sloc / slocCr, t = tau / tauCr;
        double util = Math.Sqrt(k * k + t * t) / m.P.GammaC;
        vars.Add(("γc", m.P.GammaC));
        notes.Add("формула (80) — в редакции письма ФАУ «ФЦС» от 15.12.2023 № Исх-9147 (корень из числителя)");
        return Sp16CheckResult.Of("8.5.3", "(80)", desc, util, vars, notes: notes);
    }

    // ── Стенки балок 2-го и 3-го классов (8.5.8 а) ───────────────────────

    static List<Sp16CheckResult> Web2ndClass(Sp16Member m, SteelForces f)
    {
        var res = new List<Sp16CheckResult>();
        var s = m.S; var p = m.P;
        const string title = "Устойчивость стенки балки 2-го/3-го класса";
        double sloc = Sp16Section8Strength.LocalSigma(m) ?? 0;
        if (sloc > 0)
        {
            res.Add(Fail("8.5.8", "(86)", title,
                "8.5.8 — только при отсутствии местного напряжения (σloc = 0); выполните расчёт без учёта пластических деформаций"));
            return res;
        }
        if (Math.Abs(f.Mx) <= Eps) return res;
        if (s.Kind == SteelProfileKind.IBeam && !s.Profile.IsDoublySymmetricIBeam)
        {
            res.Add(Sp16CheckResult.NotApplicableFor("8.5.8", "(87)", title,
                "асимметричный двутавр — 8.5.8 б) вне объёма; выполните расчёт без учёта пластических деформаций"));
            return res;
        }
        double lw = LambdaW(s);
        double aw = s.Aw, tau = Math.Abs(f.Qy) / aw, tauRatio = tau / s.Mat.Rs;
        if (tauRatio > 0.9)
        {
            res.Add(Fail("8.5.8", "(86)", title, $"τ/Rs = {tauRatio:0.###} > 0,9 — вне табл. 18"));
            return res;
        }
        if (lw > 5.5)
        {
            res.Add(Fail("8.5.8", "(86)", title, $"λ̄w = {lw:0.###} > 5,5 — вне табл. 18; учёт пластических деформаций для такой стенки не предусмотрен"));
            return res;
        }
        double alpha = Sp16Tables.Table18Alpha(tauRatio, Math.Max(lw, 2.2));
        double af = Math.Min(s.AfTop, s.AfBottom), alphaF = af / aw, r = 1.0;
        double twSum = s.Tw * s.WebCount;
        double cap = s.Mat.Ry * p.GammaC * s.Hef * s.Hef * twSum * (r * alphaF + alpha);
        res.Add(Sp16CheckResult.Of("8.5.8", "(86)", title, Math.Abs(f.Mx) / cap,
            [("M", f.Mx), ("Q", f.Qy), ("τ = Q/Aw", tau), ("τ/Rs", tauRatio), ("λ̄w", lw), ("α", alpha), ("Af", af), ("Aw", aw), ("αf", alphaF), ("r", r), ("hef", s.Hef), ("tw", twSum), ("Ry", s.Mat.Ry), ("γc", p.GammaC)],
            Math.Abs(f.Mx), cap,
            [
                "r = Ryf/Ryw = 1 (однородная сталь, 8.4.5)",
                lw < 2.2 ? "λ̄w < 2,2 — α по столбцу λ̄w = 2,2 табл. 18" : null,
                s.Kind == SteelProfileKind.Box ? "коробчатое сечение: tw — суммарная толщина стенок" : null,
                "M и Q — в одном сечении балки; требуется соблюдение 7.3.1, 8.2.3 и 8.5.9 (рёбра на участках с пластическими деформациями — при любой λ̄w)",
            ]));
        return res;
    }

    static Sp16CheckResult Fail(string clause, string formula, string description, string note) => new()
    {
        Clause = clause, Formula = formula, Description = description,
        Utilization = double.PositiveInfinity, Status = CheckStatus.Fail, Notes = [note],
    };

    // ── Сжатые пояса (8.5.18, 8.5.19) ────────────────────────────────────

    static List<Sp16CheckResult> Flanges(Sp16Member m, SteelForces f, bool plastic)
    {
        var res = new List<Sp16CheckResult>();
        var s = m.S; var p = m.P;
        bool mx = Math.Abs(f.Mx) > Eps, my = Math.Abs(f.My) > Eps;
        if (!mx && !my) return res;
        string clause = plastic ? "8.5.19" : "8.5.18";
        const string title = "Устойчивость сжатого пояса балки";
        if (s.Kind is not (SteelProfileKind.IBeam or SteelProfileKind.Box))
        {
            if (s.Kind is SteelProfileKind.Generic or SteelProfileKind.Channel or SteelProfileKind.Tee)
                res.Add(Sp16CheckResult.NotApplicableFor(clause, "(97)", title, s.Kind == SteelProfileKind.Generic
                    ? "профиль не распознан — задайте профиль вручную"
                    : "8.5.18, 8.5.19 устанавливают предельную гибкость поясов двутавровых и коробчатых сечений"));
            return res;
        }

        double ry = s.Mat.Ry, sq = Math.Sqrt(ry / s.Mat.E), gc = p.GammaC;
        var flanges = new List<(string Name, bool Top)>();
        if (s.Kind == SteelProfileKind.Box)
        {
            if (!mx) return res;                                                          // (98) — пояс, сжатый от Mx
            flanges.Add(("", f.Mx < 0));
        }
        else if (s.SymmetricAboutX && !mx) flanges.Add(("", true));
        else if (mx) flanges.Add((f.Mx < 0 ? "верхний" : "нижний", f.Mx < 0));
        else { flanges.Add(("верхний", true)); flanges.Add(("нижний", false)); }

        foreach (var (name, top) in flanges)
        {
            bool box = s.Kind == SteelProfileKind.Box;
            double bf = top ? s.BfTop : s.BfBottom, tf = top ? s.TfTop : s.TfBottom;
            if (tf <= 0) continue;
            double b = box ? s.BefBoxFlange : (top ? s.BefTop : s.BefBottom);
            double lf = b / tf * sq;
            string desc = title + (name != "" ? $" ({name})" : "") + (box ? ": поясной лист" : ": свес полки");
            var vars = new List<(string, double)> { (box ? "bef,1" : "bef", b), ("tf", tf), (box ? "λ̄f1" : "λ̄f", lf) };
            var notes = new List<string?> { "окаймление и отгиб полки (8.5.20) не учитываются" };
            double lu;
            string formula;
            if (plastic)
            {
                double lw = LambdaW(s), luw = Math.Clamp(lw, 2.2, 5.5);
                formula = box ? "(100)" : "(99)";
                lu = box ? 0.675 + 0.15 * luw : 0.17 + 0.06 * luw;
                vars.AddRange([("λ̄w", lw), ("λ̄uw", luw)]);
                notes.Add("λ̄uw принята равной условной гибкости стенки λ̄w" + (lw < 2.2 || lw > 5.5 ? " (ограничена диапазоном 2,2…5,5)" : ""));
                notes.Add("при выполнении требований 7.3.7, 8.2.3 и 8.5.8");
            }
            else
            {
                double sc = (mx ? Math.Abs(f.Mx) / (s.Wx(top) * gc) : 0) + (my ? Math.Abs(f.My) / (s.Iy / (bf / 2) * gc) : 0);
                if (sc <= 0) continue;
                if (sc > ry) { notes.Add($"σc = {sc:0} кПа > Ry — принято σc = Ry"); sc = ry; }
                formula = box ? "(98)" : "(97)";
                lu = (box ? 1.5 : 0.5) * Math.Sqrt(ry / sc);
                vars.AddRange([("Mx", f.Mx), ("My", f.My), ("σc", sc), ("Ry", ry), ("γc", gc)]);
                notes.Add("σc = Mx/(Wxc·γc) + My/(Wyn·γc) — однородное сечение; Wyn — для крайней точки пояса");
            }
            vars.Add((box ? "λ̄uf,1" : "λ̄uf", lu));
            res.Add(Sp16CheckResult.Limit(clause, formula, desc, lf, lu, vars, notes));
        }
        return res;
    }
}
