namespace CSfea.CScoreBridge.Structural;

/// <summary>
/// Уравнения номограмм (alignment charts) для колонны изолированной подсистемы (ASCE 1997, п. 2.3.1; Chen, Lui 1987):
/// рама со смещением — ур. (2.9), без смещения — ур. (2.13). G — отношение Σ(EI/L) колонн к Σ(EI/L) ригелей в узле
/// конца; +∞ — шарнир (ригели не держат), 0 — заделка. Корень ищется делением пополам: со смещением u = π/K ∈ (0; π)
/// (K ≥ 1, функция возрастает), без смещения u ∈ (π; 2π) (0,5 ≤ K ≤ 1).
/// </summary>
public static class RcAlignmentChart
{
    // G больше — шарнир; меньше — заделка (уравнения вырождаются в концах диапазона u).
    const double GMax = 1e12, GMin = 1e-12;

    /// <summary>K рамы со смещением по ур. (2.9); +∞ — механизм (оба конца без закрепления от поворота).</summary>
    public static double Sway(double gA, double gB)
    {
        double a = Clamp(gA), b = Clamp(gB);
        if (a >= GMax && b >= GMax) return double.PositiveInfinity;
        double scale = (1 + a) * (1 + b);
        double F(double u) => (a * b * u * u - 36 - 6 * (a + b) * u / Math.Tan(u)) / scale;
        double u0 = Bisect(F, 1e-9, Math.PI - 1e-12);
        return Math.PI / u0;
    }

    /// <summary>K рамы без смещения по ур. (2.13).</summary>
    public static double Braced(double gA, double gB)
    {
        double a = Clamp(gA), b = Clamp(gB);
        double scale = (1 + a) * (1 + b);
        double F(double u) => (a * b / 4 * u * u + (a + b) / 2 * (1 - u / Math.Tan(u)) + 2 * Math.Tan(u / 2) / u - 1) / scale;
        double lo = Math.PI * (1 + 1e-12), hi = 2 * Math.PI * (1 - 1e-12);
        if (F(lo) >= 0) return 1;      // оба конца — шарниры: K = 1
        if (F(hi) <= 0) return 0.5;    // оба конца — заделки
        return Math.PI / Bisect(F, lo, hi);
    }

    /// <summary>Невязка ур. (2.9) при K — для подстановки в отчёте.</summary>
    public static double SwayResidual(double gA, double gB, double k)
    {
        double u = Math.PI / k;
        return (gA * gB * u * u - 36) / (6 * (gA + gB)) - u / Math.Tan(u);
    }

    static double Clamp(double g) => double.IsNaN(g) ? GMax : Math.Clamp(g, GMin, GMax);

    // Корень возрастающей на [lo; hi] функции со сменой знака.
    static double Bisect(Func<double, double> f, double lo, double hi)
    {
        double flo = f(lo);
        for (int i = 0; i < 200 && hi - lo > 1e-14 * hi; i++)
        {
            double mid = 0.5 * (lo + hi), fm = f(mid);
            if (Math.Sign(fm) == Math.Sign(flo)) { lo = mid; flo = fm; } else hi = mid;
        }
        return 0.5 * (lo + hi);
    }
}

/// <summary>Этаж колонны в табл. 31 СП 16: верхний (над колонной нет продолжения), средний, нижний (у опоры).</summary>
public enum Sp16Storey { Single, Top, Middle, Bottom }

/// <summary>
/// Коэффициент μ колонны рамы по табл. 31 СП 16.13330.2017 (с изм. 1–6; формула (141) — по письму ФЦС от 10.11.2023
/// № Исх-8076). <paramref name="Formula"/> — номер формулы или строки частного случая для отчёта; <paramref name="P"/>,
/// <paramref name="N"/> — параметры таблицы после приведения по числу пролётов и этажу (p = +∞ — заделка основания,
/// p = 0 — шарнир).
/// </summary>
public sealed record Sp16FrameMu(double Mu, string Formula, double P, double N);

/// <summary>
/// Формулы табл. 31 СП 16 для колонн постоянного сечения свободных и несвободных рам (п. 10.3.4). Суммы
/// <c>pSum</c> = Σ I_i·l_c/(I_c·l_i) по ригелям у нижнего конца колонны, <c>nSum</c> = Σ I_s·l_c/(I_c·l_s) — у верхнего;
/// k — число пролётов (для крайней колонны многопролётной свободной рамы — как однопролётной, k = 1 по примечанию к
/// таблице). Основание: заделка — p = +∞, шарнир — p = 0.
/// </summary>
public static class Sp16FrameTable
{
    public static Sp16FrameMu Mu(bool sway, Sp16Storey storey, double pSum, double nSum, int k)
    {
        k = Math.Max(1, k);
        double f = k / (k + 1.0);
        return sway ? Sway(storey, pSum, nSum, k, f) : Braced(storey, pSum, nSum);
    }

    static Sp16FrameMu Sway(Sp16Storey storey, double pSum, double nSum, int k, double f)
    {
        bool fixedBase = double.IsPositiveInfinity(pSum), pinnedBase = pSum == 0;
        if (storey == Sp16Storey.Single && (fixedBase || pinnedBase))
        {
            double n = k == 1 ? nSum : f * nSum;
            return pinnedBase
                ? new(2 * Math.Sqrt(1 + 0.38 / n), "(141)", 0, n)
                : new(Math.Sqrt((n + 0.56) / (n + 0.14)), "(142)", double.PositiveInfinity, n);
        }
        double p, nn;
        switch (storey)
        {
            case Sp16Storey.Middle: p = f * pSum; nn = f * nSum; break;
            case Sp16Storey.Bottom: p = 2 * f * pSum; nn = f * nSum; break;
            default: p = f * pSum; nn = 2 * f * nSum; break;   // верхний (и одноэтажная без опорного закрепления)
        }
        if (storey == Sp16Storey.Bottom && fixedBase)
            return nn <= 0.2
                ? new(1.21 * Math.Sqrt((nn + 0.22) / (nn + 0.08)), "p = ∞, n ≤ 0,2", double.PositiveInfinity, nn)
                : new(Math.Sqrt((nn + 0.28) / nn), "p = ∞, n > 0,2", double.PositiveInfinity, nn);
        if (storey == Sp16Storey.Bottom && pinnedBase)
            return nn <= 0.2
                ? new(2.15 * Math.Sqrt((nn + 0.22) / nn), "p = 0, n ≤ 0,2", 0, nn)
                : new(2.0 * Math.Sqrt((nn + 0.28) / nn), "p = 0, n > 0,2", 0, nn);
        return nn <= 0.2
            ? new((p + 0.68) * Math.Sqrt(nn + 0.22) / Math.Sqrt(0.68 * p * (p + 0.9) * (nn + 0.08) + 0.1 * nn), "(143)", p, nn)
            : new((p + 0.63) * Math.Sqrt(nn + 0.28) / Math.Sqrt(p * nn * (p + 0.9) + 0.1 * nn), "(144)", p, nn);
    }

    static Sp16FrameMu Braced(Sp16Storey storey, double pSum, double nSum)
    {
        double p, n;
        switch (storey)
        {
            case Sp16Storey.Middle: p = 0.5 * pSum; n = 0.5 * nSum; break;
            case Sp16Storey.Bottom: p = pSum; n = 0.5 * nSum; break;
            default: p = 0.5 * pSum; n = nSum; break;   // верхний и одноэтажная
        }
        if (double.IsPositiveInfinity(p))
            return new(Math.Sqrt((1 + 0.39 * n) / (2 + 1.54 * n)), "p = ∞", p, n);
        if (p == 0 && storey is Sp16Storey.Bottom or Sp16Storey.Single)
            return new(Math.Sqrt((1 + 0.46 * n) / (1 + 0.93 * n)), "p = 0", p, n);
        return new(Math.Sqrt((1 + 0.46 * (p + n) + 0.18 * p * n) / (1 + 0.93 * (p + n) + 0.71 * p * n)), "(145)", p, n);
    }
}
