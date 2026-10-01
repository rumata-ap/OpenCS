using CScore.Planar;

namespace CScore.Import;

/// <summary>
/// Системы осей выдачи усилий пластин SCAD (ApiSystemCoord) → угол согласования местных осей
/// <see cref="Fem.FemElement.LocalAxisAngleDeg"/>: поворот оси X1 от направления «узел 1 → узел 2»
/// вокруг нормали КЭ (p2−p1)×(p3−p1).
/// </summary>
public static class ScadOutputAxes
{
    /// <summary>X1 — по вектору в глобальной системе.</summary>
    public const byte X1Vector = 16;
    /// <summary>X1 — на точку, от узла 1 КЭ.</summary>
    public const byte X1PointFromNode1 = 17;
    /// <summary>X1 — на точку, от центра КЭ.</summary>
    public const byte X1PointFromCenter = 18;
    /// <summary>Y1 — по вектору в глобальной системе.</summary>
    public const byte Y1Vector = 19;
    /// <summary>Y1 — на точку, от узла 1 КЭ.</summary>
    public const byte Y1PointFromNode1 = 20;
    /// <summary>Y1 — на точку, от центра КЭ.</summary>
    public const byte Y1PointFromCenter = 21;

    /// <summary>Относительный порог вырождения проекции направления на плоскость КЭ.</summary>
    const double DegenerateTolerance = 1e-9;

    /// <summary>
    /// Угол оси выдачи X1 КЭ, град.
    /// </summary>
    /// <param name="type">Тип системы (16–21).</param>
    /// <param name="data">Данные системы: вектор или точка (3 числа, глобальные координаты).</param>
    /// <param name="points">Узлы КЭ (3 или 4) в порядке SCAD или контурном — нормаль от этого не зависит.</param>
    /// <returns>null — тип не пластинчатый, данных мало, КЭ вырожден или направление ⟂ плоскости КЭ.</returns>
    public static double? AngleDeg(byte type, ReadOnlySpan<double> data, IReadOnlyList<PlanarVector3> points)
    {
        if (type is < X1Vector or > Y1PointFromCenter || data.Length < 3 || points.Count < 3) return null;

        var p1 = points[0];
        var e1 = points[1] - p1;
        var normal = e1.Cross(points[2] - p1);
        if (e1.Length < 1e-12 || normal.Length < 1e-12 * e1.LengthSquared) return null;
        e1 = e1.Normalize();
        normal = normal.Normalize();

        var target = new PlanarVector3(data[0], data[1], data[2]);
        var direction = type switch
        {
            X1Vector or Y1Vector                   => target,
            X1PointFromNode1 or Y1PointFromNode1   => target - p1,
            _                                      => target - Center(points),
        };
        if (!(direction.Length > 0)) return null;

        // Проекция на плоскость КЭ.
        var inPlane = direction - normal * direction.Dot(normal);
        if (inPlane.Length < DegenerateTolerance * direction.Length) return null;

        // Для 19–21 задана Y1: X1 дополняет её до правой тройки с нормалью.
        var x1 = type >= Y1Vector ? inPlane.Cross(normal) : inPlane;
        return Math.Atan2(e1.Cross(x1).Dot(normal), e1.Dot(x1)) * 180.0 / Math.PI;
    }

    static PlanarVector3 Center(IReadOnlyList<PlanarVector3> points)
    {
        var sum = PlanarVector3.Zero;
        foreach (var p in points) sum += p;
        return sum * (1.0 / points.Count);
    }
}
