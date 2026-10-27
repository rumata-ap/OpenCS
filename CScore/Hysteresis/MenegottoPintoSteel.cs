namespace CScore.Hysteresis;

/// <summary>
/// Сталь Menegotto–Pinto (1973) с изменением R по Filippou, Popov, Bertero (1983):
/// σ* = b·ε* + (1 − b)·ε*/(1 + |ε*|^R)^(1/R), ε* = (ε − εr)/(ε0 − εr), σ* = (σ − σr)/(σ0 − σr),
/// R = R0·(1 − cR1·ξ/(cR2 + ξ)), ξ = |εm − ε0|/εy. Асимптоты — упругая из точки разворота и неподвижная линия текучести
/// направления нагружения (кинематическое упрочнение). Поля: P0 εr, P1 σr, P2 ε0, P3 σ0, P4 εmax, P5 εmin, P6 ξ;
/// Flags: 1 — нагружение в растяжение, 2 — в сжатие, 0 — ещё не нагружалась.
/// εm — наибольшая (для ветви растяжения) или наименьшая (сжатия) деформация за историю, ε0 — новой асимптоты;
/// начальные εmax = fyT/E, εmin = −fyC/E (так ведёт себя Steel02 OpenSees: с εmin = 0 первый разворот из текучести
/// давал ξ ≈ 1 и R ≈ 4 вместо R ≈ R0, расхождение до 90 МПа; с ±εy петли совпадают до 1e-12).
/// </summary>
public sealed class MenegottoPintoSteel : IHysteresisModel
{
    readonly double _e, _fyT, _fyC, _b, _r0, _cr1, _cr2, _ey;

    public MenegottoPintoSteel(double e, double fyT, double fyC, double b,
        double r0 = 20, double cr1 = 0.925, double cr2 = 0.15)
    {
        if (e <= 0 || fyT <= 0 || fyC <= 0 || b < 0 || b >= 1 || r0 <= 0 || cr2 <= 0)
            throw new ArgumentException("Недопустимые параметры стали Menegotto–Pinto.");
        _e = e; _fyT = fyT; _fyC = fyC; _b = b; _r0 = r0; _cr1 = cr1; _cr2 = cr2; _ey = fyT / e;
    }

    public HystState Initial => default(HystState) with { P4 = _fyT / _e, P5 = -_fyC / _e };

    /// <summary>Пересечение упругой линии из (εr, σr) с линией текучести растяжения (dir = 1) или сжатия (dir = 2).</summary>
    (double eps0, double sig0) Asymptote(double er, double sr, int dir)
    {
        double fy = dir == 1 ? _fyT : -_fyC;
        double e0 = (_e * er - sr + fy * (1 - _b)) / (_e * (1 - _b));
        return (e0, sr + _e * (e0 - er));
    }

    public double Trial(in HystState c, double eps, bool ten, out double et, out HystState next)
    {
        double er = c.P0, sr = c.P1, e0 = c.P2, s0 = c.P3, emax = c.P4, emin = c.P5, xi = c.P6;
        int dir = c.Flags;
        double de = eps - c.Eps;

        if (dir == 0)
        {
            if (de == 0) { et = _e; next = c; return 0; }
            dir = de > 0 ? 1 : 2;
            er = 0; sr = 0; xi = 0;
            (e0, s0) = dir == 1 ? (_fyT / _e, _fyT) : (-_fyC / _e, -_fyC);
        }
        else if ((dir == 1 && de < 0) || (dir == 2 && de > 0))
        {
            dir = dir == 1 ? 2 : 1;
            er = c.Eps; sr = c.Sig;
            (e0, s0) = Asymptote(er, sr, dir);
            double em = dir == 1 ? emax : emin;
            xi = Math.Abs(em - e0) / _ey;
        }

        double r = _r0 * (1 - _cr1 * xi / (_cr2 + xi));
        double span = e0 - er;
        double es = span != 0 ? (eps - er) / span : 0;
        double a = Math.Pow(1 + Math.Pow(Math.Abs(es), r), 1 / r);
        double ss = _b * es + (1 - _b) * es / a;
        double sig = ss * (s0 - sr) + sr;
        double dss = _b + (1 - _b) / (a * (1 + Math.Pow(Math.Abs(es), r)));
        et = span != 0 ? dss * (s0 - sr) / span : _e;

        next = c with
        {
            Eps = eps, Sig = sig, P0 = er, P1 = sr, P2 = e0, P3 = s0,
            P4 = Math.Max(emax, eps), P5 = Math.Min(emin, eps), P6 = xi, Flags = dir,
        };
        return sig;
    }
}
