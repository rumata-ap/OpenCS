namespace OpenCS.OpenSees.Structural;

/// <summary>Единая конвенция локальных осей стержня для OpenSees и 3D-глифа сечения.</summary>
public static class FemLocalAxis
{
    /// <summary>
    /// Порог вертикальности по углу. Для почти вертикального стержня проекция глобальной Z
    /// становится плохо определённой, поэтому локальная Y задаётся глобальной X.
    /// </summary>
    const double VerticalCosineThreshold = 0.99996;
    const double VectorEpsilon = 1e-12;

    /// <summary>
    /// Возвращает локальный ортонормированный базис X/Y/Z.
    /// X направлена от узла I к узлу J; при нулевом угле Y — проекция глобальной Z,
    /// а для вертикального стержня — глобальная X. Положительный угол вращает Y и Z
    /// вокруг X по правилу правой руки.
    /// </summary>
    public static (
        (double X, double Y, double Z) X,
        (double X, double Y, double Z) Y,
        (double X, double Y, double Z) Z)
        LocalFrame(FemLinearNode i, FemLinearNode j, double rotationDeg = 0)
    {
        double dx = j.X - i.X, dy = j.Y - i.Y, dz = j.Z - i.Z;
        double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (!double.IsFinite(len) || len < VectorEpsilon)
            throw new InvalidOperationException("Нулевая длина стержня — невозможно построить локальные оси.");
        if (!double.IsFinite(rotationDeg))
            throw new InvalidOperationException("Угол поворота сечения должен быть конечным числом.");

        var x = (X: dx / len, Y: dy / len, Z: dz / len);
        var reference = Math.Abs(x.Z) > VerticalCosineThreshold
            ? (X: 1d, Y: 0d, Z: 0d)
            : (X: 0d, Y: 0d, Z: 1d);
        var y = Normalize(Subtract(reference, Multiply(x, Dot(reference, x))));
        var z = Cross(x, y);

        if (rotationDeg == 0) return (x, y, z);

        double angleRad = rotationDeg * Math.PI / 180.0;
        return (x, RotateAroundAxis(y, x, angleRad), RotateAroundAxis(z, x, angleRad));
    }

    /// <summary>Возвращает вектор vecxz OpenSees, совпадающий с локальной осью Z.</summary>
    public static (double X, double Y, double Z) Vecxz(FemLinearNode i, FemLinearNode j, double rotationDeg = 0) =>
        LocalFrame(i, j, rotationDeg).Z;

    static (double X, double Y, double Z) Normalize((double X, double Y, double Z) v)
    {
        double length = Math.Sqrt(Dot(v, v));
        if (!double.IsFinite(length) || length < VectorEpsilon)
            throw new InvalidOperationException("Невозможно построить поперечную локальную ось стержня.");
        return (v.X / length, v.Y / length, v.Z / length);
    }

    static (double X, double Y, double Z) Multiply(
        (double X, double Y, double Z) v, double factor) =>
        (v.X * factor, v.Y * factor, v.Z * factor);

    static (double X, double Y, double Z) Subtract(
        (double X, double Y, double Z) left, (double X, double Y, double Z) right) =>
        (left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    static double Dot((double X, double Y, double Z) left, (double X, double Y, double Z) right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    static (double X, double Y, double Z) Cross(
        (double X, double Y, double Z) left, (double X, double Y, double Z) right) =>
        (left.Y * right.Z - left.Z * right.Y,
         left.Z * right.X - left.X * right.Z,
         left.X * right.Y - left.Y * right.X);

    /// <summary>Поворот вектора вокруг единичной оси по формуле Родрига.</summary>
    static (double X, double Y, double Z) RotateAroundAxis(
        (double X, double Y, double Z) vector,
        (double X, double Y, double Z) axis,
        double angleRad)
    {
        double cos = Math.Cos(angleRad), sin = Math.Sin(angleRad);
        double axisDotVector = Dot(axis, vector);
        var cross = Cross(axis, vector);
        return (
            vector.X * cos + cross.X * sin + axis.X * axisDotVector * (1 - cos),
            vector.Y * cos + cross.Y * sin + axis.Y * axisDotVector * (1 - cos),
            vector.Z * cos + cross.Z * sin + axis.Z * axisDotVector * (1 - cos));
    }
}
