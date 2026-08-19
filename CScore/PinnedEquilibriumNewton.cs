namespace CScore;

/// <summary>
/// Результат Ньютон-решения равновесия с подстановкой деформации в фиксированной
/// ("pin") точке контура/арматуры вместо явного решения по e0. Общее ядро, используемое
/// <see cref="LimitForceSolverFast"/> и пин-ориентированными решателями
/// <see cref="TensionPinSolverFast"/>/<see cref="CompressionPinSolverFast"/>/
/// <see cref="GoverningPinSolverFast"/>.
/// </summary>
public readonly record struct PinnedNewtonResult(
    double Kx, double Ky, double K, Kurvature Plane, bool Converged, int Iterations);

/// <summary>
/// Ньютон 3×3 по неизвестным (kx,ky,k) с явной подстановкой деформации в pin-точке
/// (<c>e0 = epsPin - ky·yA - kx·xA</c>), убирающей одну степень свободы и дающей хорошо
/// обусловленную систему. Извлечено из <see cref="LimitForceSolverFast"/> (см. память проекта
/// limitforcesolverfast-debug.md) без изменения самой механики — только generalized
/// на произвольный <paramref name="epsPin"/> вместо константы предельной деформации.
/// </summary>
public static class PinnedEquilibriumNewton
{
    /// <summary>
    /// Решает систему N=nFn(k), Mx=mxFn(k), My=myFn(k) методом Ньютона по (kx,ky,k) с
    /// подстановкой деформации <paramref name="epsPin"/> в точке (<paramref name="xA"/>,
    /// <paramref name="yA"/>). <paramref name="forces"/> — вычислитель усилий сечения на
    /// заданной плоскости (инкапсулирует CalcType/ten/ψs — единая точка расширения для всех
    /// вызывающих сторон).
    /// </summary>
    public static PinnedNewtonResult Solve(
        Func<Kurvature, (double N, double Mx, double My)> forces,
        double xA, double yA, double epsPin,
        double kx0, double ky0, double k0,
        Func<double, double> nFn, Func<double, double> mxFn, Func<double, double> myFn,
        double dNdk, double dMxdk, double dMydk,
        double yRef, double xRef,
        double hDiff = 1e-6, int maxIter = 60, double relTol = 1e-3)
    {
        double yr = Math.Max(yRef, 1e-12), xr = Math.Max(xRef, 1e-12);
        double ke = Math.Max(Math.Abs(k0), 1e-6);
        double Kx = kx0 * yr, Ky = ky0 * xr, K = k0 / ke;
        double h = hDiff;
        Kurvature sp = MakeSp(kx0, ky0, xA, yA, epsPin);

        double ResidualTol(double k)
        {
            double nT = nFn(k), mxT = mxFn(k), myT = myFn(k);
            double mag = Math.Sqrt(nT * nT + mxT * mxT + myT * myT);
            return Math.Max(relTol * Math.Max(mag, 1e-9), 1e-9);
        }

        for (int iter = 1; iter <= maxIter; iter++)
        {
            Unscale(Kx, Ky, K, yr, xr, ke, out double kx, out double ky, out double k);
            sp = MakeSp(kx, ky, xA, yA, epsPin);
            var f0 = forces(sp);
            double g0 = f0.N - nFn(k);
            double g1 = f0.Mx - mxFn(k);
            double g2 = f0.My - myFn(k);
            double norm = Math.Sqrt(g0 * g0 + g1 * g1 + g2 * g2);
            if (norm <= ResidualTol(k))
                return new PinnedNewtonResult(kx, ky, k, sp, true, iter);

            double hKx = Kx != 0 ? Math.Max(h, Math.Abs(Kx) * 1e-4) : h;
            double hKy = Ky != 0 ? Math.Max(h, Math.Abs(Ky) * 1e-4) : h;

            Unscale(Kx + hKx, Ky, K, yr, xr, ke, out double kxH, out double kyH, out _);
            var fKx = forces(MakeSp(kxH, kyH, xA, yA, epsPin));
            Unscale(Kx, Ky + hKy, K, yr, xr, ke, out kxH, out kyH, out _);
            var fKy = forces(MakeSp(kxH, kyH, xA, yA, epsPin));
            double[,] j = new double[3, 3]
            {
                { (fKx.N - f0.N) / hKx, (fKy.N - f0.N) / hKy, -dNdk * ke },
                { (fKx.Mx - f0.Mx) / hKx, (fKy.Mx - f0.Mx) / hKy, -dMxdk * ke },
                { (fKx.My - f0.My) / hKx, (fKy.My - f0.My) / hKy, -dMydk * ke },
            };

            if (!GaussSolve(j, [-g0, -g1, -g2], out double[] delta))
            {
                for (int i = 0; i < 3; i++) j[i, i] += 1e-4;
                if (!GaussSolve(j, [-g0, -g1, -g2], out delta))
                    return new PinnedNewtonResult(kx, ky, k, sp, false, iter);
            }

            double alpha = 1.0;
            for (int ls = 0; ls < 8; ls++)
            {
                double Ktry = K + alpha * delta[2];
                if (Ktry <= 0) { alpha *= 0.5; continue; }
                Unscale(Kx + alpha * delta[0], Ky + alpha * delta[1], Ktry, yr, xr, ke,
                    out double kxn, out double kyn, out double kn);
                var fn = forces(MakeSp(kxn, kyn, xA, yA, epsPin));
                double normNew = Math.Sqrt(
                    Math.Pow(fn.N - nFn(kn), 2) +
                    Math.Pow(fn.Mx - mxFn(kn), 2) +
                    Math.Pow(fn.My - myFn(kn), 2));
                if (normNew < norm) break;
                alpha *= 0.5;
            }

            Kx += alpha * delta[0];
            Ky += alpha * delta[1];
            K += alpha * delta[2];
        }

        Unscale(Kx, Ky, K, yr, xr, ke, out double kxf, out double kyf, out double kf);
        sp = MakeSp(kxf, kyf, xA, yA, epsPin);
        return new PinnedNewtonResult(kxf, kyf, kf, sp, false, maxIter);
    }

    /// <summary>Строит плоскость деформаций с подстановкой pin-условия (см. класс).</summary>
    public static Kurvature MakeSp(double kx, double ky, double xA, double yA, double epsPin)
        => new() { e0 = epsPin - ky * yA - kx * xA, ky = ky, kz = kx };

    static void Unscale(double Kx, double Ky, double K, double yr, double xr, double ke,
        out double kx, out double ky, out double k)
    {
        kx = Kx / yr;
        ky = Ky / xr;
        k = K * ke;
    }

    /// <summary>Метод Гаусса с выбором ведущего элемента для системы 3×3.</summary>
    public static bool GaussSolve(double[,] a, double[] b, out double[] x)
    {
        x = new double[3];
        double[,] m = (double[,])a.Clone();
        double[] v = (double[])b.Clone();
        const int n = 3;

        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < n; row++)
                if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col]))
                    pivot = row;

            double pivVal = m[pivot, col];
            if (!double.IsFinite(pivVal) || Math.Abs(pivVal) < 1e-15)
                return false;

            if (pivot != col)
            {
                for (int k2 = 0; k2 < n; k2++)
                    (m[col, k2], m[pivot, k2]) = (m[pivot, k2], m[col, k2]);
                (v[col], v[pivot]) = (v[pivot], v[col]);
            }

            for (int row = col + 1; row < n; row++)
            {
                double factor = m[row, col] / m[col, col];
                for (int k2 = col; k2 < n; k2++)
                    m[row, k2] -= factor * m[col, k2];
                v[row] -= factor * v[col];
            }
        }

        for (int row = n - 1; row >= 0; row--)
        {
            double sum = v[row];
            for (int k2 = row + 1; k2 < n; k2++)
                sum -= m[row, k2] * x[k2];
            x[row] = sum / m[row, row];
        }

        return true;
    }
}
