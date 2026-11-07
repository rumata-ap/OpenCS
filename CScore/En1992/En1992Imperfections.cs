using System.Globalization;

namespace CScore.En1992;

/// <summary>
/// Наклон θᵢ по EN 1992-1-1 (5.1) с ходом вычисления: θᵢ = θ₀·α_h·α_m, θ₀ = 1/200, α_h = 2/√l (2/3 ≤ α_h ≤ 1),
/// α_m = √(0,5·(1 + 1/m)); l и m — по случаю п. 5.2(6).
/// </summary>
public sealed record En1992Theta(double L, double AlphaH, int M, double AlphaM, double Theta)
{
    /// <summary>Строка хода вычисления: «θᵢ = θ₀·α_h·α_m = 1/200 · … = 1/…; α_h = 2/√l = …; α_m = …».</summary>
    public string Describe()
    {
        var c = CultureInfo.CurrentCulture;
        double raw = 2 / Math.Sqrt(L);
        string clamp = raw > 1 ? " → 1 (не более 1)" : raw < 2.0 / 3 ? " → 2/3 (не менее 2/3)" : "";
        return string.Create(c,
            $"θᵢ = θ₀·α_h·α_m = 1/200 · {AlphaH:0.###} · {AlphaM:0.###} = 1/{1 / Theta:0.#} ({Theta:0.######}); "
            + $"α_h = 2/√l = 2/√{L:0.###} = {raw:0.###}{clamp}; α_m = √(0,5·(1 + 1/m)) = √(0,5·(1 + 1/{M})) = {AlphaM:0.###}");
    }
}

/// <summary>
/// Геометрические несовершенства EN 1992-1-1:2004 (п. 5.2), сверено по тексту ТКП EN 1992-1-1-2009
/// (<c>obsidian\norms\raw\1992_1_1_2009.pdf</c>, с. 36–38 текста = с. 52–54 PDF) и поправке AC:2019
/// (<c>BDS_EN_1992-1-1_2004_AC_2019.pdf</c>, с. 5, п. 12 — только ссылка «(4)» → «(6)» и рис. 5.1 a2): ф. (5.1) —
/// наклон θᵢ; п. (6) — l и m по трём случаям (элемент: l — длина, m = 1; раскрепляющая система: l — высота здания;
/// диафрагма: l — высота этажа); п. (7)a, ф. (5.2) — эксцентриситет отдельного элемента eᵢ = θᵢ·l₀/2 (l₀ — расчётная
/// длина), для стен и отдельных колонн раскреплённых систем упрощённо eᵢ = l₀/400 (α_h = 1), п. (9) — то же.
/// В отличие от EN 1993 (5.5), m — все вертикальные элементы, вносящие вклад (без правила «&gt; 50 % средней»).
/// </summary>
public static class En1992Imperfections
{
    /// <summary>Основное значение θ₀ = 1/200 (рекомендуемое, примечание к п. 5.2(5)).</summary>
    public const double Theta0 = 1.0 / 200;

    /// <summary>Знаменатель упрощённого эксцентриситета eᵢ = l₀/400 (п. 5.2(7)a, (9)).</summary>
    public const double SimplifiedL0OverE = 400;

    /// <summary>θᵢ по (5.1): <paramref name="l"/> — длина или высота по п. 5.2(6), м (&gt; 0); <paramref name="m"/> ≥ 1.</summary>
    public static En1992Theta Theta(double l, int m)
    {
        if (!(l > 0) || !double.IsFinite(l)) throw new ArgumentOutOfRangeException(nameof(l), "Длина или высота l по (5.1) должна быть положительной.");
        if (m < 1) throw new ArgumentOutOfRangeException(nameof(m), "Число вертикальных элементов m по (5.1) — не меньше 1.");
        double alphaH = Math.Clamp(2 / Math.Sqrt(l), 2.0 / 3, 1.0);
        double alphaM = Math.Sqrt(0.5 * (1 + 1.0 / m));
        return new En1992Theta(l, alphaH, m, alphaM, Theta0 * alphaH * alphaM);
    }

    /// <summary>eᵢ = θᵢ·l₀/2 (5.2), м.</summary>
    public static double BowEi(double theta, double l0) => theta * l0 / 2;

    /// <summary>Упрощённо eᵢ = l₀/400 (п. 5.2(7)a, (9)), м.</summary>
    public static double BowSimplified(double l0) => l0 / SimplifiedL0OverE;

    /// <summary>
    /// Ход eᵢ по (5.2) для отдельного элемента длиной <paramref name="length"/> (случай «элемент»: l = длина, m = 1),
    /// l₀ = μ·L; возвращает eᵢ, м.
    /// </summary>
    public static double BowEi(double length, double mu, out string describe)
    {
        var theta = Theta(length, 1);
        double l0 = mu * length;
        double e = BowEi(theta.Theta, l0);
        describe = string.Create(CultureInfo.CurrentCulture,
            $"EN 1992-1-1 (5.2): eᵢ = θᵢ·l₀/2 = 1/{1 / theta.Theta:0.#} · {l0:0.###}/2 = {e * 1000:0.##} мм; "
            + $"l₀ = μ·L = {mu:0.###} · {length:0.###} = {l0:0.###} м; {theta.Describe()} (п. 5.2(6): элемент, l = L, m = 1)");
        return e;
    }

    /// <summary>Ход упрощённого eᵢ = l₀/400, l₀ = μ·L; возвращает eᵢ, м.</summary>
    public static double BowSimplified(double length, double mu, out string describe)
    {
        double l0 = mu * length;
        double e = BowSimplified(l0);
        describe = string.Create(CultureInfo.CurrentCulture,
            $"EN 1992-1-1 п. 5.2(7)a, (9): eᵢ = l₀/400 = {l0:0.###}/400 = {e * 1000:0.##} мм (α_h = 1); l₀ = μ·L = {mu:0.###} · {length:0.###} м");
        return e;
    }
}
