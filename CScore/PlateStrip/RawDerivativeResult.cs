namespace CScore.PlateStrip;

/// <summary>Диагностическое дифференцирование целевой эпюры: интенсивность нагрузки в станциях,
/// полученная конечными разностями. Заполняется всегда, во всех режимах, но расчётной нагрузкой
/// не становится никогда — прямое требование родительской спеки.
///
/// Размерность учитывает, что станции безразмерны (x = L·s):
/// <code>
/// qx = −(1/L)·dN/ds      qz = −(1/L²)·d²My/ds²      qy = (1/L²)·d²Mz/ds²
/// </code>
/// На неравномерной сетке используются разделённые разности, на крайних станциях —
/// односторонние формулы второго порядка.</summary>
public sealed record RawDerivativeResult(
    IReadOnlyList<double> StationFractions,
    IReadOnlyList<double> Qx,
    IReadOnlyList<double> Qy,
    IReadOnlyList<double> Qz,
    bool IsDefined)
{
    /// <summary>Неопределённый результат (станций меньше трёх).</summary>
    public static RawDerivativeResult Undefined(IReadOnlyList<double> stations) =>
        new(stations, new double[stations.Count], new double[stations.Count],
            new double[stations.Count], false);

    /// <summary>Считает интенсивности по целевой эпюре.</summary>
    public static RawDerivativeResult Compute(TargetBeamResultants target, double lengthM)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!(lengthM > 0.0) || !double.IsFinite(lengthM))
            throw new ArgumentOutOfRangeException(nameof(lengthM),
                "Длина полосы должна быть конечной и положительной.");

        var s = target.StationFractions;
        int n = s.Count;
        if (n < 3)
            return Undefined(s);

        var qx = new double[n];
        var qy = new double[n];
        var qz = new double[n];
        double l2 = lengthM * lengthM;

        for (int i = 0; i < n; i++)
        {
            qx[i] = -FirstDerivative(s, target.N, i) / lengthM;
            qz[i] = -SecondDerivative(s, target.My, i) / l2;
            qy[i] = SecondDerivative(s, target.Mz, i) / l2;
        }

        return new(s, qx, qy, qz, true);
    }

    /// <summary>Тройка узлов, по которой строится квадратичный интерполянт для станции i:
    /// сама станция и её соседи, на краях — ближайшая внутренняя тройка (это и даёт
    /// односторонние формулы второго порядка).</summary>
    static int CenterIndex(int stationCount, int i) =>
        i == 0 ? 1 : i == stationCount - 1 ? stationCount - 2 : i;

    /// <summary>Первая производная квадратичного интерполянта по трём узлам в точке s[i].</summary>
    static double FirstDerivative(IReadOnlyList<double> s, IReadOnlyList<double> f, int i)
    {
        int j = CenterIndex(s.Count, i);
        double x0 = s[j - 1], x1 = s[j], x2 = s[j + 1], x = s[i];

        return f[j - 1] * (2.0 * x - x1 - x2) / ((x0 - x1) * (x0 - x2))
             + f[j] * (2.0 * x - x0 - x2) / ((x1 - x0) * (x1 - x2))
             + f[j + 1] * (2.0 * x - x0 - x1) / ((x2 - x0) * (x2 - x1));
    }

    /// <summary>Вторая производная квадратичного интерполянта по трём узлам (постоянна на тройке).</summary>
    static double SecondDerivative(IReadOnlyList<double> s, IReadOnlyList<double> f, int i)
    {
        int j = CenterIndex(s.Count, i);
        double x0 = s[j - 1], x1 = s[j], x2 = s[j + 1];

        return 2.0 * (f[j - 1] / ((x0 - x1) * (x0 - x2))
                    + f[j] / ((x1 - x0) * (x1 - x2))
                    + f[j + 1] / ((x2 - x0) * (x2 - x1)));
    }
}
