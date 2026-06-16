namespace CSfea.Core;

/// <summary>Числовые утилиты: 1D-интерполяция и численный градиент (аналоги numpy).</summary>
public static class Num
{
    /// <summary>
    /// Линейная интерполяция <c>np.interp</c>: за пределами диапазона —
    /// постоянная экстраполяция крайними значениями. Узлы <paramref name="xp"/>
    /// должны возрастать.
    /// </summary>
    public static double Interp(double x, double[] xp, double[] fp)
    {
        int n = xp.Length;
        if (x <= xp[0]) return fp[0];
        if (x >= xp[n - 1]) return fp[n - 1];
        // двоичный поиск интервала
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (xp[mid] <= x) lo = mid; else hi = mid;
        }
        double t = (x - xp[lo]) / (xp[hi] - xp[lo]);
        return fp[lo] + t * (fp[hi] - fp[lo]);
    }

    /// <summary>
    /// Центральный градиент <c>np.gradient</c> по неравномерной сетке
    /// (краевые точки — односторонние разности).
    /// </summary>
    public static double[] Gradient(double[] y, double[] x)
    {
        int n = y.Length;
        var g = new double[n];
        if (n == 1) { g[0] = 0.0; return g; }
        g[0] = (y[1] - y[0]) / (x[1] - x[0]);
        g[n - 1] = (y[n - 1] - y[n - 2]) / (x[n - 1] - x[n - 2]);
        for (int i = 1; i < n - 1; i++)
        {
            double hd = x[i + 1] - x[i];
            double hs = x[i] - x[i - 1];
            g[i] = (hs * hs * y[i + 1] + (hd * hd - hs * hs) * y[i] - hd * hd * y[i - 1])
                   / (hs * hd * (hd + hs));
        }
        return g;
    }
}
