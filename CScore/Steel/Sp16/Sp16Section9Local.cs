namespace CScore.Sp16;

/// <summary>
/// 9.4 СП 16: местная устойчивость стенок (9.4.2, табл. 22 — в редакции письма ФАУ «ФЦС» от 15.12.2023
/// № Исх-9147; 9.4.3, (131)) и поясов (9.4.7, табл. 23) внецентренно сжатых (сжато-изгибаемых) элементов
/// сплошного сечения. Усилия — канонические оси, кН, кН·м. При изгибе в двух плоскостях тип сечения
/// принимается по моменту Mx. Не реализованы: 9.4.5 (продольное ребро), 9.4.8 (отгибы), 9.4.9 (увеличение
/// пределов при определяющей предельной гибкости). Растянуто-изгибаемые элементы (9.4.10) проверяются как
/// изгибаемые — <see cref="Sp16Section8Local"/>; центрально сжатые — <see cref="Sp16Section7.LocalStability"/>.
/// </summary>
public static class Sp16Section9Local
{
    const double Eps = 1e-9;
    const string WebTitle = "Местная устойчивость стенки внецентренно сжатого элемента";
    const string FlangeTitle = "Местная устойчивость пояса внецентренно сжатого элемента";

    /// <summary>Проверки 9.4.2, 9.4.3 (стенки) и 9.4.7 (пояса) при сжатии с изгибом.</summary>
    public static List<Sp16CheckResult> Check(Sp16Member m, SteelForces f)
    {
        var res = new List<Sp16CheckResult>();
        bool mx = Math.Abs(f.Mx) > Eps, my = Math.Abs(f.My) > Eps;
        if (f.N >= 0 || !mx && !my) return res;
        var s = m.S;
        if (s.Kind is SteelProfileKind.Pipe or SteelProfileKind.Rect or SteelProfileKind.Round) return res;
        if (s.Kind is SteelProfileKind.Generic or SteelProfileKind.Angle)
        {
            res.Add(Sp16CheckResult.NotApplicableFor("9.4.2", "табл. 22", WebTitle, s.Kind == SteelProfileKind.Generic
                ? "профиль не распознан — задайте профиль вручную"
                : "одиночный уголок в табл. 22, 23 не приведён"));
            return res;
        }
        if (m.GoverningPhi() == null)
        {
            res.Add(Sp16CheckResult.NotApplicableFor("9.4.2", "табл. 22", WebTitle, "тип сечения по табл. 7 не определён"));
            return res;
        }
        var ctx = new Ctx(m, f, -f.N, mx);
        res.AddRange(Web(ctx));
        res.AddRange(Flanges(ctx));
        if (mx && my)
            foreach (var r in res) r.Notes.Add("изгиб в двух плоскостях: тип сечения по табл. 22, 23 принят по моменту Mx");
        return res;
    }

    /// <summary>Исходные данные: aboutX — плоскость момента, определяющего тип сечения (Mx, если он есть).</summary>
    sealed record Ctx(Sp16Member M, SteelForces F, double NAbs, bool AboutX)
    {
        public Sp16Section S => M.S;
        public double Moment => AboutX ? F.Mx : F.My;
        public double Sq => Math.Sqrt(S.Mat.Ry / S.Mat.E);
        /// <summary>Сжата сторона y &gt; 0 (x &gt; 0).</summary>
        public bool CompPos => Moment < 0;
        /// <summary>Относительный эксцентриситет m = |M|·A/(N·Wc).</summary>
        public double Rel => Math.Abs(Moment) * S.A / (NAbs * (AboutX ? S.Wx(CompPos) : S.Wy(CompPos)));
        /// <summary>Условная гибкость в плоскости действия момента.</summary>
        public double LambdaBar => M.LambdaBar(AboutX);
        /// <summary>λ̄ для предельных значений центрально сжатых элементов (7.3.2, 7.3.8).</summary>
        public double LambdaBarCentral => M.GoverningPhi()!.Value.LambdaBar;
    }

    // ── Стенки: табл. 22 ─────────────────────────────────────────────────

    static double L125(double lb) => lb < 2 ? 1.3 + 0.15 * lb * lb : Math.Min(1.2 + 0.35 * lb, 3.1);

    /// <summary>(127): λ̄uw2 = 1,42·√(ccr·Ry·γc/(σ1(2 − α + √(α² + 4β²)))) ≤ 0,7 + 2,4α; ccr — табл. 17, β = 0,15ccr·τ/σ1.</summary>
    static double L127(Ctx c, double sigma1, double alpha, double tau)
    {
        double ccr = Sp16Tables.Table17Ccr(alpha), beta = 0.15 * ccr * tau / sigma1;
        double v = 1.42 * Math.Sqrt(ccr * c.S.Mat.Ry * c.M.P.GammaC / (sigma1 * (2 - alpha + Math.Sqrt(alpha * alpha + 4 * beta * beta))));
        return Math.Min(v, 0.7 + 2.4 * alpha);
    }

    /// <summary>Стенка (стенки), параллельная плоскости момента: высота, толщина, границы и τ.</summary>
    sealed record WebGeom(double Hef, double Tw, double Hw, double YUp, double YDown, double Tau, bool Swapped);

    static WebGeom? WebOf(Ctx c)
    {
        var s = c.S;
        if (s.Kind == SteelProfileKind.Box && !c.AboutX)
        {
            // Изгиб в плоскости поясных листов: их роль — «стенки» (bef,1, tf).
            double b = s.BefBoxFlange, t = s.Profile.Tf1;
            return new(b, t, s.Profile.Bf1 - 2 * s.Tw, b / 2, -b / 2, Math.Abs(c.F.Qx) / (2 * t * (s.Profile.Bf1 - 2 * s.Tw)), true);
        }
        if (!c.AboutX) return new(s.Hef, s.Tw, s.Hw, 0, 0, 0, false);
        double gap = (s.Hw - s.Hef) / 2;
        double yUp = s.YTop - s.TfTop - gap, yDown = -(s.YBottom - s.TfBottom - gap);
        if (s.Kind == SteelProfileKind.Tee)
        {
            gap = s.Hw - s.Hef;                                   // закругление — только у полки
            yUp = s.TfTop > 0 ? s.YTop - s.TfTop - gap : s.YTop;
            yDown = s.TfBottom > 0 ? -(s.YBottom - s.TfBottom - gap) : -s.YBottom;
        }
        double tau = Math.Abs(c.F.Qy) / (s.Tw * s.WebCount * s.Hw);
        return new(s.Hef, s.Tw, s.Hw, yUp, yDown, tau, false);
    }

    static List<Sp16CheckResult> Web(Ctx c)
    {
        var res = new List<Sp16CheckResult>();
        var s = c.S; var m = c.M;
        if (s.Kind == SteelProfileKind.Tee && !c.AboutX)
        {
            res.Add(Sp16CheckResult.NotApplicableFor("9.4.2", "табл. 22", WebTitle, "тавр при изгибе из плоскости стенки в табл. 22 не приведён"));
            return res;
        }
        var w = WebOf(c)!;
        double lw = w.Hef / w.Tw * c.Sq, mRel = c.Rel, lb = c.LambdaBar;
        var vars = new List<(string, double)> { (w.Swapped ? "bef,1" : "hef", w.Hef), (w.Swapped ? "tf" : "tw", w.Tw), ("λ̄w", lw),
            ($"m{c.M.Axis(c.AboutX)}", mRel), ($"λ̄{c.M.Axis(c.AboutX)}", lb) };
        var notes = new List<string?>();
        if (w.Swapped) notes.Add("коробчатое сечение, изгиб в плоскости поясных листов: поясные листы проверены как стенки (тип 1)");

        // σ1, σ2 у расчётных границ стенки (сжатие «+», без φe, φexy, c).
        double iAx = c.AboutX ? s.Ix : s.Iy, sN = c.NAbs / s.A;
        double sUp = sN - c.Moment * w.YUp / iAx, sDown = sN - c.Moment * w.YDown / iAx;
        double s1 = Math.Max(sUp, sDown), s2 = Math.Min(sUp, sDown);
        double alpha = s1 > 0 ? (s1 - s2) / s1 : 0;

        double central() => Sp16Tables.WebLimitCentral(s,
            s.Kind == SteelProfileKind.Box ? m.LambdaBar(w.Swapped ? false : true) : c.LambdaBarCentral, out _)!.Value;

        double luw;
        string formula, clause = "9.4.2";
        int type;
        if (!c.AboutX && s.Kind is SteelProfileKind.IBeam or SteelProfileKind.Channel)
        {
            // Тип 5: изгиб в плоскости полок.
            type = 5;
            double l130 = Math.Min(2 * Math.Sqrt(s.A * s.Mat.Ry * m.P.GammaC / c.NAbs), 5.5);
            if (mRel >= 1) { luw = l130; formula = "(130)"; }
            else
            {
                double lc = central();
                luw = lc + (l130 - lc) * mRel; formula = "(130), прим. 4";
                notes.Add($"прим. 4 табл. 22: 0 < my < 1 — интерполяция между λ̄uw = {lc:0.###} (7.3.2, my = 0) и {l130:0.###} ((130), my = 1)");
            }
            vars.Add(("λ̄uw (130)", l130));
        }
        else if (s.Kind == SteelProfileKind.Tee)
        {
            // Тип 4: тавр, эксцентриситет в сторону полки.
            bool flangeTop = s.TfTop > 0;
            if (flangeTop != c.CompPos)
            {
                res.Add(Sp16CheckResult.NotApplicableFor("9.4.2", "табл. 22", WebTitle,
                    "тавр с эксцентриситетом в сторону стенки (сжат конец стенки) в табл. 22 не приведён"));
                return res;
            }
            type = 4;
            double lbc = Math.Clamp(lb, 0.8, 4), ratio = s.Profile.Bf1 / s.Hef;
            if (lbc != lb) notes.Add($"прим. 3 табл. 22: λ̄x = {lb:0.###} — принято {lbc:0.#}");
            if (ratio < 1 || ratio > 2)
            {
                notes.Add($"условие 1 ≤ bf/hef ≤ 2 не выполнено (bf/hef = {ratio:0.###}) — отношение ограничено этим диапазоном");
                ratio = Math.Clamp(ratio, 1, 2);
            }
            luw = (0.4 + 0.07 * lbc) * (1 + 0.25 * Math.Sqrt(2 - ratio)); formula = "(129)";
            vars.Add(("bf/hef", ratio));
        }
        else
        {
            // Двутавр: тип 1 при cφy > φe, иначе тип 2; короб — тип 1; швеллер — тип 3.
            type = s.Kind switch { SteelProfileKind.Channel => 3, SteelProfileKind.Box => 1, _ => 0 };
            PhiEResult? pe = null;
            if (s.Kind == SteelProfileKind.IBeam || type == 1)
                pe = Sp16Section9.PhiE(m, c.NAbs, c.Moment, c.AboutX);
            if (type == 0)
            {
                var cc = Sp16Section9.CoefficientC(m, c.NAbs, c.Moment);
                bool ok = cc.NotApplicable == null && cc.Failure == null && pe is { NotApplicable: null, Failure: null };
                if (ok)
                {
                    double cphi = cc.C * cc.PhiY;
                    type = cphi > pe!.PhiE ? 1 : 2;
                    vars.AddRange([("c", cc.C), ("φy", cc.PhiY), ("φe", pe.PhiE)]);
                    notes.Add($"cφy = {cphi:0.###} {(type == 1 ? ">" : "≤")} φe = {pe.PhiE:0.###} — тип {type} табл. 22");
                }
                else
                {
                    type = 1;
                    notes.Add("cφy или φe не определены — принят тип 1 табл. 22");
                }
            }
            vars.AddRange([("σ1", s1), ("σ2", s2), ("α", alpha), ("τ", w.Tau)]);
            if (s1 <= 0)
            {
                res.Add(Sp16CheckResult.NotApplicableFor("9.4.2", "табл. 22", WebTitle, "стенка не сжата (σ1 ≤ 0) — проверка не требуется"));
                return res;
            }
            double aC = Math.Min(alpha, 2);
            if (alpha > 2) notes.Add($"α = {alpha:0.###} > 2 — в (127) принято α = 2");

            if (type == 1)
            {
                double l1 = L125(lb);
                string f1 = lb < 2 ? "(125)" : "(126)";
                vars.Add(("λ̄uw1", l1));
                if (mRel < 1)
                {
                    double lc = central();
                    luw = lc + (l1 - lc) * mRel; formula = f1 + ", прим. 1";
                    notes.Add($"прим. 1 табл. 22: 0 < mx < 1 — интерполяция между λ̄uw = {lc:0.###} (7.3.2, mx = 0) и {l1:0.###} (mx = 1)");
                }
                else if (mRel <= 10)
                {
                    luw = l1; formula = f1;
                    // 9.4.3: увеличение при 0,8 ≤ N/(φe·A·Ry·γc) ≤ 1 (λ̄uw2 — по (127)).
                    if (pe is { NotApplicable: null, Failure: null } && alpha >= 1)
                    {
                        double ratio = c.NAbs / (pe.PhiE * s.A * s.Mat.Ry * m.P.GammaC);
                        double l2 = L127(c, s1, aC, w.Tau);
                        vars.AddRange([("N/(φeARyγc)", ratio), ("λ̄uw2", l2)]);
                        if (ratio < 0.8) { luw = l2; formula = "(127)"; clause = "9.4.3"; notes.Add("9.4.3: N/(φeARyγc) < 0,8 — λ̄uw = λ̄uw2"); }
                        else if (ratio <= 1) { luw = l1 + 5 * (l2 - l1) * (1 - ratio); formula = "(131)"; clause = "9.4.3"; }
                    }
                    else notes.Add("9.4.3 не применён: " + (alpha < 1 ? $"α = {alpha:0.###} < 1 — (127) не применима" : "φe не определён"));
                }
                else if (mRel <= 20)
                {
                    var (l851, n851) = Sp16Section8Local.WebLimit851(m, 0);
                    luw = l1 + (l851 - l1) * (mRel - 10) / 10; formula = f1 + ", прим. 1";
                    notes.Add($"прим. 1 табл. 22: 10 < mx ≤ 20 — интерполяция между {l1:0.###} (mx = 10) и {l851:0.#} (8.5.1, mx = 20; {n851})");
                }
                else
                {
                    res.Add(Sp16CheckResult.NotApplicableFor("9.4.2", "табл. 22", WebTitle,
                        $"mx = {mRel:0.##} > 20 — устойчивость стенки проверяется как у изгибаемого элемента (8.5)"));
                    return res;
                }
            }
            else
            {
                // Типы 2 и 3; при α < 1 — прим. 2 (для типа 3 — по аналогии, см. 9.4.6).
                double Lt(double a) => type == 2 ? L127(c, s1, a, w.Tau) : Math.Min(0.75 * L127(c, s1, a, w.Tau), 0.52 + 1.8 * a);
                string ft = type == 2 ? "(127)" : "(128)";
                if (alpha >= 1) { luw = Lt(aC); formula = ft; }
                else
                {
                    double lc = central();
                    double l05 = type == 2 ? Math.Min(lc, L125(lb)) : lc;
                    if (alpha <= 0.5) { luw = l05; formula = ft + ", прим. 2"; }
                    else { double l1 = Lt(1); luw = l05 + (l1 - l05) * (alpha - 0.5) / 0.5; formula = ft + ", прим. 2"; vars.Add(("λ̄uw (α = 1)", l1)); }
                    vars.Add(("λ̄uw (α ≤ 0,5)", l05));
                    notes.Add(type == 2
                        ? "прим. 2 табл. 22: при α ≤ 0,5 λ̄uw определено дважды — по 7.3.2 и по (125), (126); принято меньшее"
                        : "тип 3: табл. 22 установлена при 1 ≤ α ≤ 2; при α < 1 принято по прим. 2 (как для типа 2, 9.4.6): α ≤ 0,5 — по 7.3.2");
                    if (alpha > 0.5) notes.Add("0,5 < α < 1 — интерполяция между значениями при α = 0,5 и α = 1");
                }
            }
        }
        vars.Add(("λ̄uw", luw));
        notes.Insert(0, $"тип {type} табл. 22");
        if (lw >= 2.3) notes.Add("9.4.4: при λ̄w ≥ 2,3 стенку следует укреплять поперечными рёбрами жёсткости по 7.3.3");
        if (lw > luw && type <= 3)
            notes.Add("9.4.6: при λ̄w > λ̄uw проверки устойчивости по (109), (115), (116) выполняются с уменьшенной площадью Ad (7.3.6) — не реализовано");
        res.Add(Sp16CheckResult.Limit(clause, "табл. 22 " + formula, WebTitle, lw, luw, vars, notes));
        return res;
    }

    // ── Пояса: табл. 23 ──────────────────────────────────────────────────

    static List<Sp16CheckResult> Flanges(Ctx c)
    {
        var res = new List<Sp16CheckResult>();
        var s = c.S; var m = c.M;
        double lb = c.LambdaBar, lbc = Math.Clamp(lb, 0.8, 4), mRel = c.Rel;
        string ax = m.Axis(c.AboutX);
        string? clampNote = lbc != lb ? $"прим. 2 табл. 23: λ̄{ax} = {lb:0.###} — принято {lbc:0.#}" : null;

        if (s.Kind == SteelProfileKind.Box)
        {
            // Тип 2: поясной лист, перпендикулярный плоскости момента (при My — стенки).
            bool swapped = !c.AboutX;
            double b = swapped ? s.Hef : s.BefBoxFlange, t = swapped ? s.Tw : s.Profile.Tf1;
            double lufc = Sp16Tables.WebLimitCentral(s, m.LambdaBar(swapped), out _)!.Value;
            var r = ByM(c, "(133)", lufc - 0.01 * (5.3 + 1.3 * lbc) * Math.Min(mRel, 5), lufc, b, t, 1.5, true,
                swapped ? "поясной лист — стенка короба (изгиб в плоскости поясных листов)" : "поясной лист", clampNote);
            if (r != null) res.Add(r);
            return res;
        }
        if (s.Kind == SteelProfileKind.Channel || !c.AboutX)
        {
            // Тип 3 (швеллер, Mx) и тип 4 (двутавр, швеллер — My): предел не зависит от m.
            if (s.Kind == SteelProfileKind.Tee)
            {
                res.Add(Sp16CheckResult.NotApplicableFor("9.4.7", "табл. 23", FlangeTitle, "тавр при изгибе из плоскости стенки в табл. 23 не приведён"));
                return res;
            }
            double luf = 0.36 + 0.10 * lbc;
            string formula = c.AboutX ? "(134)" : "(135)";
            foreach (var (name, bef, tf) in FlangeList(s))
            {
                double lf = bef / tf * c.Sq;
                res.Add(Sp16CheckResult.Limit("9.4.7", "табл. 23 " + formula, FlangeTitle + name, lf, luf,
                    [("bef", bef), ("tf", tf), ("λ̄f", lf), ($"λ̄{ax}", lb), ("λ̄uf", luf)],
                    [$"тип {(c.AboutX ? 3 : 4)} табл. 23", clampNote]));
            }
            return res;
        }

        // Тип 1: двутавр, тавр — изгиб в плоскости стенки; сжатый (со стороны эксцентриситета) пояс.
        double lufc0 = Sp16Tables.FlangeLimitCentral(s, c.LambdaBarCentral)!.Value;
        bool top = c.CompPos;
        double bef1 = top ? s.BefTop : s.BefBottom, tf1 = top ? s.TfTop : s.TfBottom;
        if (tf1 > 0)
        {
            var r = ByM(c, "(132)", lufc0 - 0.01 * (1.5 + 0.7 * lbc) * Math.Min(mRel, 5), lufc0, bef1, tf1, 0.5, false,
                s.SymmetricAboutX ? "свес пояса" : (top ? "верхний пояс" : "нижний пояс"), clampNote);
            if (r != null) res.Add(r);
        }
        // Второй пояс (асимметричный двутавр, тавр с полкой на растянутой стороне): как у центрально сжатого, если сжат.
        double befO = top ? s.BefBottom : s.BefTop, tfO = top ? s.TfBottom : s.TfTop;
        if (tfO > 0 && !s.SymmetricAboutX)
        {
            double y = top ? -s.YBottom : s.YTop;
            double sigma = c.NAbs / s.A - c.Moment * y / s.Ix;
            if (sigma > 0)
            {
                double lf = befO / tfO * c.Sq;
                res.Add(Sp16CheckResult.Limit("9.4.7", "табл. 10", FlangeTitle + (top ? " (нижний пояс)" : " (верхний пояс)"), lf, lufc0,
                    [("bef", befO), ("tf", tfO), ("λ̄f", lf), ("σ", sigma), ("λ̄uf", lufc0)],
                    ["пояс на стороне, противоположной эксцентриситету, в табл. 23 не приведён; его сжатие меньше среднего — принят предел центрально сжатого элемента λ̄ufc (7.3.8)"]));
            }
        }
        return res;
    }

    /// <summary>Свесы поясов двутавра (верхний, нижний — если отличается) и швеллера.</summary>
    static List<(string Name, double Bef, double Tf)> FlangeList(Sp16Section s)
    {
        var l = new List<(string, double, double)>();
        bool differ = s.Kind == SteelProfileKind.IBeam && !s.SymmetricAboutX;
        if (s.TfTop > 0) l.Add((differ ? " (верхний пояс)" : "", s.BefTop, s.TfTop));
        if (differ && s.TfBottom > 0) l.Add((" (нижний пояс)", s.BefBottom, s.TfBottom));
        return l;
    }

    /// <summary>
    /// Типы 1 и 2 табл. 23: при m ≤ 5 — формула; при 5 &lt; m ≤ 20 — интерполяция с 8.5.18 ((97) — k = 0,5,
    /// (98) — k = 1,5) при m = 20 (прим. 1); при m &gt; 20 — как изгибаемый элемент.
    /// </summary>
    static Sp16CheckResult? ByM(Ctx c, string formula, double lu5, double lufc, double b, double t, double k851, bool box, string name, string? clampNote)
    {
        var s = c.S; var m = c.M;
        double mRel = c.Rel, lf = b / t * c.Sq;
        string desc = $"{FlangeTitle} ({name})";
        string lfName = box ? "λ̄f1" : "λ̄f", luName = box ? "λ̄uf,1" : "λ̄uf";
        if (mRel > 20)
            return Sp16CheckResult.NotApplicableFor("9.4.7", "табл. 23 " + formula, desc,
                $"m = {mRel:0.##} > 20 — устойчивость пояса проверяется как у изгибаемого элемента (8.5.18)");
        var vars = new List<(string, double)> { (box ? "bef,1" : "bef", b), ("tf", t), (lfName, lf), ($"m{m.Axis(c.AboutX)}", mRel),
            ($"λ̄{m.Axis(c.AboutX)}", c.LambdaBar), (box ? "λ̄uf,1c" : "λ̄ufc", lufc) };
        var notes = new List<string?> { $"тип {(box ? 2 : 1)} табл. 23", clampNote };
        double lu = lu5;
        if (mRel > 5)
        {
            double wc = c.AboutX ? s.Wx(c.CompPos) : s.Wy(c.CompPos);
            double sc = Math.Min((c.NAbs / s.A + Math.Abs(c.Moment) / wc) / m.P.GammaC, s.Mat.Ry);
            double l20 = k851 * Math.Sqrt(s.Mat.Ry / sc);
            lu = lu5 + (l20 - lu5) * (mRel - 5) / 15;
            vars.AddRange([($"{luName} (m = 5)", lu5), ("σc", sc), ($"{luName} (8.5.18)", l20)]);
            notes.Add($"прим. 1 табл. 23: 5 < m ≤ 20 — интерполяция между {formula} (m = 5) и 8.5.18 {(box ? "(98)" : "(97)")} (m = 20); σc = (N/A + M/Wc)/γc ≤ Ry");
        }
        vars.Add((luName, lu));
        return Sp16CheckResult.Limit("9.4.7", "табл. 23 " + formula, desc, lf, lu, vars, notes);
    }
}
