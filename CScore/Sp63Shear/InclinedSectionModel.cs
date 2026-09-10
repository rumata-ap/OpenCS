namespace CScore.Sp63Shear;

/// <summary>
/// Наклонное сечение с проекцией C, отложенной от стоянки в сторону опоры.
/// Точка 0 — конец сечения, противоположный проверяемой продольной арматуре.
/// </summary>
/// <param name="Station">Координата стоянки (сечения с проверяемой арматурой), м.</param>
/// <param name="Direction">Направление к опоре: +1 или −1.</param>
/// <param name="ProjectionC">Длина проекции наклонного сечения, м.</param>
public readonly record struct InclinedSectionModel(
    double Station, int Direction, double ProjectionC)
{
    /// <summary>Координата точки 0, м.</summary>
    public double Point0 => Station + Math.Sign(Direction) * ProjectionC;

    /// <summary>
    /// Поперечная сила в наклонном сечении, кН — определяется от всех внешних сил по одну
    /// сторону от сечения (п. 8.1.33), т.е. статически в самой стоянке <see cref="Station"/>
    /// (сечении с проверяемой арматурой). «Наиболее опасное загружение в пределах наклонного
    /// сечения» из формулы (8.56) — это про выбор худшего варианта загружения (сочетания
    /// нагрузок, положения подвижной нагрузки), а не про максимум Q по точкам одного и того же
    /// загружения внутри проекции C: при равномерной нагрузке Q убывает с ростом C
    /// (Q = Q_A − q·C, см. «ручной» контроль пособия к СП 63.13330.2012), поэтому нельзя брать
    /// максимум по отрезку [Station; Point0] — это ломает саму оптимизацию по C, для которой
    /// Qb растёт, а Qsw убывает с уменьшением C, и наоборот.
    /// </summary>
    public double AppliedShear(IForceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Math.Abs(profile.Q(Station));
    }

    /// <summary>Изгибающий момент относительно точки 0, кН·м.</summary>
    public double AppliedMoment(IForceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Math.Abs(profile.M(Point0));
    }

    /// <summary>Расстояние от стоянки до опоры в направлении сечения, м.</summary>
    public double SupportDistance(IForceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.SupportDistanceAt(Station, Direction);
    }
}
