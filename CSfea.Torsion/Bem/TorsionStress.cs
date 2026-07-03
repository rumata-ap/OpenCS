namespace CSfea.Torsion;

/// <summary>Безразмерное касательное напряжение τ/(GΘ) конечными разностями по 3 соседним элементам.
/// Порт _torsion_stress из GreenSectionPy. Соседство — в рамках одного контура (по J1).</summary>
public static class TorsionStress
{
    /// <summary>τ/(GΘ) на центрах элементов.</summary>
    public static double[] Compute(double[] ub, double[] xm, double[] ym,
        double xtc, double ytc, BoundaryDiscrete d)
    {
        int n = d.X.Length;

        // Длины элементов (полные)
        var sl = new double[n];
        for (int i = 0; i < n; i++)
        {
            int jn = d.J1[i];
            sl[i] = Math.Sqrt((d.X[jn] - d.X[i]) * (d.X[jn] - d.X[i]) + (d.Y[jn] - d.Y[i]) * (d.Y[jn] - d.Y[i]));
        }

        // jm1[i] — предыдущий элемент в обратном обходе: jm1[j1[i]] = i
        var jm1 = new int[n];
        for (int i = 0; i < n; i++)
            jm1[d.J1[i]] = i;

        var tau = new double[n];
        for (int i = 0; i < n; i++)
        {
            int jn = d.J1[i];
            double ax = (d.X[jn] - d.X[i]) * 0.5;
            double ay = (d.Y[jn] - d.Y[i]) * 0.5;
            double ssl = Math.Sqrt(ax * ax + ay * ay);
            double enx = ay / ssl;
            double eny = -ax / ssl;

            int ip = jm1[i];
            int inextEl = jn;
            double s1 = sl[ip] + sl[i];
            double s2 = sl[i] + sl[inextEl];
            double b1 = ub[ip], b2 = ub[i], b3 = ub[inextEl];

            double denom = s1 * s2 * (s1 + s2);
            double ubt = Math.Abs(denom) < 1e-30 ? 0.0
                : (s1 * s1 * b3 - s2 * s2 * b1 + (s2 * s2 - s1 * s1) * b2) / denom;
            tau[i] = ubt + (xm[i] - xtc) * enx + (ym[i] - ytc) * eny;
        }
        return tau;
    }
}
