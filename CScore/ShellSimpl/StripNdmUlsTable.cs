namespace CScore.ShellSimpl;

/// <summary>
/// Предельный контур полосы, затабулированный по плоскостям (<see cref="StripNdm.Tabulate"/>):
/// бетонные усилия и напряжения рядов в каждой точке контура. Для заданных площадей контур —
/// замкнутая ломаная в координатах (N·h/6, M); η = 1/λ по наименьшему пересечению луча (N, M) с
/// ломаной — та же мера, что <see cref="StripNdm.UltimateFactor"/>, с погрешностью хорды.
/// </summary>
public sealed class StripNdmUlsTable
{
    readonly double _sc;

    /// <summary>Толщина полосы, м.</summary>
    public double H { get; }
    readonly double[] _z, _nc, _mc, _sig;
    readonly StripNdmPivot[] _pivots;

    internal StripNdmUlsTable(double h, double[] z, double[] nc, double[] mc, double[] sig, StripNdmPivot[] pivots)
    {
        H = h; _sc = h / 6.0; _z = z; _nc = nc; _mc = mc; _sig = sig; _pivots = pivots;
    }

    /// <summary>Число рядов (порядок — как в полосе, по которой построена таблица).</summary>
    public int RowCount => _z.Length;

    /// <summary>η = 1/λ при площадях рядов <paramref name="areas"/>; 0 — нулевая нагрузка,
    /// <see cref="double.MaxValue"/> — луч не пересекает контур.</summary>
    public double Eta(ReadOnlySpan<double> areas, double n, double m) => Eta(areas, n, m, out _);

    public double Eta(ReadOnlySpan<double> areas, double n, double m, out StripNdmPivot pivot)
    {
        pivot = StripNdmPivot.None;
        double tu = n * _sc, tv = m;
        double tr = Math.Sqrt(tu * tu + tv * tv);
        if (tr < 1e-12) return 0.0;
        double du = tu / tr, dv = tv / tr;

        int nRows = _z.Length, count = _nc.Length;
        double best = double.PositiveInfinity;
        double pu0 = 0, pv0 = 0;
        for (int k = 0; k <= count; k++)
        {
            int i = k % count;
            double nn = _nc[i], mm = _mc[i];
            for (int r = 0; r < nRows; r++)
            {
                double f = _sig[i * nRows + r] * areas[r];
                nn += f; mm += f * _z[r];
            }
            double u = nn * _sc, v = mm;
            if (k == 0) { pu0 = u; pv0 = v; continue; }

            // Ребро (pu0, pv0) → (u, v) и луч t·d: a + s·e = t·d, s ∈ [0, 1], t > 0.
            double eu = u - pu0, ev = v - pv0;
            double cde = du * ev - dv * eu;
            if (Math.Abs(cde) > 1e-300)
            {
                double s = -(du * pv0 - dv * pu0) / cde;
                if (s >= 0.0 && s <= 1.0)
                {
                    double t = (pu0 + s * eu) * du + (pv0 + s * ev) * dv;
                    if (t > 0.0 && t < best) { best = t; pivot = _pivots[(k - 1) % count]; }
                }
            }
            pu0 = u; pv0 = v;
        }
        if (!double.IsFinite(best)) return double.MaxValue;
        double lambda = best / tr;
        return lambda > 1e-12 ? 1.0 / lambda : double.MaxValue;
    }
}
