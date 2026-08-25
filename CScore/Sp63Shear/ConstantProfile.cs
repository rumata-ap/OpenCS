namespace CScore.Sp63Shear;

/// <summary>
/// Усилия приняты постоянными на длине наклонного сечения: расчёт по одной строке
/// набора усилий без сведений о распределении вдоль элемента.
/// </summary>
/// <param name="q">Поперечная сила, кН.</param>
/// <param name="m">Изгибающий момент, кН·м.</param>
/// <param name="n">Продольная сила, кН.</param>
/// <param name="supportDistance">Расстояние до опоры, м; 0 — не задано.</param>
public sealed class ConstantProfile(double q, double m, double n, double supportDistance)
    : IForceProfile
{
    /// <summary>Поперечная сила, кН.</summary>
    public double Q(double s) => q;

    /// <summary>Изгибающий момент, кН·м.</summary>
    public double M(double s) => m;

    /// <summary>Продольная сила, кН.</summary>
    public double N(double s) => n;

    /// <summary>Длина области определения, м.</summary>
    public double Length => 0.0;

    /// <summary>Единственная стоянка в начале координат.</summary>
    public (double Min, double Max) StationRange => (0.0, 0.0);

    /// <summary>Расстояние до опоры, заданное параметрами задачи.</summary>
    public double SupportDistanceAt(double station, int direction) => supportDistance;

    /// <summary>Опора учитывается, только если расстояние до неё задано.</summary>
    public bool HasSupport(int direction) => supportDistance > 0.0;

    /// <summary>Поперечная сила постоянна — максимум равен её модулю.</summary>
    public double MaxAbsQ(double from, double to) => Math.Abs(q);
}
