namespace CScore.Sp63Shear;

/// <summary>
/// Аналитический профиль при равномерно распределённой нагрузке:
/// Q(s) = Q₀ − q·s, M(s) = M₀ + Q₀·s − q·s²/2.
/// Флаги <paramref name="supportAtStart"/> и <paramref name="supportAtEnd"/> задают, какой
/// из концов области определения действительно является опорой: приопорные поправки
/// и ограничение проекции наклонного сечения применяются только к объявленным опорам.
/// </summary>
/// <param name="q0">Поперечная сила в начале участка, кН.</param>
/// <param name="m0">Изгибающий момент в начале участка, кН·м.</param>
/// <param name="n0">Продольная сила, принятая постоянной по длине, кН.</param>
/// <param name="distributedLoad">Равномерно распределённая нагрузка, кН/м.</param>
/// <param name="supportDistance">Длина участка до второй опоры, м.</param>
/// <param name="supportAtStart">Начало участка является опорой.</param>
/// <param name="supportAtEnd">Конец участка является опорой.</param>
public sealed class UniformLoadProfile(
    double q0, double m0, double n0, double distributedLoad, double supportDistance,
    bool supportAtStart = true, bool supportAtEnd = true)
    : IForceProfile
{
    /// <summary>Поперечная сила в сечении s, кН.</summary>
    public double Q(double s) => q0 - distributedLoad * s;

    /// <summary>Изгибающий момент в сечении s, кН·м.</summary>
    public double M(double s) => m0 + q0 * s - 0.5 * distributedLoad * s * s;

    /// <summary>Продольная сила, принятая постоянной по длине, кН.</summary>
    public double N(double s) => n0;

    /// <summary>Длина области определения, м.</summary>
    public double Length => supportDistance > 0.0 ? supportDistance : 0.0;

    /// <summary>Стоянки от расчётного сечения до опоры.</summary>
    public (double Min, double Max) StationRange => (0.0, Length);

    /// <summary>Расстояние от стоянки до опоры в заданном направлении, м.</summary>
    public double SupportDistanceAt(double station, int direction)
    {
        if (supportDistance <= 0.0) return 0.0;
        if (direction >= 0)
            return supportAtEnd ? Math.Max(supportDistance - station, 0.0) : 0.0;
        return supportAtStart ? Math.Max(station, 0.0) : 0.0;
    }

    /// <summary>Опора в заданном направлении объявлена и её положение известно.</summary>
    public bool HasSupport(int direction) =>
        supportDistance > 0.0 && (direction >= 0 ? supportAtEnd : supportAtStart);

    /// <summary>Максимум |Q| на отрезке: Q(s) линейна, поэтому достигается на его конце.</summary>
    public double MaxAbsQ(double from, double to) =>
        Math.Max(Math.Abs(Q(from)), Math.Abs(Q(to)));
}
