namespace CSfea.Torsion;

/// <summary>Сборка плотных матриц влияния G (потенциал) и H (телесный угол).</summary>
public static class BemMatrices
{
    /// <summary>
    /// Строит матрицы G (n×n) и H (n×n) для постоянных граничных элементов,
    /// а также координаты центров элементов xm, ym и нормалей enx, eny.
    /// d.J1 — индекс следующей вершины (замыкание по контуру).
    /// </summary>
    public static (double[,] G, double[,] H, double[] xm, double[] ym, double[] enx, double[] eny)
        Build(BoundaryDiscrete d)
    {
        int n = d.X.Length;
        var G = new double[n, n];
        var H = new double[n, n];
        var xm = new double[n];
        var ym = new double[n];
        var enx = new double[n];
        var eny = new double[n];
        var half = new double[n];

        for (int i = 0; i < n; i++)
        {
            int j1 = d.J1[i];
            double dx = d.X[j1] - d.X[i], dy = d.Y[j1] - d.Y[i];
            half[i] = 0.5 * Math.Sqrt(dx * dx + dy * dy);
            xm[i] = (d.X[i] + d.X[j1]) * 0.5;
            ym[i] = (d.Y[i] + d.Y[j1]) * 0.5;
            // Внешняя нормаль при CCW-обходе: n = (dy, -dx)/|e| (как в _neumann_bc/_torcenter: enx=ay/sl, eny=-ax/sl)
            double sl = 2.0 * half[i];
            enx[i] = dy / sl;
            eny[i] = -dx / sl;
        }

        for (int i = 0; i < n; i++)
        {
            int j1 = d.J1[i];
            for (int j = 0; j < n; j++)
            {
                int jn = d.J1[j];
                if (i == j)
                {
                    G[i, j] = BemKernels.Slintc(half[j]);
                    H[i, j] = -0.5;
                }
                else
                {
                    G[i, j] = BemKernels.Rlintc(xm[i], ym[i], d.X[j], d.Y[j], d.X[jn], d.Y[jn], half[j]);
                    H[i, j] = BemKernels.Dalpha(xm[i], ym[i], d.X[j], d.Y[j], d.X[jn], d.Y[jn]);
                }
            }
        }
        return (G, H, xm, ym, enx, eny);
    }
}
