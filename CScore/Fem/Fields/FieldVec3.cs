namespace CScore.Fem.Fields;

/// <summary>Точка или вектор в общих осях схемы, м.</summary>
public readonly record struct FieldVec3(double X, double Y, double Z)
{
    public static FieldVec3 operator +(FieldVec3 a, FieldVec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static FieldVec3 operator -(FieldVec3 a, FieldVec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static FieldVec3 operator *(FieldVec3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public double Dot(FieldVec3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public FieldVec3 Cross(FieldVec3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double Length => Math.Sqrt(Dot(this));
    public FieldVec3 Normalized() { double l = Length; return l > 0 ? this * (1 / l) : this; }
}

/// <summary>Точка таблицы: координаты и значения по выбранным столбцам.</summary>
public sealed record FieldPoint(FieldVec3 P, double[] Values);
