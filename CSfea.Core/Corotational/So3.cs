using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Группа вращений SO(3): кососимметрия, экспонента (формула Родригеса) и
/// логарифм. Нужны для корректной работы с конечными поворотами узлов в
/// коротационных формулировках. Порт <c>corotational.py: skew/exp_so3/log_so3</c>.
/// </summary>
public static class So3
{
    /// <summary>Кососимметричная матрица [v]× (3×3).</summary>
    public static double[,] Skew(double[] v)
        => new[,]
        {
            { 0.0, -v[2], v[1] },
            { v[2], 0.0, -v[0] },
            { -v[1], v[0], 0.0 },
        };

    /// <summary>R = exp([θ]×) — формула Родригеса.</summary>
    public static double[,] Exp(double[] theta)
    {
        double a = Dense.Norm(theta);
        if (a < 1e-12)
        {
            var r0 = Identity3();
            var s0 = Skew(theta);
            return Dense.Add(r0, s0);
        }
        var k = Dense.ScaleV(theta, 1.0 / a);
        var bigK = Skew(k);
        var k2 = Dense.MatMul(bigK, bigK);
        var r = Identity3();
        double sa = Math.Sin(a), ca = Math.Cos(a);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                r[i, j] += sa * bigK[i, j] + (1.0 - ca) * k2[i, j];
        return r;
    }

    /// <summary>θ = log(R) — обратное экспоненциальному отображению.</summary>
    public static double[] Log(double[,] r)
    {
        double tr = (r[0, 0] + r[1, 1] + r[2, 2] - 1.0) * 0.5;
        tr = Math.Clamp(tr, -1.0, 1.0);
        double a = Math.Acos(tr);
        if (Math.Abs(a) < 1e-12)
        {
            return new[]
            {
                (r[2, 1] - r[1, 2]) * 0.5,
                (r[0, 2] - r[2, 0]) * 0.5,
                (r[1, 0] - r[0, 1]) * 0.5,
            };
        }
        if (Math.Abs(a - Math.PI) < 1e-6)
        {
            // поворот ~180°: R = 2nnᵀ − I, ищем ось
            var m = new double[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    m[i, j] = 0.5 * (r[i, j] + (i == j ? 1.0 : 0.0));
            int idx = 0;
            double best = m[0, 0];
            for (int i = 1; i < 3; i++)
                if (m[i, i] > best) { best = m[i, i]; idx = i; }
            double denom = Math.Sqrt(m[idx, idx]);
            var n = new[] { m[0, idx] / denom, m[1, idx] / denom, m[2, idx] / denom };
            return Dense.ScaleV(n, a);
        }
        double f = a / (2.0 * Math.Sin(a));
        return new[]
        {
            f * (r[2, 1] - r[1, 2]),
            f * (r[0, 2] - r[2, 0]),
            f * (r[1, 0] - r[0, 1]),
        };
    }

    /// <summary>Единичная матрица 3×3.</summary>
    public static double[,] Identity3()
        => new[,] { { 1.0, 0.0, 0.0 }, { 0.0, 1.0, 0.0 }, { 0.0, 0.0, 1.0 } };
}
