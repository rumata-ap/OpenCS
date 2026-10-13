namespace CScore.PlateStrip;

/// <summary>Относительные расхождения двух решений полосы (собственная балка ↔ фибровый стержень,
/// shell ↔ балка). Нормировка — на максимум модуля по обеим эпюрам.</summary>
public static class StripResultMismatch
{
    /// <summary>Максимальное расхождение изгибающих моментов My по станциям, доля от масштаба эпюры.
    /// NaN — эпюры разной длины или пустые.</summary>
    public static double Moment(IReadOnlyList<double[]> a, IReadOnlyList<double[]> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Count == 0 || a.Count != b.Count) return double.NaN;

        double scale = 0.0;
        for (int i = 0; i < a.Count; i++)
            scale = Math.Max(scale, Math.Max(Math.Abs(a[i][1]), Math.Abs(b[i][1])));
        if (scale <= 0.0) return 0.0;

        double worst = 0.0;
        for (int i = 0; i < a.Count; i++)
            worst = Math.Max(worst, Math.Abs(a[i][1] - b[i][1]) / scale);
        return worst;
    }

    /// <summary>Относительное расхождение двух чисел по большему модулю; 0 при обоих нулях.</summary>
    public static double Relative(double a, double b)
    {
        double scale = Math.Max(Math.Abs(a), Math.Abs(b));
        return scale > 0.0 ? Math.Abs(a - b) / scale : 0.0;
    }
}
