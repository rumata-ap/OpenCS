namespace CScore.Sp16;

/// <summary>
/// φe для плоскости действия момента по 9.2.2 (табл. Д.2, Д.3, Д.5, табл. 20). Если
/// <see cref="NotApplicable"/> или <see cref="Failure"/> не null — φe не определён.
/// </summary>
public sealed class PhiEResult
{
    /// <summary>Коэффициент устойчивости при сжатии с изгибом φe (с ограничением φe ≤ φ).</summary>
    public double PhiE { get; init; } = double.NaN;
    /// <summary>Приведённый относительный эксцентриситет mef.</summary>
    public double Mef { get; init; } = double.NaN;
    /// <summary>Относительный эксцентриситет m = eA/Wc (для Д.5 — mef,1/η).</summary>
    public double M { get; init; } = double.NaN;
    /// <summary>Коэффициент влияния формы сечения η.</summary>
    public double Eta { get; init; } = double.NaN;
    /// <summary>Переменные расчёта.</summary>
    public List<(string Name, double Value)> Vars { get; init; } = [];
    /// <summary>Примечания.</summary>
    public List<string?> Notes { get; init; } = [];
    /// <summary>Причина, по которой расчёт по (109) не выполняется (другой пункт нормы).</summary>
    public string? NotApplicable { get; init; }
    /// <summary>Причина, по которой φe не может быть определён (вне таблиц нормы).</summary>
    public string? Failure { get; init; }
}

/// <summary>
/// Раздел 9 СП 16 (сплошные сечения): прочность 9.1.1 (105)/(106), 9.1.3 (107)/(108);
/// устойчивость в плоскости действия момента 9.2.2 (109), (110) с табл. Д.2, Д.3, Д.5 и табл. 20 (9.2.3);
/// устойчивость из плоскости действия момента 9.2.4–9.2.6 (111)–(114), табл. 21, cmax (Д.1), (Д.2); 9.2.8 (115).
/// Усилия — канонические оси, кН, кН·м; N &gt; 0 — растяжение.
/// </summary>
public static partial class Sp16Section9
{
    const double Eps = 1e-9;

    // ── 9.1 Прочность ────────────────────────────────────────────────────

    /// <summary>Прочность при сжатии (растяжении) с изгибом: (105) либо (106); для сжатия и Ryn &gt; 440 — (107).</summary>
    public static List<Sp16CheckResult> Strength(Sp16Member m, SteelForces f)
    {
        var res = new List<Sp16CheckResult>();
        string? reason = Reason105(m, f);
        if (m.P.AllowPlastic && reason == null)
            res.Add(Formula105(m, f));
        else
        {
            if (m.P.AllowPlastic)
                res.Add(Sp16CheckResult.NotApplicableFor("9.1.1", "(105)", "Прочность с учётом пластических деформаций",
                    reason + " — выполнен расчёт по (106)"));
            res.Add(Formula106(m, f));
        }
        res.AddRange(Formula107(m, f));
        return res;
    }

    /// <summary>Причина неприменимости (105); null — применима.</summary>
    internal static string? Reason105(Sp16Member m, SteelForces f)
    {
        var s = m.S;
        if (m.P.DynamicLoad) return "элемент подвергается непосредственному воздействию динамических нагрузок";
        if (s.Mat.RynOrRy > 440000) return "(105) — только для сталей с Ryn ≤ 440 Н/мм²";
        if (Sp16Tables.TableE1(s) == null) return "сечение не приведено в табл. Е.1";
        double tau = MaxTau(s, f);
        if (tau >= 0.5 * s.Mat.Rs) return $"τ = {tau:0} кПа ≥ 0,5Rs = {0.5 * s.Mat.Rs:0} кПа";
        return null;
    }

    /// <summary>Наибольшее касательное напряжение τ = QS/(It) на центральных осях от Qx и Qy, кПа.</summary>
    static double MaxTau(Sp16Section s, SteelForces f)
    {
        double tau = 0;
        foreach (bool aboutX in new[] { true, false })
        {
            double q = aboutX ? f.Qy : f.Qx;
            if (Math.Abs(q) <= Eps) continue;
            var (sStat, t) = s.ShearAtCentroid(aboutX);
            tau = Math.Max(tau, Math.Abs(q) * sStat / ((aboutX ? s.Ix : s.Iy) * t));
        }
        return tau;
    }

    /// <summary>9.1.1, формула (105) с n, cx, cy по табл. Е.1 (бимомент не учитывается).</summary>
    static Sp16CheckResult Formula105(Sp16Member m, SteelForces f)
    {
        var s = m.S;
        var e1 = Sp16Tables.TableE1(s)!;
        bool mx = Math.Abs(f.Mx) > Eps, my = Math.Abs(f.My) > Eps;
        double n = mx && my ? e1.NBoth : my ? e1.NOnlyMy : e1.NOnlyMx;
        double cx = Sp16Tables.CapPlastic(e1.Cx, m.P.GammaFEq), cy = Sp16Tables.CapPlastic(e1.Cy, m.P.GammaFEq);
        double rg = s.Mat.Ry * m.P.GammaC, an = s.A * m.P.NetAreaRatio;
        double sigma = Math.Abs(f.N) / an;
        double u = Math.Pow(sigma / rg, n) + Math.Abs(f.Mx) / (cx * s.WxMin * rg) + Math.Abs(f.My) / (cy * s.WyMin * rg);
        return Sp16CheckResult.Of("9.1.1", "(105)", "Прочность при сжатии (растяжении) с изгибом с учётом пластических деформаций", u,
            [("N", f.N), ("Mx", f.Mx), ("My", f.My), ("An", an), ("n", n), ("cx", cx), ("cy", cy), ("Wxn,min", s.WxMin), ("Wyn,min", s.WyMin), ("Ry", s.Mat.Ry), ("γc", m.P.GammaC)],
            notes:
            [
                $"табл. Е.1: {e1.Note}",
                cx < e1.Cx || cy < e1.Cy ? $"прим. 2 табл. Е.1: c ≤ 1,15γf = {1.15 * m.P.GammaFEq:0.###}" : null,
                sigma <= 0.1 * s.Mat.Ry ? "σ = N/An ≤ 0,1Ry: формула (105) применяется при выполнении требований 8.5.8 и 8.5.18" : null,
                "бимомент B не учитывается (вне объёма); ослабление сечения в Wn,min не учитывается",
            ]);
    }

    /// <summary>9.1.1, формула (106): наибольшее |N/An ± Mx·y/Ixn ± My·x/Iyn| в вершинах контура.</summary>
    static Sp16CheckResult Formula106(Sp16Member m, SteelForces f)
    {
        var s = m.S;
        double an = s.A * m.P.NetAreaRatio, rg = s.Mat.Ry * m.P.GammaC;
        double best = 0, bx = 0, by = 0;
        foreach (var (px, py) in s.Poly.Outer)
        {
            double x = px - s.Poly.Xc, y = py - s.Poly.Yc;
            double sg = f.N / an + Sp16Section8Strength.Sigma(s, f with { N = 0 }, x, y);
            if (Math.Abs(sg) > Math.Abs(best)) { best = sg; bx = x; by = y; }
        }
        return Sp16CheckResult.Of("9.1.1", "(106)", "Прочность при сжатии (растяжении) с изгибом", Math.Abs(best) / rg,
            [("N", f.N), ("Mx", f.Mx), ("My", f.My), ("An", an), ("x", bx), ("y", by), ("σ", best), ("Ry", s.Mat.Ry), ("γc", m.P.GammaC)],
            Math.Abs(best), rg,
            [
                Math.Abs(s.Poly.Ixy) > 1e-6 * Math.Max(s.Ix, s.Iy) ? "оси сечения не главные — напряжение вычислено с учётом Ixy" : null,
                "бимомент B не учитывается (вне объёма); ослабление сечения в моментах инерции не учитывается",
            ]);
    }

    /// <summary>
    /// 9.1.3, формулы (107), (108): сжатые с изгибом элементы из стали с Ryn &gt; 440 Н/мм² с сечением,
    /// несимметричным относительно оси, перпендикулярной к плоскости изгиба, — прочность растянутого волокна.
    /// </summary>
    static List<Sp16CheckResult> Formula107(Sp16Member m, SteelForces f)
    {
        var res = new List<Sp16CheckResult>();
        var s = m.S;
        if (f.N >= 0 || s.Mat.RynOrRy <= 440000) return res;
        double nAbs = -f.N, an = s.A * m.P.NetAreaRatio;
        foreach (bool aboutX in new[] { true, false })
        {
            double mom = aboutX ? f.Mx : f.My;
            if (Math.Abs(mom) <= Eps || (aboutX ? s.SymmetricAboutX : s.SymmetricAboutY)) continue;
            bool tensionPositive = mom > 0;
            double wt = aboutX ? s.Wx(tensionPositive) : s.Wy(tensionPositive);
            double lb = m.LambdaBar(aboutX);
            double delta = 1 - 0.1 * nAbs * lb * lb / (s.A * s.Mat.Ry);
            string title = $"Прочность растянутого волокна при сжатии с изгибом (ось {m.Axis(aboutX)})";
            var vars = new List<(string, double)> { ("N", f.N), ("M", mom), ("An", an), ("Wtn", wt), ("λ̄", lb), ("δ", delta), ("Ru", s.Mat.Ru), ("γu", Sp16Section7.GammaU), ("γc", m.P.GammaC) };
            string? rynNote = s.Mat.Ryn == null ? "нормативное сопротивление Ryn не задано — условие Ryn > 440 Н/мм² проверено по Ry" : null;
            if (delta <= 0)
            {
                res.Add(new Sp16CheckResult
                {
                    Clause = "9.1.3", Formula = "(107)", Description = title, Utilization = double.PositiveInfinity, Status = CheckStatus.Fail,
                    Variables = vars.Select(v => new KeyValuePair<string, double>(v.Item1, v.Item2)).ToList(),
                    Notes = [$"δ = {delta:0.###} ≤ 0 по (108) — сжимающая сила превышает допустимую для гибкости элемента"],
                });
                continue;
            }
            double u = Sp16Section7.GammaU / (s.Mat.Ru * m.P.GammaC) * Math.Abs(nAbs / an - Math.Abs(mom) / (delta * wt));
            res.Add(Sp16CheckResult.Of("9.1.3", "(107)", title, u, vars, notes: [rynNote, "δ по (108), N — со знаком «+»"]));
        }
        return res;
    }

    // ── 9.2.2 Устойчивость в плоскости действия момента ──────────────────

    /// <summary>
    /// 9.2.2, формула (109): N/(φe·A·Ry·γc) ≤ 1 при сжатии с изгибом в одной из главных плоскостей,
    /// совпадающей с плоскостью симметрии (в т. ч. 9.2.8 — изгиб в плоскости наименьшей жёсткости).
    /// </summary>
    public static List<Sp16CheckResult> InPlaneStability(Sp16Member m, SteelForces f)
    {
        var res = new List<Sp16CheckResult>();
        bool mx = Math.Abs(f.Mx) > Eps, my = Math.Abs(f.My) > Eps;
        if (f.N >= 0 || !mx && !my) return res;
        const string title = "Устойчивость в плоскости действия момента";
        if (mx && my)
        {
            res.Add(Sp16CheckResult.NotApplicableFor("9.2.2", "(109)", title,
                "сжатие с изгибом в двух главных плоскостях — расчёт по 9.2.9 (коробчатое сечение — 9.2.10)"));
            return res;
        }
        bool aboutX = mx;
        string desc = $"{title} (изгиб относительно оси {m.Axis(aboutX)})";
        double nAbs = -f.N;
        var pe = PhiE(m, nAbs, aboutX ? f.Mx : f.My, aboutX);
        if (pe.NotApplicable != null) { res.Add(Sp16CheckResult.NotApplicableFor("9.2.2", "(109)", desc, pe.NotApplicable)); return res; }
        if (pe.Failure != null)
        {
            res.Add(new Sp16CheckResult
            {
                Clause = "9.2.2", Formula = "(109)", Description = desc, Utilization = double.PositiveInfinity, Status = CheckStatus.Fail,
                Variables = pe.Vars.Select(v => new KeyValuePair<string, double>(v.Name, v.Value)).ToList(),
                Notes = [pe.Failure],
            });
            return res;
        }
        var s = m.S;
        double cap = pe.PhiE * s.A * s.Mat.Ry * m.P.GammaC;
        var notes = new List<string?>(pe.Notes);
        if (!aboutX && s.Iy < s.Ix) notes.Add("9.2.8: изгиб в плоскости наименьшей жёсткости — расчёт по (109)");
        res.Add(Sp16CheckResult.Of("9.2.2", "(109)", desc, nAbs / cap,
            [.. pe.Vars, ("A", s.A), ("Ry", s.Mat.Ry), ("γc", m.P.GammaC)], nAbs, cap, notes));
        return res;
    }

    /// <summary>
    /// φe по 9.2.2 для сжатия силой nAbs (кН, &gt; 0) с моментом moment (кН·м, со знаком) относительно
    /// канонической оси x (aboutX) или y. Расчётный момент — по 9.2.3: табл. Д.5 (двоякосимметричное
    /// сечение, эпюра от концевых моментов), табл. 20 (одна ось симметрии в плоскости изгиба, шарнирные
    /// концы), иначе — заданный момент.
    /// </summary>
    public static PhiEResult PhiE(Sp16Member m, double nAbs, double moment, bool aboutX)
    {
        var s = m.S; var p = m.P;
        var notes = new List<string?>();
        bool symPlane = aboutX ? s.SymmetricAboutY : s.SymmetricAboutX;
        if (!symPlane && p.EtaOverride == null)
            return new() { NotApplicable = s.Kind == SteelProfileKind.Generic
                ? "профиль не распознан — задайте профиль или коэффициент η вручную"
                : "плоскость действия момента не совпадает с плоскостью симметрии сечения — 9.2.2 не распространяется" };
        if (!symPlane) notes.Add("совпадение плоскости момента с плоскостью симметрии не проверено: η задан вручную");

        bool compPos = moment < 0;                       // M > 0 растягивает сторону y > 0 (x > 0)
        double wc = aboutX ? s.Wx(compPos) : s.Wy(compPos);
        double lb = m.LambdaBar(aboutX), a = s.A, mAbs = Math.Abs(moment);
        double Rel(double mom) => mom * a / (nAbs * wc);
        EtaValue EtaAt(double mr) => p.EtaOverride is { } e
            ? new EtaValue(e, null, null, "η задан вручную", null)
            : Sp16Tables.Eta(s, aboutX, compPos, mr, lb);

        var vars = new List<(string, double)> { ("N", -nAbs), ("M", moment), ("Wc", wc), ("λ̄", lb) };
        bool doubly = s.SymmetricAboutX && s.SymmetricAboutY;
        bool pinned = p.MomentShape is MomentShape.LinearEndMoments or MomentShape.PinnedTransverse;
        double mDesign = mAbs, mRel, mef;
        EtaValue eta;

        if (doubly && p.MomentShape == MomentShape.LinearEndMoments)
        {
            mRel = Rel(mAbs);
            eta = EtaAt(mRel);
            if (eta.Eta == null) return new() { Vars = vars, NotApplicable = eta.Reason };
            double mef1 = eta.Eta.Value * mRel;
            var d5 = Sp16Tables.MefD5(p.EndMomentRatio, lb, mef1, out var d5Note);
            vars.AddRange([("δ", p.EndMomentRatio), ("m", mRel), ("η", eta.Eta.Value), ("mef,1", mef1)]);
            if (d5 == null)
                return new() { Vars = vars, NotApplicable = $"mef,1 = {mef1:0.##} > 20 — расчёт как изгибаемого элемента (раздел 8)" };
            mef = d5.Value;
            notes.Add("шарнирно опёртый стержень двоякосимметричного сечения: mef по табл. Д.5, mef,1 = η·M1·A/(N·Wc)");
            notes.Add(d5Note);
        }
        else
        {
            if (!doubly && pinned)
            {
                double ratio1 = p.MomentShape == MomentShape.LinearEndMoments ? (2 + p.EndMomentRatio) / 3 : p.MiddleThirdMomentRatio;
                double m1 = Math.Max(ratio1, 0.5) * mAbs, mMaxRel = Rel(mAbs);
                mDesign = Sp16Tables.MomentTable20(mAbs, m1, mMaxRel, lb);
                vars.AddRange([("Mmax", mAbs), ("M1", m1), ("mmax", mMaxRel), ("M (табл. 20)", mDesign)]);
                notes.Add("шарнирно опёртый стержень с одной осью симметрии в плоскости изгиба: M по табл. 20 (9.2.3)"
                          + (p.MomentShape == MomentShape.LinearEndMoments ? $"; M1 — момент на трети длины, δ = {p.EndMomentRatio:0.##}" : ""));
            }
            else if (doubly && p.MomentShape == MomentShape.PinnedTransverse)
                notes.Add("табл. Д.5 — только для эпюры от концевых моментов: M принят равным расчётному");
            mRel = Rel(mDesign);
            eta = EtaAt(mRel);
            if (eta.Eta == null) return new() { Vars = vars, NotApplicable = eta.Reason };
            mef = eta.Eta.Value * mRel;
            vars.AddRange([("m", mRel), ("η", eta.Eta.Value), ("mef", mef)]);
        }
        if (eta.Type is { } type) notes.Add($"η — тип сечения {type} табл. Д.2" + (eta.AfAw is { } r ? $", Af/Aw = {r:0.###}" : ""));
        notes.Add(eta.Note);
        if (mef > 20)
            return new() { Vars = vars, M = mRel, Eta = eta.Eta.Value, Mef = mef, NotApplicable = $"mef = {mef:0.##} > 20 — расчёт как изгибаемого элемента (раздел 8)" };

        var phiE = Sp16Tables.PhiE(lb, mef);
        if (phiE == null)
            return new() { Vars = vars, M = mRel, Eta = eta.Eta.Value, Mef = mef,
                Failure = $"λ̄ = {lb:0.###}, mef = {mef:0.###} — вне табл. Д.3 (значение не установлено)" };
        double pe = phiE.Value;
        if (lb < 0.5 || mef < 0.1) notes.Add("λ̄ < 0,5 или mef < 0,1 — φe принят по первой строке/столбцу табл. Д.3");
        if (m.Phi(aboutX) is { } phi)
        {
            vars.Add(("φ", phi));
            if (pe > phi) { notes.Add($"φe = {pe:0.###} по табл. Д.3 ограничен значением φ (прим. 2 табл. Д.3)"); pe = phi; }
        }
        vars.Add(("φe", pe));
        return new() { PhiE = pe, M = mRel, Eta = eta.Eta.Value, Mef = mef, Vars = vars, Notes = notes };
    }
}
