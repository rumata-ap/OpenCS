namespace CScore.LimitSurface;

/// <summary>
/// Область допустимых плоскостей деформаций сечения: каждая контрольная точка в своих пределах,
/// для бетона — с правилом п. 8.1.30 СП 63 (ф. (8.53)) при однозначной эпюре. Граница по лучу
/// находится в замкнутом виде: отношение ε1/ε2 от масштаба луча не зависит.
/// </summary>
public sealed class StrainDomain
{
    const double Tiny = 1e-15;

    /// <summary>Контрольные точки.</summary>
    public IReadOnlyList<StrainControlPoint> Points { get; }

    /// <summary>Применять (8.53); выключение — только для сверки с решателями, берущими εb2 всегда.</summary>
    internal bool ApplySingleSignRule { get; private init; } = true;

    /// <summary>Создаёт область; требует хотя бы одну бетонную точку и положительные пределы стержней.</summary>
    public StrainDomain(IReadOnlyList<StrainControlPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (!points.Any(p => p.Kind == StrainPointKind.Concrete))
            throw new ArgumentException("В области деформаций нет бетонных точек.", nameof(points));
        foreach (var p in points.Where(p => p.Kind == StrainPointKind.Rebar))
            if (!(p.EpsMax > 0))
                throw new ArgumentException(
                    $"Стержень ({p.X:G4}; {p.Y:G4}): предварительная деформация не меньше предельной (εs,ult − εp = {p.EpsMax:G4}).",
                    nameof(points));
        Points = points;
    }

    /// <summary>Копия области с включённым/выключенным правилом (8.53).</summary>
    internal StrainDomain WithSingleSignRule(bool apply) => new(Points) { ApplySingleSignRule = apply };

    /// <summary>
    /// Масштаб λ луча ε = λ·(a + by·y + bz·x) до границы области и активное ограничение.
    /// λ = +∞ (PointIndex = −1), если по лучу ни один предел не достигается.
    /// </summary>
    public (double Lambda, ActiveConstraint Active) Boundary(double a, double by, double bz)
    {
        double vmax = double.NegativeInfinity, vmin = double.PositiveInfinity;
        foreach (var p in Points)
        {
            if (p.Kind != StrainPointKind.Concrete) continue;
            double v = a + by * p.Y + bz * p.X;
            if (v > vmax) vmax = v;
            if (v < vmin) vmin = v;
        }
        bool single = ApplySingleSignRule && vmax <= 0 && vmin < -Tiny;
        double r = single ? vmax / vmin : 0.0;

        double lamMin = double.PositiveInfinity;
        var active = new ActiveConstraint(-1, 0, single);
        for (int i = 0; i < Points.Count; i++)
        {
            var p = Points[i];
            double v = a + by * p.Y + bz * p.X;
            double lo, hi;
            if (p.Kind == StrainPointKind.Concrete)
            {
                lo = single ? p.EpsB2 - (p.EpsB2 - p.EpsB0) * r : p.EpsB2;
                hi = double.PositiveInfinity;
            }
            else
            {
                lo = p.EpsMin;
                hi = p.EpsMax;
            }

            double lam; int side;
            if (v > Tiny) { lam = hi / v; side = 1; }
            else if (v < -Tiny) { lam = lo / v; side = -1; }
            else continue;

            if (lam > 0 && lam < lamMin)
            {
                lamMin = lam;
                active = new ActiveConstraint(i, side, single);
            }
        }
        return (lamMin, active);
    }
}
