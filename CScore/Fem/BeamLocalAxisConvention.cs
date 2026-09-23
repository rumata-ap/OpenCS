using CScore.Planar;

namespace CScore.Fem;

/// <summary>
/// Единая конвенция локальных осей стержня (OpenSees geomTransf, 3D-глиф сечения, субмодели).
/// X направлена от узла I к узлу J; при нулевом угле Y — проекция глобальной Z,
/// а для вертикального стержня — глобальная X. Положительный угол вращает Y и Z
/// вокруг X по правилу правой руки.
/// Повторяет <c>OpenCS.OpenSees.Structural.FemLocalAxis</c> побитно (сборка OpenSees не зависит от CScore);
/// совпадение сторожит <c>BeamLocalAxisConventionEquivalenceTests</c> в OpenCS.OpenSees.Tests.
/// </summary>
public static class BeamLocalAxisConvention
{
    /// <summary>
    /// Порог вертикальности по углу. Для почти вертикального стержня проекция глобальной Z
    /// становится плохо определённой, поэтому локальная Y задаётся глобальной X.
    /// </summary>
    const double VerticalCosineThreshold = 0.99996;
    const double VectorEpsilon = 1e-12;

    /// <summary>Локальный ортонормированный базис X/Y/Z стержня I→J.</summary>
    public static (PlanarVector3 X, PlanarVector3 Y, PlanarVector3 Z) Frame(
        PlanarVector3 i, PlanarVector3 j, double rotationDeg = 0)
    {
        var d = j - i;
        double len = d.Length;
        if (!double.IsFinite(len) || len < VectorEpsilon)
            throw new InvalidOperationException("Нулевая длина стержня — невозможно построить локальные оси.");
        if (!double.IsFinite(rotationDeg))
            throw new InvalidOperationException("Угол поворота сечения должен быть конечным числом.");

        var x = new PlanarVector3(d.X / len, d.Y / len, d.Z / len);
        var reference = Math.Abs(x.Z) > VerticalCosineThreshold
            ? new PlanarVector3(1, 0, 0)
            : new PlanarVector3(0, 0, 1);
        var y = Normalize(reference - x * reference.Dot(x));
        var z = x.Cross(y);

        if (rotationDeg == 0) return (x, y, z);

        double angleRad = rotationDeg * Math.PI / 180.0;
        return (x, RotateAroundAxis(y, x, angleRad), RotateAroundAxis(z, x, angleRad));
    }

    static PlanarVector3 Normalize(PlanarVector3 v)
    {
        double length = v.Length;
        if (!double.IsFinite(length) || length < VectorEpsilon)
            throw new InvalidOperationException("Невозможно построить поперечную локальную ось стержня.");
        return new PlanarVector3(v.X / length, v.Y / length, v.Z / length);
    }

    /// <summary>Поворот вектора вокруг единичной оси по формуле Родрига.</summary>
    static PlanarVector3 RotateAroundAxis(PlanarVector3 vector, PlanarVector3 axis, double angleRad)
    {
        double cos = Math.Cos(angleRad), sin = Math.Sin(angleRad);
        double axisDotVector = axis.Dot(vector);
        var cross = axis.Cross(vector);
        return new PlanarVector3(
            vector.X * cos + cross.X * sin + axis.X * axisDotVector * (1 - cos),
            vector.Y * cos + cross.Y * sin + axis.Y * axisDotVector * (1 - cos),
            vector.Z * cos + cross.Z * sin + axis.Z * axisDotVector * (1 - cos));
    }
}
