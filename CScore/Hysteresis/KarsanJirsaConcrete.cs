namespace CScore.Hysteresis;

/// <summary>
/// Бетон с состоянием поверх огибающей-диаграммы. Сжатие: остаточная деформация Karsan–Jirsa (1969), двухветвевая
/// формула по Taucer, Spacone, Filippou (1991); разгрузка и повторное нагружение — по прямой через (εp, 0) и (εmin, σmin).
/// Растяжение (ten) отсчитывается от εp: до трещины — секущая к нулю, после εbt2 — трещина навсегда (σ = 0),
/// при сжатии трещина закрывается. Поля: P0 = εmin, P1 = σmin, P2 = εp, P3 = εt,max, P4 = σ(εt,max), Flags bit0 = трещина.
/// </summary>
public sealed class KarsanJirsaConcrete : IHysteresisModel
{
    const int Cracked = 1;
    readonly Diagramm _env;
    readonly double _tensionEnd;
    readonly double _e0Tension;

    /// <param name="envelope">Огибающая — диаграмма бетона области (σ в кПа, сжатие отрицательно).</param>
    public KarsanJirsaConcrete(Diagramm envelope)
    {
        _env = envelope;
        PeakStrain = FindPeak(envelope);
        _tensionEnd = envelope.It.X[^1];
        envelope.Sig(1e-9, out _e0Tension, true);
    }

    /// <summary>Деформация пика сжатия огибающей εc0 (&lt; 0).</summary>
    public double PeakStrain { get; }

    public HystState Initial => default;

    /// <summary>Остаточная деформация после разгрузки из εmin: η = εmin/εc0.</summary>
    public static double ResidualStrain(double epsMin, double ec0)
    {
        if (epsMin >= 0) return 0;
        double eta = epsMin / ec0;
        return eta < 2 ? ec0 * (0.145 * eta * eta + 0.13 * eta) : ec0 * (0.707 * (eta - 2) + 0.834);
    }

    /// <summary>
    /// Пик сжатия огибающей (один раз при построении): перебор по сетке и узлам ветви сжатия, затем уточнение
    /// золотым сечением около лучшей точки (у кусочно-линейной ветви сходится к узлу).
    /// </summary>
    static double FindPeak(Diagramm d)
    {
        double lo = d.Ic.X[0], best = 0, bestSig = 0;
        const int n = 4000;
        double Sig(double e) => d.Sig(e, out _, false);
        void Try(double e)
        {
            if (e >= 0 || e < lo) return;
            double s = Sig(e);
            if (s < bestSig) { bestSig = s; best = e; }
        }
        for (int i = 1; i <= n; i++) Try(lo * i / n);
        foreach (double x in d.Ic.X) Try(x);
        if (best >= 0) return -0.002;

        double h = Math.Abs(lo) / n;
        double a = Math.Max(lo, best - h), b = Math.Min(0, best + h);
        const double g = 0.6180339887498949;
        double x1 = b - g * (b - a), x2 = a + g * (b - a), f1 = Sig(x1), f2 = Sig(x2);
        for (int k = 0; k < 100 && b - a > 1e-15; k++)
        {
            if (f1 < f2) { b = x2; x2 = x1; f2 = f1; x1 = b - g * (b - a); f1 = Sig(x1); }
            else { a = x1; x1 = x2; f1 = f2; x2 = a + g * (b - a); f2 = Sig(x2); }
        }
        Try(0.5 * (a + b));
        return best;
    }

    public double Trial(in HystState c, double eps, bool ten, out double et, out HystState next)
    {
        double epsMin = c.P0, sigMin = c.P1, ep = c.P2, etMax = c.P3, sigT = c.P4;
        int flags = c.Flags;
        double sig;
        if (eps <= epsMin && eps < 0)
        {
            // огибающая сжатия: новое наибольшее сжатие
            sig = _env.Sig(eps, out et, false);
            epsMin = eps; sigMin = sig; ep = ResidualStrain(eps, PeakStrain);
        }
        else if (eps < ep)
        {
            // разгрузка / повторное нагружение по прямой через (εp, 0) и (εmin, σmin)
            et = sigMin / (epsMin - ep);
            sig = et * (eps - ep);
        }
        else
        {
            sig = 0; et = 0;
            if (ten && (flags & Cracked) == 0)
            {
                double t = eps - ep;
                if (t <= etMax)
                {
                    et = etMax > 0 ? sigT / etMax : _e0Tension;
                    sig = etMax > 0 ? sigT * t / etMax : 0;
                }
                else if (t > _tensionEnd)
                {
                    flags |= Cracked; sig = 0; et = 0;
                }
                else
                {
                    sig = _env.Sig(t, out et, true);
                    etMax = t; sigT = sig;
                }
            }
        }
        next = c with { Eps = eps, Sig = sig, P0 = epsMin, P1 = sigMin, P2 = ep, P3 = etMax, P4 = sigT, Flags = flags };
        return sig;
    }
}
