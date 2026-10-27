namespace CScore.Hysteresis;

/// <summary>
/// Билинейная сталь с кинематическим упрочнением: упругий модуль E, после текучести наклон b·E; поверхность текучести
/// [−fyC, fyT] сдвигается вместе с обратным напряжением α (одномерная пластичность с линейным кинематическим
/// упрочнением, H = b·E/(1 − b)). Поля: P0 = пластическая деформация, P1 = α.
/// </summary>
public sealed class BilinearSteel : IHysteresisModel
{
    readonly double _e, _fyT, _fyC, _h;

    public BilinearSteel(double e, double fyT, double fyC, double b)
    {
        if (e <= 0 || fyT <= 0 || fyC <= 0 || b < 0 || b >= 1)
            throw new ArgumentException("Недопустимые параметры билинейной стали.");
        _e = e; _fyT = fyT; _fyC = fyC; _h = b * e / (1 - b);
    }

    public HystState Initial => default;

    public double Trial(in HystState c, double eps, bool ten, out double et, out HystState next)
    {
        double ep = c.P0, alpha = c.P1;
        double trial = _e * (eps - ep);
        double xi = trial - alpha;
        double sig = trial;
        et = _e;
        double f = xi > _fyT ? xi - _fyT : xi < -_fyC ? xi + _fyC : 0;
        if (f != 0)
        {
            double dg = f / (_e + _h);          // со знаком направления течения
            ep += dg;
            alpha += _h * dg;
            sig = trial - _e * dg;
            et = _e * _h / (_e + _h);
        }
        next = c with { Eps = eps, Sig = sig, P0 = ep, P1 = alpha };
        return sig;
    }
}
