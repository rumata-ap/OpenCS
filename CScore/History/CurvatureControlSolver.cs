namespace CScore.History;

/// <summary>
/// Равновесие при заданной проекции кривизны c = ky·cos φ + kz·sin φ: неизвестные e0 и
/// κ⊥ = −ky·sin φ + kz·cos φ; уравнения N(K) = N₀ и M⊥ = −Mx·sin φ + My·cos φ = 0.
/// Ньютон 2×2 с якобианом из <see cref="StrainSolver.EvaluateJacobian(Kurvature)"/> и демпфированием шага.
/// Застрявший Ньютон продолжается брекетингом (<see cref="TryBracket"/>): в режиме «по волокнам»
/// с ролью «Разрушается» (с гистерезисом и без) N(e0) пилообразна (ряды волокон по очереди проходят короткий спад) и Ньютон на ней
/// останавливается, хотя корень есть.
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
        if (TryNewton(n0, phi, c, guess, out k, out iterations, out residual)) return true;
        double sin = Math.Sin(phi), cos = Math.Cos(phi);
        double a0 = k.e0, b0 = -k.ky * sin + k.kz * cos;
        if (!TryBracket(n0, phi, c, a0, b0, out var kb, out int itb, out double rb)) return false;
        k = kb; iterations += itb; residual = rb;
        return true;
    }

    bool TryNewton(double n0, double phi, double c, Kurvature guess,
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

    /// <summary>
    /// Запасной путь: при фиксированных c и κ⊥ e0 ищется бисекцией по N (интервал расширяется от начального
    /// приближения до смены знака), κ⊥ — секущей по M⊥ = 0 поверх внутренней бисекции.
    /// </summary>
    bool TryBracket(double n0, double phi, double c, double a0, double b0,
        out Kurvature k, out int iterations, out double residual)
    {
        double sin = Math.Sin(phi), cos = Math.Cos(phi);
        int evals = 0;
        k = Compose(a0, c, b0, phi);
        residual = double.PositiveInfinity;

        (double N, double MPerp) F(double a, double b)
        {
            evals++;
            var f = _section.Integral(Compose(a, c, b, phi), _o.Calc, _o.Ten);
            return (f.N - n0, -f.Mx * sin + f.My * cos);
        }

        // e0 при заданном κ⊥: смена знака N − N₀ и бисекция; null — интервал не найден.
        (double a, double rN, double rM)? SolveE0(double b)
        {
            double fa = F(a0, b).N;
            if (Math.Abs(fa) < 0.5 * _o.Tol) { var f0 = F(a0, b); return (a0, f0.N, f0.MPerp); }
            double step = Math.Max(1e-5, 2e-3 * Math.Abs(c));
            double lo = a0, flo = fa, hi = double.NaN, fhi = double.NaN;
            for (int n = 0; n < 40 && double.IsNaN(hi); n++, step *= 1.6)
                foreach (double sgn in (double[])[1, -1])
                {
                    double at = a0 + sgn * step, ft = F(at, b).N;
                    if (ft * fa <= 0) { lo = a0; flo = fa; hi = at; fhi = ft; break; }
                }
            if (double.IsNaN(hi)) return null;
            for (int n = 0; n < 100; n++)
            {
                double mid = 0.5 * (lo + hi), fm = F(mid, b).N;
                if (Math.Abs(fm) < 0.5 * _o.Tol || Math.Abs(hi - lo) < 1e-15)
                {
                    var r = F(mid, b);
                    return (mid, r.N, r.MPerp);
                }
                if (fm * flo > 0) { lo = mid; flo = fm; } else { hi = mid; fhi = fm; }
            }
            var last = F(0.5 * (lo + hi), b);
            return (0.5 * (lo + hi), last.N, last.MPerp);
        }

        var s0 = SolveE0(b0);
        iterations = evals;
        if (s0 is null) return false;
        double bPrev = b0, gPrev = s0.Value.rM;
        var best = (a: s0.Value.a, b: b0, rN: s0.Value.rN, rM: s0.Value.rM);
        double db = Math.Max(1e-7, 1e-3 * Math.Abs(c));
        double bCur = b0 + db;
        for (int it = 0; it < 30 && Math.Abs(best.rM) >= 0.5 * _o.Tol; it++)
        {
            var sc = SolveE0(bCur);
            if (sc is null) break;
            var (a, rN, rM) = sc.Value;
            if (Math.Abs(rM) < Math.Abs(best.rM)) best = (a, bCur, rN, rM);
            if (Math.Abs(rM - gPrev) < 1e-300) break;
            double bNext = bCur - rM * (bCur - bPrev) / (rM - gPrev);
            bPrev = bCur; gPrev = rM; bCur = bNext;
        }
        iterations = evals;
        k = Compose(best.a, c, best.b, phi);
        residual = Math.Sqrt(best.rN * best.rN + best.rM * best.rM);
        return residual < _o.Tol;
    }
}
