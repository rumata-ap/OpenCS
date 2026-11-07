using System.Globalization;
using CScore.ParametricSteel;
using CScore.Sp16;

namespace CScore.En1993;

/// <summary>Кривая потери устойчивости EN 1993-1-1 (табл. 6.1, 6.2).</summary>
public enum En1993Curve { a0 = 0, a = 1, b = 2, c = 3, d = 4 }

/// <summary>
/// Наклон крена по EN 1993-1-1 (5.5) с ходом вычисления: φ = φ₀·α_h·α_m, φ₀ = 1/200, α_h = 2/√h (2/3 ≤ α_h ≤ 1),
/// α_m = √(0,5·(1 + 1/m)).
/// </summary>
public sealed record En1993SwayPhi(double H, double AlphaH, int M, double AlphaM, double Phi)
{
    /// <summary>Строка хода вычисления: «φ = 1/200 · α_h · α_m = …; α_h = 2/√h = …; α_m = …».</summary>
    public string Describe()
    {
        var c = CultureInfo.CurrentCulture;
        double raw = 2 / Math.Sqrt(H);
        string clamp = raw > 1 ? " → 1 (не более 1)" : raw < 2.0 / 3 ? " → 2/3 (не менее 2/3)" : "";
        return string.Create(c,
            $"φ = φ₀·α_h·α_m = 1/200 · {AlphaH:0.###} · {AlphaM:0.###} = 1/{1 / Phi:0.#} ({Phi:0.######}); "
            + $"α_h = 2/√h = 2/√{H:0.###} = {raw:0.###}{clamp}; α_m = √(0,5·(1 + 1/m)) = √(0,5·(1 + 1/{M})) = {AlphaM:0.###}");
    }
}

/// <summary>
/// Понижающий коэффициент χ по EN 1993-1-1 (6.49) с ходом: Φ = 0,5·[1 + α(λ̄ − 0,2) + λ̄²], χ = 1/(Φ + √(Φ² − λ̄²)) ≤ 1.
/// </summary>
public sealed record En1993Chi(double LambdaBar, En1993Curve Curve, double Alpha, double Phi, double Chi)
{
    public string Describe() => string.Create(CultureInfo.CurrentCulture,
        $"Φ = 0,5·[1 + α(λ̄ − 0,2) + λ̄²] = 0,5·[1 + {Alpha:0.##}·({LambdaBar:0.###} − 0,2) + {LambdaBar:0.###}²] = {Phi:0.###}; "
        + $"χ = 1/(Φ + √(Φ² − λ̄²)) = {Chi:0.###}{(Chi >= 1 ? " (не более 1)" : "")} (6.49, кривая {En1993Imperfections.Name(Curve)})");
}

/// <summary>
/// Амплитуда e₀ несовершенства по форме потери устойчивости EN 1993-1-1 (5.10) с ходом:
/// e₀ = α·(λ̄ − 0,2)·M_Rk/N_Rk·(1 − χλ̄²/γ_M1)/(1 − χλ̄²) при λ̄ &gt; 0,2, иначе 0. M_Rk — Н·м, N_Rk — Н, e₀ — м.
/// </summary>
public sealed record En1993ModeE0(double Alpha, double LambdaBar, double Chi, double MRk, double NRk, double GammaM1, double E0)
{
    /// <summary>λ̄ ≤ 0,2 — (5.10) не применяется, e₀ = 0.</summary>
    public bool Small => !(LambdaBar > 0.2);

    public string Describe()
    {
        var c = CultureInfo.CurrentCulture;
        if (Small) return string.Create(c, $"λ̄ = {LambdaBar:0.###} ≤ 0,2 — (5.10) не применяется, e₀ = 0");
        double l2 = Chi * LambdaBar * LambdaBar;
        string factor = Math.Abs(GammaM1 - 1) < 1e-12 ? ""
            : string.Create(c, $"·(1 − {l2:0.####}/{GammaM1:0.###})/(1 − {l2:0.####})");
        return string.Create(c,
            $"e₀ = α·(λ̄ − 0,2)·M_Rk/N_Rk·(1 − χλ̄²/γ_M1)/(1 − χλ̄²) = {Alpha:0.##}·({LambdaBar:0.###} − 0,2)·"
            + $"{MRk / 1000:0.###} кН·м/{NRk / 1000:0.###} кН{factor} = {E0 * 1000:0.###} мм (5.10, γ_M1 = {GammaM1:0.###})");
    }
}

/// <summary>Кривая сечения по табл. 6.2 и пояснение выбора (ветвь таблицы); <see cref="Curve"/> null — не определена.</summary>
public sealed record En1993CurveChoice(En1993Curve? Curve, string Reason);

/// <summary>
/// Эквивалентные геометрические несовершенства EN 1993-1-1:2005+AC:2009 (п. 5.3.2), сверено по тексту ТКП EN 1993-1-1-2009
/// (<c>obsidian\norms\raw\TKP EN 1993-1-1-2009.pdf</c>): ф. (5.5) — с. 21 (с. 31 PDF), табл. 5.1 — с. 21–22 (с. 31–32 PDF),
/// п. 5.3.2(6), ф. (5.8) — с. 22 (с. 32 PDF), табл. 6.1 — с. 43 (с. 53 PDF), табл. 6.2 — с. 44 (с. 54 PDF). Число m по (5.5) —
/// колонны с N_Ed больше 50 % средней (DIN EN 1993-1-1:2010 «größer»; ТКП «не менее» — по DIN, спека разд. 2).
/// Амплитуда формы потери устойчивости п. 5.3.2(11), ф. (5.9)–(5.11) — с. 23–24 ТКП (с. 33–34 PDF) и с. 39 DIN EN
/// 1993-1-1:2010-12 (с. 43 PDF книги; AC:2009 — |η″_cr|_max, моменты в α_ult,k не учитываются); χ — (6.49), с. 43 ТКП
/// (с. 53 PDF); γ_M1 = 1,00 — рекомендуемое (п. 6.1(1), прим. 2B).
/// </summary>
public static class En1993Imperfections
{
    /// <summary>Основное значение крена φ₀ = 1/200 (п. 5.3.2(3)a).</summary>
    public const double Phi0 = 1.0 / 200;

    /// <summary>Граница колонки S460 табл. 6.2 по R_yn, кПа: S460 при R_yn &gt; 440 МПа (ближе к 460, чем к 420).</summary>
    public const double S460RynThresholdKPa = 440000;

    /// <summary>
    /// φ по (5.5): <paramref name="h"/> — высота конструкции, м (&gt; 0); <paramref name="m"/> — число колонн в ряду
    /// с N_Ed больше 50 % средней (≥ 1).
    /// </summary>
    public static En1993SwayPhi Phi(double h, int m)
    {
        if (!(h > 0) || !double.IsFinite(h)) throw new ArgumentOutOfRangeException(nameof(h), "Высота h по (5.5) должна быть положительной.");
        if (m < 1) throw new ArgumentOutOfRangeException(nameof(m), "Число колонн m по (5.5) — не меньше 1.");
        double alphaH = Math.Clamp(2 / Math.Sqrt(h), 2.0 / 3, 1.0);
        double alphaM = Math.Sqrt(0.5 * (1 + 1.0 / m));
        return new En1993SwayPhi(h, alphaH, m, alphaM, Phi0 * alphaH * alphaM);
    }

    /// <summary>L/e₀ табл. 5.1 (рекомендуемые значения): упругий расчёт | пластический.</summary>
    public static double LOverE0(En1993Curve curve, bool plastic) => curve switch
    {
        En1993Curve.a0 => plastic ? 300 : 350,
        En1993Curve.a => plastic ? 250 : 300,
        En1993Curve.b => plastic ? 200 : 250,
        En1993Curve.c => plastic ? 150 : 200,
        En1993Curve.d => plastic ? 100 : 150,
        _ => throw new ArgumentOutOfRangeException(nameof(curve)),
    };

    /// <summary>e₀/L табл. 5.1.</summary>
    public static double E0OverL(En1993Curve curve, bool plastic) => 1 / LOverE0(curve, plastic);

    /// <summary>Коэффициент несовершенства α табл. 6.1.</summary>
    public static double Alpha(En1993Curve curve) => curve switch
    {
        En1993Curve.a0 => 0.13,
        En1993Curve.a => 0.21,
        En1993Curve.b => 0.34,
        En1993Curve.c => 0.49,
        En1993Curve.d => 0.76,
        _ => throw new ArgumentOutOfRangeException(nameof(curve)),
    };

    /// <summary>Разбор обозначения кривой «a0», «a», …, «d» (без учёта регистра); иначе null.</summary>
    public static En1993Curve? ParseCurve(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "a0" => En1993Curve.a0,
        "a" => En1993Curve.a,
        "b" => En1993Curve.b,
        "c" => En1993Curve.c,
        "d" => En1993Curve.d,
        _ => null,
    };

    /// <summary>Обозначение кривой для отчёта.</summary>
    public static string Name(En1993Curve curve) => curve.ToString();

    /// <summary>
    /// Кривая по табл. 6.2 для профиля в каноническом положении (<see cref="SteelProfile"/>: ось x — горизонтальная,
    /// наибольшей жёсткости). <paramref name="strongAxis"/> — потеря устойчивости относительно оси y–y EN (каноническая
    /// x, изгиб в плоскости стенки), иначе z–z. Колонка S460 — при <paramref name="rynKPa"/> &gt; 440 МПа (спека разд. 4.2).
    /// h/b и t_f — по большему поясу. Сварные коробки — общий случай (b): «толстые швы» по сортаменту не определяются.
    /// </summary>
    public static En1993CurveChoice Curve(SteelProfile profile, double rynKPa, bool strongAxis)
    {
        ArgumentNullException.ThrowIfNull(profile);
        bool s460 = rynKPa > S460RynThresholdKPa;
        var c = CultureInfo.CurrentCulture;
        string column = s460 ? "S460" : "S235–S420";
        string axis = strongAxis ? "y–y" : "z–z";
        En1993CurveChoice Pick(En1993Curve curve, string branch) => new(curve, $"табл. 6.2: {branch}, ось {axis}, {column} → {Name(curve)}");
        switch (profile.Kind)
        {
            case SteelProfileKind.IBeam:
            {
                double b = Math.Max(profile.Bf1, profile.BfBottom), tf = Math.Max(profile.Tf1, profile.TfBottom);
                if (!(profile.H > 0) || !(b > 0) || !(tf > 0)) return new(null, "двутавр без размеров h, b, t_f");
                double hb = profile.H / b, tfMm = tf * 1000;
                // Границы таблицы включительно (≤ 1,2; ≤ 40 и ≤ 100 мм) — с допуском на округление размеров в метрах.
                const double eps = 1e-9;
                string dims = string.Create(c, $"h/b = {hb:0.###}, t_f = {tfMm:0.#} мм");
                if (profile.Fabrication == SteelFabrication.Rolled)
                {
                    if (hb > 1.2 + eps)
                    {
                        if (tfMm <= 40 + eps) return Pick(strongAxis ? (s460 ? En1993Curve.a0 : En1993Curve.a) : (s460 ? En1993Curve.a0 : En1993Curve.b),
                            $"прокатный двутавр, {dims} (h/b > 1,2, t_f ≤ 40 мм)");
                        if (tfMm <= 100 + eps) return Pick(strongAxis ? (s460 ? En1993Curve.a : En1993Curve.b) : (s460 ? En1993Curve.a : En1993Curve.c),
                            $"прокатный двутавр, {dims} (h/b > 1,2, 40 < t_f ≤ 100 мм)");
                        return new(null, $"прокатный двутавр, {dims}: h/b > 1,2 при t_f > 100 мм в табл. 6.2 нет");
                    }
                    if (tfMm <= 100 + eps) return Pick(strongAxis ? (s460 ? En1993Curve.a : En1993Curve.b) : (s460 ? En1993Curve.a : En1993Curve.c),
                        $"прокатный двутавр, {dims} (h/b ≤ 1,2, t_f ≤ 100 мм)");
                    return Pick(s460 ? En1993Curve.c : En1993Curve.d, $"прокатный двутавр, {dims} (h/b ≤ 1,2, t_f > 100 мм)");
                }
                if (profile.Fabrication == SteelFabrication.Welded)
                {
                    string tfOnly = string.Create(c, $"t_f = {tfMm:0.#} мм");
                    return tfMm <= 40 + eps
                        ? Pick(strongAxis ? En1993Curve.b : En1993Curve.c, $"сварной двутавр, {tfOnly} (t_f ≤ 40 мм)")
                        : Pick(strongAxis ? En1993Curve.c : En1993Curve.d, $"сварной двутавр, {tfOnly} (t_f > 40 мм)");
                }
                return new(null, "гнутый двутавр в табл. 6.2 не приведён");
            }
            case SteelProfileKind.Box or SteelProfileKind.Pipe:
            {
                string kind = profile.Kind == SteelProfileKind.Box ? "коробчатое" : "трубчатое";
                return profile.Fabrication switch
                {
                    SteelFabrication.Rolled => Pick(s460 ? En1993Curve.a0 : En1993Curve.a, $"замкнутое {kind} горячедеформированное"),
                    SteelFabrication.Bent => Pick(En1993Curve.c, $"замкнутое {kind} холоднодеформированное"),
                    _ when profile.Kind == SteelProfileKind.Box => Pick(En1993Curve.b,
                        "сварное коробчатое, общий случай (признак «толстых швов» a > 0,5t_f, b/t_f < 30, h/t_w < 30 по сортаменту не определяется — при нём кривая c, задайте вручную)"),
                    _ => new(null, "сварная труба: горячая или холодная деформация не известна"),
                };
            }
            case SteelProfileKind.Channel:
                return Pick(En1993Curve.c, "швеллер");
            case SteelProfileKind.Tee:
                return Pick(En1993Curve.c, "тавр");
            case SteelProfileKind.Rect or SteelProfileKind.Round:
                return Pick(En1993Curve.c, "сплошное сечение");
            case SteelProfileKind.Angle:
                return Pick(En1993Curve.b, "уголок");
            default:
                return new(null, "профиль не распознан (произвольное сечение)");
        }
    }

    /// <summary>
    /// Кривая для сечения CScore стержня: одна стальная область (<see cref="MatType.Steel"/> или
    /// <see cref="MatType.Custom"/>), профиль — параметрического МК-сечения или распознанный по контуру
    /// (<see cref="SteelProfileRecognizer"/>), R_yn — материала. <paramref name="aboutContourX"/> — потеря устойчивости
    /// относительно оси x контура сечения; у повёрнутого на 90° контура оси y–y/z–z EN переставляются.
    /// </summary>
    public static En1993CurveChoice Curve(CrossSection section, bool aboutContourX)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (section.Areas.Count != 1 || section.Areas[0].Material is not { Type: MatType.Steel or MatType.Custom } material)
            return new(null, $"сечение «{section.Tag}» — не одна стальная область");
        var area = section.Areas[0];
        if (area.Hull?.Points is not { Count: >= 3 } hull) return new(null, $"сечение «{section.Tag}» без контура");
        var profile = section.TryGetParametricSteelProfile();
        if (profile == null)
        {
            var polygon = new PolygonSection(hull.Select(p => (p.X, p.Y)), area.Holes.Select(h => h.Points.Select(p => (p.X, p.Y))));
            profile = SteelProfileRecognizer.Recognize(polygon);
        }
        var mat = SteelMaterialProps.FromMaterial(material);
        bool strong = aboutContourX ^ profile.Rotated90;
        return Curve(profile, mat.RynOrRy, strong);
    }

    /// <summary>Рекомендуемое γ_M1 (п. 6.1(1), прим. 2B).</summary>
    public const double GammaM1Recommended = 1.0;

    /// <summary>χ по (6.49) для условной гибкости <paramref name="lambdaBar"/> (≥ 0) и кривой <paramref name="curve"/>.</summary>
    public static En1993Chi Chi(double lambdaBar, En1993Curve curve)
    {
        if (!(lambdaBar >= 0) || !double.IsFinite(lambdaBar)) throw new ArgumentOutOfRangeException(nameof(lambdaBar), "Условная гибкость λ̄ — неотрицательное число.");
        double alpha = Alpha(curve);
        double phi = 0.5 * (1 + alpha * (lambdaBar - 0.2) + lambdaBar * lambdaBar);
        double chi = Math.Min(1.0, 1 / (phi + Math.Sqrt(Math.Max(0, phi * phi - lambdaBar * lambdaBar))));
        return new En1993Chi(lambdaBar, curve, alpha, phi, chi);
    }

    /// <summary>
    /// e₀ по (5.10): <paramref name="mRk"/> — Н·м, <paramref name="nRk"/> — Н (&gt; 0), <paramref name="gammaM1"/> &gt; 0;
    /// χ — по (6.49) с той же λ̄ и кривой.
    /// </summary>
    public static En1993ModeE0 ModeE0(En1993Curve curve, double lambdaBar, double mRk, double nRk, double gammaM1 = GammaM1Recommended)
    {
        if (!(mRk > 0) || !(nRk > 0)) throw new ArgumentOutOfRangeException(nameof(nRk), "M_Rk и N_Rk должны быть положительными.");
        if (!(gammaM1 > 0)) throw new ArgumentOutOfRangeException(nameof(gammaM1), "γ_M1 должен быть положительным.");
        var chi = Chi(lambdaBar, curve);
        double alpha = chi.Alpha;
        double e0 = 0;
        if (lambdaBar > 0.2)
        {
            double l2 = chi.Chi * lambdaBar * lambdaBar;
            double ratio = Math.Abs(1 - l2) < 1e-12 ? 1 : (1 - l2 / gammaM1) / (1 - l2);
            e0 = alpha * (lambdaBar - 0.2) * mRk / nRk * ratio;
        }
        return new En1993ModeE0(alpha, lambdaBar, chi.Chi, mRk, nRk, gammaM1, e0);
    }

    /// <summary>Амплитуда формы по (5.9): η_init = e₀·N_cr/|EI·η″_cr,max|, м (e₀ — м, N_cr — Н, EI·η″ — Н·м на 1 м формы).</summary>
    public static double ModeAmplitude(double e0, double nCr, double eiEta2) =>
        !(Math.Abs(eiEta2) > 0) ? throw new ArgumentOutOfRangeException(nameof(eiEta2), "Момент формы EI·η″ равен нулю.")
            : e0 * nCr / Math.Abs(eiEta2);

    /// <summary>Критическая сила шарнирного стержня N_cr = π²EI/L², Н (EI — Н·м², L — м).</summary>
    public static double NcrPinned(double ei, double length) => Math.PI * Math.PI * ei / (length * length);

    /// <summary>
    /// Критерий п. 5.3.2(6): погибь вводится при хотя бы одном конце, воспринимающем момент
    /// (<paramref name="momentEnd"/>), и λ̄ &gt; 0,5·√(A·f_y/N_Ed) (5.8) ⇔ N_Ed &gt; 0,25·N_cr (λ̄ = √(A·f_y/N_cr)
    /// шарнирного стержня в плоскости). <paramref name="nEd"/> — сжатие плюс, Н.
    /// </summary>
    public static bool BowActive(double nEd, double ei, double length, bool momentEnd) =>
        momentEnd && nEd > 0 && nEd > 0.25 * NcrPinned(ei, length);
}
