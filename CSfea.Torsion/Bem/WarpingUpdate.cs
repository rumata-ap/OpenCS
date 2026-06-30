namespace CSfea.Torsion;

/// <summary>Поправка граничных значений депланации относительно центра кручения.
/// Порт _update_warping из GreenSectionPy.</summary>
public static class WarpingUpdate
{
    /// <summary>Пересчитывает ub, unb относительно центра кручения (xtc, ytc, ct).</summary>
    public static (double[] ub, double[] unb) Update(
        double[] xm, double[] ym, double[] enx, double[] eny,
        double[] ub, double[] unb, double xtc, double ytc, double ct)
    {
        int n = xm.Length;
        var ubN = new double[n];
        var unbN = new double[n];
        for (int i = 0; i < n; i++)
        {
            ubN[i] = ub[i] - (ytc * xm[i] - xtc * ym[i] + ct);
            unbN[i] = unb[i] - (ytc * enx[i] - xtc * eny[i]);
        }
        return (ubN, unbN);
    }
}
