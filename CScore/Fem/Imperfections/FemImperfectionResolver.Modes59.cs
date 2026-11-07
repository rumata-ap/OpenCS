using System.Globalization;
using CScore.En1993;
using CScore.Sp16;

namespace CScore.Fem.Imperfections;

/// <summary>
/// Амплитуда формы потери устойчивости по EN 1993-1-1 п. 5.3.2(11), ф. (5.9)–(5.11) (подсрез 5б плана):
/// α_ult,k = min N_Rk/|N_Ed| по сжатым стальным стержням (N_Ed — продольные силы задачи формы, моменты не учитываются —
/// прим. 1 DIN), критическое сечение — где достигается α_ult,k (тогда обе формы (5.9) равны), при равенстве (± 1 %) —
/// с наибольшим |M|/M_el,Rk формы; λ̄ = √(α_ult,k/α_cr), α_cr — λ формы; χ — (6.49); e₀ — (5.10) с M_Rk = M_el,Rk
/// = W_el,min·R_yn; η_init = e₀·N_cr/|EI·η″| (5.9), N_cr = α_cr·|N_Ed,крит|, EI·η″ — упругий момент формы в точке.
/// </summary>
public static partial class FemImperfectionResolver
{
    /// <summary>Сечения с α_ult,k в пределах этой доли от наименьшего считаются равными (выбор по моменту формы).</summary>
    const double AlphaUltTie = 0.01;

    /// <summary>Доля наибольшего |M| формы, ниже которой у критической точки — предупреждение.</summary>
    const double CurvatureSmall = 0.1;

    /// <summary>Сечение стального стержня для (5.9): A, м²; W_el,min относительно осей контура x/y, м³; R_yn, кПа.</summary>
    sealed record SteelProps59(CrossSection Section, double A, double WxMin, double WyMin, double RynKPa)
    {
        public double NRk => A * RynKPa * 1000;
        public double MRk(bool aboutContourX) => (aboutContourX ? WxMin : WyMin) * RynKPa * 1000;
    }

    static bool ValidModes59(string label, FemModesParams p, Action<string, string> error)
    {
        if (p.GammaM1 is double g && !(g > 0 && double.IsFinite(g)))
        {
            error("imperfection_modes_gamma", $"Формы «{label}»: γ_M1 должен быть положительным числом.");
            return false;
        }
        if (p.Curve != null && En1993Imperfections.ParseCurve(p.Curve) == null)
        {
            error("imperfection_modes_curve", $"Формы «{label}»: неизвестная кривая «{p.Curve}» (a0, a, b, c, d).");
            return false;
        }
        return true;
    }

    /// <summary>Свойства одной стальной области сечения; null — не одна стальная область или нет контура.</summary>
    static SteelProps59? Steel59(CrossSection? cs)
    {
        if (cs == null || cs.Areas.Count != 1 || cs.Areas[0].Material is not { Type: MatType.Steel or MatType.Custom } material)
            return null;
        var area = cs.Areas[0];
        if (area.Hull?.Points is not { Count: >= 3 } hull) return null;
        var poly = new PolygonSection(hull.Select(q => (q.X, q.Y)), area.Holes.Select(h => h.Points.Select(q => (q.X, q.Y))));
        double ry = SteelMaterialProps.FromMaterial(material).RynOrRy;
        double cy = Math.Max(poly.YMax - poly.Yc, poly.Yc - poly.YMin), cx = Math.Max(poly.XMax - poly.Xc, poly.Xc - poly.XMin);
        if (!(poly.A > 0) || !(ry > 0) || !(cy > 0) || !(cx > 0)) return null;
        return new SteelProps59(cs, poly.A, poly.Ix / cy, poly.Iy / cx, ry);
    }

    /// <summary>Амплитуда формы <paramref name="m"/> по (5.9) (м, без знака и коэффициента) и ход; null — ошибка.</summary>
    static (double Amplitude, string Source)? Amplitude59(string label, FemModesParams p, FemModeAmplitude m, FemModesLookup found,
        FemModeShape shape, IReadOnlyDictionary<string, FemElement> elements, FemImperfectionContext? context,
        Action<string, string> error, Action<FemValidationDiagnostic> warn)
    {
        var ci = CultureInfo.CurrentCulture;
        string head = $"Формы «{label}», форма {m.Mode} по (5.9)";
        if (shape.Moments == null || found.Axial == null)
        {
            error("imperfection_modes_moments",
                $"{head}: в результате постановки «{found.AnalysisTag}» нет моментов формы и продольных сил задачи — пересчитайте постановку устойчивости.");
            return null;
        }
        // Кандидаты — сжатые стальные стержни задачи: α_ult,k по N (прим. 1 DIN EN 1993-1-1:2010).
        var candidates = new List<(string Tag, FemElement E, double N, SteelProps59 S, double AlphaUlt)>();
        var notSteel = new List<string>();
        foreach (var (tag, e) in elements.OrderBy(kv => ElementNumber(kv.Key) ?? int.MaxValue).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (e.ElemType != "beam" || ElementNumber(tag) is not int id || !found.Axial.TryGetValue(id, out double n) || !(n < 0)) continue;
            if (Steel59(context?.BeamCross?.Invoke(tag)) is not { } props) { notSteel.Add(tag); continue; }
            candidates.Add((tag, e, n, props, props.NRk / -n));
        }
        if (notSteel.Count > 0)
            warn(new("imperfection_modes_not_steel",
                $"{head}: сжатые стержни без стального сечения ({Short(notSteel)}) в α_ult,k не входят — их сопротивление (5.11) не учтено.", false));
        if (candidates.Count == 0)
        {
            error("imperfection_modes_no_steel", $"{head}: в задаче «{found.CaseTag}» нет сжатых стальных стержней — α_ult,k не определён.");
            return null;
        }
        double alphaUlt = candidates.Min(c => c.AlphaUlt);

        // Точка и ось стержня с наибольшим |M|/M_el,Rk формы; ось EN — по направлению прогиба (Mz — вдоль местной y).
        (int Point, bool AlongLocalY, double M, double Use) Best((string Tag, FemElement E, double N, SteelProps59 S, double AlphaUlt) c)
        {
            (int, bool, double, double) best = (0, true, 0, -1);
            if (ElementNumber(c.Tag) is not int id || !shape.Moments.TryGetValue(id, out var pts)) return best;
            bool yAlongY = c.E.Origin != FemMember.MeshSourceImported;
            for (int k = 0; k < pts.Length; k++)
                foreach (bool alongY in new[] { true, false })
                {
                    double mm = Math.Abs(pts[k][alongY ? 1 : 0]);
                    double use = mm / c.S.MRk(alongY == yAlongY);
                    if (use > best.Item4) best = (k, alongY, mm, use);
                }
            return best;
        }

        (string Tag, FemElement E, double N, SteelProps59 S, double AlphaUlt) crit;
        bool manual = p.CriticalElement != null;
        if (manual)
        {
            var pick = candidates.Where(c => c.Tag == p.CriticalElement).ToList();
            if (pick.Count == 0)
            {
                error("imperfection_modes_critical",
                    $"{head}: критический КЭ {p.CriticalElement} — не сжатый стальной стержень задачи «{found.CaseTag}».");
                return null;
            }
            crit = pick[0];
        }
        else crit = candidates.Where(c => c.AlphaUlt <= alphaUlt * (1 + AlphaUltTie)).MaxBy(c => Best(c).Use);
        var at = Best(crit);
        if (!(at.M > 0))
        {
            error("imperfection_modes_critical", $"{head}: момент формы в критическом КЭ {crit.Tag} равен нулю — задайте критический КЭ вручную.");
            return null;
        }
        double maxM = shape.Moments.Values.SelectMany(q => q).Select(q => Math.Max(Math.Abs(q[0]), Math.Abs(q[1]))).DefaultIfEmpty(0).Max();
        if (at.M < CurvatureSmall * maxM)
            warn(new("imperfection_modes_curvature", string.Create(ci,
                $"{head}: момент формы в критическом КЭ {crit.Tag} ({at.M / 1000:0.###} кН·м) меньше {CurvatureSmall:0%} наибольшего по схеме ({maxM / 1000:0.###} кН·м) — амплитуда велика, проверьте критическое сечение."), false));

        bool aboutContourX = at.AlongLocalY == (crit.E.Origin != FemMember.MeshSourceImported);
        string curveWhy;
        En1993Curve? curve = En1993Imperfections.ParseCurve(p.Curve);
        if (curve != null) curveWhy = $"кривая {En1993Imperfections.Name(curve.Value)} задана вручную";
        else
        {
            var choice = En1993Imperfections.Curve(crit.S.Section, aboutContourX);
            if (choice.Curve == null)
            {
                error("imperfection_modes_curve", $"{head}: кривая табл. 6.2 для КЭ {crit.Tag} не определена ({choice.Reason}) — задайте кривую вручную.");
                return null;
            }
            curve = choice.Curve;
            curveWhy = choice.Reason;
        }

        double alphaCr = found.Lambda[m.Mode - 1];
        double gamma = p.GammaM1 ?? En1993Imperfections.GammaM1Recommended;
        double lambdaBar = Math.Sqrt(alphaUlt / alphaCr);
        double nRk = crit.S.NRk, mRk = crit.S.MRk(aboutContourX);
        var e0 = En1993Imperfections.ModeE0(curve.Value, lambdaBar, mRk, nRk, gamma);
        var chi = En1993Imperfections.Chi(lambdaBar, curve.Value);
        double nCr = alphaCr * -crit.N;
        double a = e0.Small ? 0 : En1993Imperfections.ModeAmplitude(e0.E0, nCr, at.M);
        if (e0.Small)
            warn(new("imperfection_modes_lambda_small", string.Create(ci,
                $"{head}: λ̄ = {lambdaBar:0.###} ≤ 0,2 — (5.10) не применяется, амплитуда 0."), false));

        int points = shape.Moments.TryGetValue(ElementNumber(crit.Tag)!.Value, out var all) ? all.Length : 0;
        string where = at.Point == 0 ? "узел I" : at.Point == points - 1 ? "узел J" : $"точка {at.Point} из {points - 2}";
        var alphaUltElem = candidates.First(c => c.AlphaUlt == alphaUlt);
        string source = string.Create(ci,
            $"α_ult,k = min N_Rk/N_Ed = {alphaUltElem.S.NRk / 1000:0.#}/{-alphaUltElem.N / 1000:0.#} = {alphaUlt:0.###} (КЭ {alphaUltElem.Tag}, N_Rk = A·R_yn); "
            + $"α_cr = λ{m.Mode} = {alphaCr:0.###}{(m.Mode > 1 ? " (норма — определяющая форма λ₁)" : "")}; "
            + $"λ̄ = √(α_ult,k/α_cr) = {lambdaBar:0.###} (5.11); {curveWhy}; {chi.Describe()}; "
            + $"критическое сечение — КЭ {crit.Tag}{(manual ? " (вручную)" : "")}, {where}, {(at.AlongLocalY ? "M_z (прогиб по местной y)" : "M_y (прогиб по местной z)")}: "
            + $"N_Rk = {nRk / 1000:0.#} кН, M_Rk = M_el,Rk = W_el·R_yn = {mRk / 1000:0.###} кН·м; "
            + $"{e0.Describe()}; ") + (e0.Small ? "η_init = 0" : string.Create(ci,
                $"N_cr = α_cr·N_Ed = {alphaCr:0.###}·{-crit.N / 1000:0.#} = {nCr / 1000:0.#} кН; EI·η″ = {at.M / 1000:0.###} кН·м; "
                + $"η_init = e₀·N_cr/(EI·η″) = {a * 1000:0.##} мм (5.9)"));
        return (a, source);
    }

    static string Short(IReadOnlyList<string> tags) =>
        tags.Count <= 5 ? string.Join(", ", tags) : string.Join(", ", tags.Take(5)) + $" и ещё {tags.Count - 5}";
}
