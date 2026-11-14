namespace CScore.Sp63Punching;

/// <summary>
/// Приведённый периметр контура продавливания прямоугольной колонной по Галяутдинову З. Ш.
/// «Влияние конструктивных параметров узла сопряжения плиты и колонны на прочность железобетонных плит
/// при продавливании»: дис. … канд. техн. наук. Самара: СамГТУ, 2022, гл. 5, формулы (5.9)–(5.35).
/// Не нормативная методика: в OpenCS — справочный вариант.
/// </summary>
/// <param name="N">n = Cmax/Cmin.</param>
/// <param name="Beta">β = √(2n(n−1)+1).</param>
/// <param name="Chi">χ (5.9).</param>
/// <param name="Rm">r_m (5.15), м.</param>
/// <param name="Kls">K_ls (5.23)/(5.24).</param>
/// <param name="Kshs">K_shs (5.32).</param>
/// <param name="URed">u_red (5.33), м.</param>
public sealed record GalyautdinovPerimeter(double N, double Beta, double Chi, double Rm, double Kls, double Kshs, double URed)
{
    /// <summary>Считает приведённый периметр по сторонам колонны и h0 (любые одинаковые единицы).</summary>
    public static GalyautdinovPerimeter Compute(double bx, double by, double h0)
    {
        double cmin = Math.Min(bx, by), cmax = Math.Max(bx, by);
        double n = cmax / cmin;
        double beta = Math.Sqrt(2 * n * (n - 1) + 1);
        double chi = Math.Sqrt(2 * (beta + n)) / (1 + beta);                               // (5.9)
        double half = Math.Sqrt((beta + n) / 2) * (1 + beta);
        double lambdaL = half * Math.Acos(1 / Math.Sqrt(beta + n)) - n;                    // (5.13)
        double rm = cmin * lambdaL / (4 * n);                                              // (5.15)
        double kls;
        if (rm < h0 / 2)
            kls = 1 / (1 + rm * rm / (h0 * h0) * (2 - Math.PI / 2));                        // (5.23)
        else
        {
            double alpha = 2 * Math.Acos(1 - h0 / (2 * rm));                               // (5.21)
            kls = 1 / (1 + rm * rm / (h0 * h0) * (h0 / rm - (alpha - Math.Sin(alpha)) / 2)); // (5.24)
        }
        double lambdaSh = half * Math.Acos(Math.Min(1, n * Math.Sqrt(2) / (1 + beta))) - n; // (5.27)
        double rmSh = cmin * lambdaSh / 4;                                                 // (5.29)
        double kshs = 1 / (1 + rmSh * rmSh / (h0 * h0) * (2 - Math.PI / 2));                // (5.32)
        double ured = 2 * cmin * (chi * kls * n + kshs) + 4 * h0;                          // (5.33)
        return new GalyautdinovPerimeter(n, beta, chi, rm, kls, kshs, ured);
    }

    /// <summary>Множитель продольного армирования (100μ)^(1/3) (5.34); μ — доля; null — 1.</summary>
    public static double ReinforcementFactor(double? mu) => mu is double m && m > 0 ? Math.Cbrt(100 * m) : 1.0;
}
