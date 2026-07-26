namespace OpenCS.OpenSees.Structural;

/// <summary>Вектор координат или направления в глобальной 3D-системе.</summary>
public readonly record struct ShellVector3(double X, double Y, double Z)
{
    /// <summary>Нулевой вектор.</summary>
    public static ShellVector3 Zero => new(0, 0, 0);

    /// <summary>Квадрат длины вектора.</summary>
    public double LengthSquared => X * X + Y * Y + Z * Z;

    /// <summary>Длина вектора.</summary>
    public double Length => Math.Sqrt(LengthSquared);

    /// <summary>Проверяет конечность всех компонент.</summary>
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

    /// <summary>Скалярное произведение.</summary>
    public double Dot(ShellVector3 other) => X * other.X + Y * other.Y + Z * other.Z;

    /// <summary>Векторное произведение.</summary>
    public ShellVector3 Cross(ShellVector3 other) => new(
        Y * other.Z - Z * other.Y,
        Z * other.X - X * other.Z,
        X * other.Y - Y * other.X);

    /// <summary>Возвращает единичный вектор.</summary>
    public ShellVector3 Normalize()
    {
        if (!IsFinite || Length <= 1e-15)
            throw new InvalidOperationException("Нельзя нормализовать нулевой или неконечный shell-вектор.");

        return new ShellVector3(X / Length, Y / Length, Z / Length);
    }

    /// <summary>Складывает два вектора.</summary>
    public static ShellVector3 operator +(ShellVector3 left, ShellVector3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    /// <summary>Вычитает два вектора.</summary>
    public static ShellVector3 operator -(ShellVector3 left, ShellVector3 right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    /// <summary>Умножает вектор на число.</summary>
    public static ShellVector3 operator *(ShellVector3 value, double scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale);
}
