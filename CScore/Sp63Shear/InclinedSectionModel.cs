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
    /// Поперечная сила в наклонном сечении, кН — наибольшая по модулю в пределах
    /// наклонного сечения (п. 8.1.33, «наиболее опасное загружение»). Максимум точный:
    /// его даёт сам профиль, а не выборка по сетке.
    /// </summary>
    public double AppliedShear(IForceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.MaxAbsQ(Station, Point0);
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
