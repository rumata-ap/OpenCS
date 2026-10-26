namespace CScore.History;

/// <summary>
/// Диаграмма с отказом за пределом: до <see cref="LimitStrain"/> совпадает с внутренней, далее σ линейно
/// спадает до нуля на длине спада и остаётся нулевой. Бетон — предел сжатия (εb2 &lt; 0), арматура —
/// предел растяжения (εs2 &gt; 0). Изломы добавлены в характерные деформации для интегрирования по Грину.
/// Отказ обратим (нелинейная упругость); необратимость — подпроект D.
/// Ограничение: у разностной диаграммы стержня вычитаемый бетон-носитель за пределом не обнуляется.
/// </summary>
public sealed class SofteningDiagramm : Diagramm
{
    readonly double _delta;
    readonly int _sign;

    public SofteningDiagramm(Diagramm inner, double limitStrain, double softeningStrain)
        : base(inner.Ic, inner.It, inner.Type, inner.MaterialType, inner.Tag)
    {
        if (limitStrain == 0) throw new ArgumentException("Предел деформации не может быть нулевым.");
        if (softeningStrain <= 0) throw new ArgumentException("Длина спада должна быть положительной.");
        Inner = inner;
        LimitStrain = limitStrain;
        _delta = softeningStrain;
        _sign = Math.Sign(limitStrain);
        CharacteristicStrains = inner.GetCriticalStrains()
            .Concat([limitStrain, limitStrain + _sign * softeningStrain])
            .Distinct().OrderBy(x => x).ToList();
    }

    /// <summary>Исходная диаграмма материала.</summary>
    public Diagramm Inner { get; }

    /// <summary>Деформация начала спада (εb2 бетона или εs2 арматуры).</summary>
    public double LimitStrain { get; }

    /// <summary>Множитель спада и его производная по ε.</summary>
    double Factor(double eps, out double dFactor)
    {
        double beyond = (eps - LimitStrain) * _sign;
        dFactor = 0;
        if (beyond <= 0) return 1;
        if (beyond >= _delta) return 0;
        dFactor = -_sign / _delta;
        return 1 - beyond / _delta;
    }

    public override double Sig(double eps, out double E2, bool tenB = true, bool comprA = true)
    {
        double s = Inner.Sig(eps, out double e2, tenB, comprA);
        double f = Factor(eps, out double df);
        E2 = e2 * f + s * df;
        return s * f;
    }

    public override void Sig(Fiber fiber, bool tenB = true, bool comprA = true)
    {
        Inner.Sig(fiber, tenB, comprA);
        double eps = fiber.Eps + fiber.Eps_p;
        double f = Factor(eps, out double df);
        if (f == 1) return;
        double s = fiber.Sig;
        fiber.Sig = s * f;
        fiber.SigOwn *= f;
        fiber.N *= f; fiber.Mx *= f; fiber.My *= f;
        fiber.E = Math.Abs(eps) > 1e-20 ? fiber.Sig / eps : 0;
        fiber.E2 = fiber.E2 * f + s * df;
    }
}
