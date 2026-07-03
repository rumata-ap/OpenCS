namespace CSfea.Torsion;

/// <summary>
/// Входная геометрия задачи кручения: упорядоченные замкнутые контуры.
/// Замыкание неявное (последняя точка соединяется с первой).
/// </summary>
public sealed class TorsionBoundary
{
    /// <summary>Координаты X внешнего контура (ориентация CCW).</summary>
    public double[] OuterX { get; }

    /// <summary>Координаты Y внешнего контура (CCW).</summary>
    public double[] OuterY { get; }

    /// <summary>Отверстия (каждое — CW), null если отверстий нет.</summary>
    public IReadOnlyList<(double[] X, double[] Y)>? Holes { get; }

    public TorsionBoundary(double[] outerX, double[] outerY,
        IReadOnlyList<(double[] X, double[] Y)>? holes = null)
    {
        OuterX = outerX;
        OuterY = outerY;
        Holes = holes;
    }
}
