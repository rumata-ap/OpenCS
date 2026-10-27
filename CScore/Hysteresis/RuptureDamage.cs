namespace CScore.Hysteresis;

/// <summary>
/// Необратимый отказ стали (роль «Разрушается»): σ = σinner·F(εmax), F = 1 до предела, линейно до 0 на длине спада,
/// далее 0. Считается по наибольшей достигнутой деформации (Aux), поэтому не восстанавливается при разгрузке.
/// В состоянии остаётся σ внутренней модели (без F): она служит внутренней модели точкой разворота.
/// </summary>
public sealed class RuptureDamage : IHysteresisModel
{
    readonly IHysteresisModel _inner;
    readonly double _limit, _delta;

    public RuptureDamage(IHysteresisModel inner, double limit, double softening)
    {
        if (limit <= 0 || softening <= 0) throw new ArgumentException("Предел и длина спада должны быть положительными.");
        _inner = inner; _limit = limit; _delta = softening;
    }

    public HystState Initial => _inner.Initial;

    double Factor(double emax) =>
        emax <= _limit ? 1 : emax >= _limit + _delta ? 0 : 1 - (emax - _limit) / _delta;

    public double Trial(in HystState c, double eps, bool ten, out double et, out HystState next)
    {
        double s = _inner.Trial(c, eps, ten, out double ei, out var n);
        double emax = Math.Max(c.Aux, eps);
        double f = Factor(emax);
        et = ei * f;
        if (eps > c.Aux && emax > _limit && emax < _limit + _delta) et += s * (-1 / _delta);
        next = n with { Aux = emax };
        return s * f;
    }
}
