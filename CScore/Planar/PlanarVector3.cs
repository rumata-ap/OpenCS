namespace CScore.Planar;

/// <summary>Лёгкий 3D-вектор для геометрии PlanarRegion/Frame3D. Не переиспользует
/// OpenCS.OpenSees.Structural.ShellVector3 — CScore ниже по зависимостям.</summary>
public readonly record struct PlanarVector3(double X, double Y, double Z)
{
    public static readonly PlanarVector3 Zero = new(0, 0, 0);

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

    public double Dot(PlanarVector3 other) => X * other.X + Y * other.Y + Z * other.Z;

    public PlanarVector3 Cross(PlanarVector3 other) => new(
        Y * other.Z - Z * other.Y,
        Z * other.X - X * other.Z,
        X * other.Y - Y * other.X);

    public PlanarVector3 Normalize()
    {
        var len = Length;
        if (len < 1e-12)
            throw new InvalidOperationException("Нельзя нормализовать вектор нулевой длины.");
        return new PlanarVector3(X / len, Y / len, Z / len);
    }

    public static PlanarVector3 operator +(PlanarVector3 a, PlanarVector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static PlanarVector3 operator -(PlanarVector3 a, PlanarVector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static PlanarVector3 operator *(PlanarVector3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
}
