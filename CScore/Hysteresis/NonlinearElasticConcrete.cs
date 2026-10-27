namespace CScore.Hysteresis;

/// <summary>
/// Бетон без памяти: σ всегда по диаграмме области, разгрузка идёт по кривой нагружения, остаточных деформаций нет.
/// Позволяет при гистерезисной стали отделить её вклад в остаточные деформации от вклада бетона.
/// </summary>
public sealed class NonlinearElasticConcrete(Diagramm envelope) : IHysteresisModel
{
    public HystState Initial => default;

    public double Trial(in HystState c, double eps, bool ten, out double et, out HystState next)
    {
        double sig = envelope.Sig(eps, out et, ten);
        next = c with { Eps = eps, Sig = sig };
        return sig;
    }
}
