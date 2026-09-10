using CScore.Planar;

namespace CScore.Submodel;

/// <summary>Угловые вычисления анализатора. Все углы — в градусах.</summary>
public static class ChainAngleMath
{
    /// <summary>Угол между прямыми, а не между направленными векторами.</summary>
    public static double AngleBetweenLinesDeg(PlanarVector3 a, PlanarVector3 b)
    {
        var lengths = a.Length * b.Length;
        if (!double.IsFinite(lengths) || lengths <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(a), "Нулевой или неконечный вектор направления.");

        var cosine = Math.Abs(a.Dot(b)) / lengths;
        return Math.Acos(Math.Min(1.0, cosine)) * 180.0 / Math.PI;
    }

    /// <summary>Эффективный допуск угла; null означает применение линейного критерия.</summary>
    public static double? AngleToleranceDeg(double segmentLengthM, double lineDistanceTolM, double angularDeg)
    {
        if (!double.IsFinite(segmentLengthM) || segmentLengthM <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(segmentLengthM));
        if (!double.IsFinite(lineDistanceTolM) || lineDistanceTolM < 0.0)
            throw new ArgumentOutOfRangeException(nameof(lineDistanceTolM));
        if (!double.IsFinite(angularDeg) || angularDeg < 0.0)
            throw new ArgumentOutOfRangeException(nameof(angularDeg));
        if (segmentLengthM <= lineDistanceTolM) return null;

        var linearEquivalentDeg = Math.Asin(Math.Min(1.0, lineDistanceTolM / segmentLengthM)) * 180.0 / Math.PI;
        return Math.Max(angularDeg, linearEquivalentDeg);
    }
}
