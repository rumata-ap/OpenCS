namespace CSfea.Torsion;

/// <summary>Постоянная кручения It (TORSTIF) через граничный интеграл.
/// Порт _torsion_stiffness из GreenSectionPy.</summary>
public static class TorsionStiffness
{
    /// <summary>It = D = граничный интеграл относительно центра кручения.</summary>
    public static double Compute(double[] ub, double[] xm, double[] ym,
        double xtc, double ytc, BoundaryDiscrete d)
    {
        int n = d.X.Length;
        double D = 0.0;
        for (int i = 0; i < n; i++)
        {
            int jn = d.J1[i];
            double ax = (d.X[jn] - d.X[i]) * 0.5;
            double ay = (d.Y[jn] - d.Y[i]) * 0.5;
            double sl = Math.Sqrt(ax * ax + ay * ay);
            double enx = ay / sl;
            double eny = -ax / sl;
            double bx = (d.X[jn] + d.X[i]) * 0.5 - xtc;
            double by = (d.Y[jn] + d.Y[i]) * 0.5 - ytc;

            double eD = 0.0;
            for (int q = 0; q < GaussLegendre.Xi.Length; q++)
            {
                double xi = GaussLegendre.Xi[q], w = GaussLegendre.W[q];
                double xc = ax * xi + bx;
                double yc = ay * xi + by;
                eD += w * ((xc * yc * yc - yc * ub[i]) * enx
                         + (yc * xc * xc + xc * ub[i]) * eny);
            }
            D += eD * sl;
        }
        return D;
    }
}
