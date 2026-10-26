namespace CScore.History;

/// <summary>
/// Равновесие при заданной проекции кривизны c = ky·cos φ + kz·sin φ: неизвестные e0 и
/// κ⊥ = −ky·sin φ + kz·cos φ; уравнения N(K) = N₀ и M⊥ = −Mx·sin φ + My·cos φ = 0.
/// Ньютон 2×2 с якобианом из <see cref="StrainSolver.EvaluateJacobian(Kurvature)"/> и демпфированием шага.
/// </summary>
internal sealed class CurvatureControlSolver
{
    const int MaxBacktracks = 20;
    readonly CrossSection _section;
    readonly LoadHistoryOptions _o;
    readonly StrainSolver _jac;

    public CurvatureControlSolver(CrossSection section, LoadHistoryOptions o)
    {
        _section = section;
        _o = o;
        _jac = new StrainSolver(section, o.Calc, ten: o.Ten, tol: o.Tol, maxIter: o.MaxIter,
            h: o.H, centralJacobian: o.CentralJacobian);
    }

    /// <summary>Плоскость по e0, проекции c и поперечной кривизне κ⊥ при направлении φ.</summary>
    public static Kurvature Compose(double e0, double c, double kPerp, double phi)
    {
        double cos = Math.Cos(phi), sin = Math.Sin(phi);
        return new Kurvature { e0 = e0, ky = c * cos - kPerp * sin, kz = c * sin + kPerp * cos };
    }

    public bool TrySolve(double n0, double phi, double c, Kurvature guess,
        out Kurvature k, out int iterations, out double residual)
    {
        double cos = Math.Cos(phi), sin = Math.Sin(phi);
        double a = guess.e0;
        double b = -guess.ky * sin + guess.kz * cos;
        k = Compose(a, c, b, phi);
        var (r0, r1) = Res(k);
        residual = Math.Sqrt(r0 * r0 + r1 * r1);

        for (iterations = 1; iterations <= _o.MaxIter; iterations++)
        {
            if (residual < _o.Tol) return true;
            var j = _jac.EvaluateJacobian(k).ToArray(); // строки N, Mx, My; столбцы e0, ky, kz
            // dK/da = (1, 0, 0); dK/db = (0, −sin, cos); строка M⊥ = −sin·Mx + cos·My
            double j00 = j[0, 0];
            double j01 = -j[0, 1] * sin + j[0, 2] * cos;
            double j10 = -sin * j[1, 0] + cos * j[2, 0];
            double j11 = (-sin * j[1, 1] + cos * j[2, 1]) * -sin + (-sin * j[1, 2] + cos * j[2, 2]) * cos;
            double det = j00 * j11 - j01 * j10;
            if (Math.Abs(det) < 1e-30) return false;
            double da = (r0 * j11 - r1 * j01) / det;
            double db = (j00 * r1 - j10 * r0) / det;

            double lambda = 1;
            bool improved = false;
            for (int bt = 0; bt <= MaxBacktracks; bt++, lambda *= 0.5)
            {
                var kt = Compose(a - lambda * da, c, b - lambda * db, phi);
                var (t0, t1) = Res(kt);
                double rt = Math.Sqrt(t0 * t0 + t1 * t1);
                if (rt < residual)
                {
                    a -= lambda * da; b -= lambda * db;
                    k = kt; r0 = t0; r1 = t1; residual = rt;
                    improved = true;
                    break;
                }
            }
            if (!improved) return residual < _o.Tol;
        }
        return residual < _o.Tol;

        (double, double) Res(Kurvature kk)
        {
            var f = _section.Integral(kk, _o.Calc, _o.Ten);
            return (f.N - n0, -f.Mx * sin + f.My * cos);
        }
    }
}
