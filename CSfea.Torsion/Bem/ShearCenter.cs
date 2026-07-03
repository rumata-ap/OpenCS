using CSfea.Sparse;

namespace CSfea.Torsion;

/// <summary>Центр кручения (TORCENTER) через СЛАУ 3×3 и три пробные гармонические функции.
/// Дословный порт _torcenter из GreenSectionPy: для каждого элемента сумма по точкам
/// Гаусса умножается на sl (полудлину) и накапливается в общую величину.</summary>
public static class ShearCenter
{
    /// <summary>Возвращает центр жёсткости (xtc, ytc) и константу ct.</summary>
    public static (double xtc, double ytc, double ct) Compute(double[] ub, double[] unb, BoundaryDiscrete d)
    {
        int n = d.X.Length;
        double area = 0.0, sx = 0.0, sy = 0.0, aix = 0.0, aiy = 0.0, aixy = 0.0;
        double ai1 = 0.0, ai2 = 0.0, ai3 = 0.0;

        for (int i = 0; i < n; i++)
        {
            int jn = d.J1[i];
            double ax = (d.X[jn] - d.X[i]) * 0.5;
            double ay = (d.Y[jn] - d.Y[i]) * 0.5;
            double bx = (d.X[jn] + d.X[i]) * 0.5;
            double by = (d.Y[jn] + d.Y[i]) * 0.5;
            double sl = Math.Sqrt(ax * ax + ay * ay);
            double enx = ay / sl;
            double eny = -ax / sl;

            // Локальные суммы по 4 точкам Гаусса для этого элемента
            double eArea = 0, eSx = 0, eSy = 0, eAix = 0, eAiy = 0, eAixy = 0;
            double eAi1 = 0, eAi2 = 0, eAi3 = 0;
            for (int q = 0; q < GaussLegendre.Xi.Length; q++)
            {
                double xi = GaussLegendre.Xi[q], w = GaussLegendre.W[q];
                double xc = ax * xi + bx;
                double yc = ay * xi + by;
                eArea += 0.5 * (xc * enx + yc * eny) * w;
                eSx += 0.5 * yc * yc * eny * w;
                eSy += 0.5 * xc * xc * enx * w;
                eAix += (1.0 / 3.0) * yc * yc * yc * eny * w;
                eAiy += (1.0 / 3.0) * xc * xc * xc * enx * w;
                eAixy += 0.25 * xc * yc * (xc * enx + yc * eny) * w;

                double u1 = (xc * xc * yc + yc * yc * yc) / 8.0;
                double u1n = (2.0 * xc * yc * enx + (xc * xc + 3.0 * yc * yc) * eny) / 8.0;
                eAi1 += (ub[i] * u1n - unb[i] * u1) * w;

                double u2 = (xc * xc * xc + xc * yc * yc) / 8.0;
                double u2n = ((3.0 * xc * xc + yc * yc) * enx + 2.0 * xc * yc * eny) / 8.0;
                eAi2 += (ub[i] * u2n - unb[i] * u2) * w;

                double u3 = (xc * xc + yc * yc) / 4.0;
                double u3n = (xc * enx + yc * eny) / 2.0;
                eAi3 += (ub[i] * u3n - unb[i] * u3) * w;
            }
            area += eArea * sl; sx += eSx * sl; sy += eSy * sl;
            aix += eAix * sl; aiy += eAiy * sl; aixy += eAixy * sl;
            ai1 += eAi1 * sl; ai2 += eAi2 * sl; ai3 += eAi3 * sl;
        }

        var AA = new double[3, 3]
        {
            { aix, -aixy, -sx },
            { -aixy, aiy, sy },
            { sx, -sy, -area }
        };
        var BB = new double[] { -ai1, ai2, -ai3 };
        double[] sol = DenseLinAlg.Solve(AA, BB);
        return (sol[0], sol[1], sol[2]);
    }
}
