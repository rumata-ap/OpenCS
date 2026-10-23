namespace CScore.LimitSurface;

/// <summary>Точка (вектор) в пространстве усилий: N, кН; Mx, My, кН·м.</summary>
public readonly record struct ForcePoint(double N, double Mx, double My)
{
    public static ForcePoint operator +(ForcePoint a, ForcePoint b) => new(a.N + b.N, a.Mx + b.Mx, a.My + b.My);
    public static ForcePoint operator -(ForcePoint a, ForcePoint b) => new(a.N - b.N, a.Mx - b.Mx, a.My - b.My);
    public static ForcePoint operator *(double k, ForcePoint a) => new(k * a.N, k * a.Mx, k * a.My);

    /// <summary>Скалярное произведение.</summary>
    public double Dot(ForcePoint b) => N * b.N + Mx * b.Mx + My * b.My;

    /// <summary>Векторное произведение.</summary>
    public ForcePoint Cross(ForcePoint b) => new(Mx * b.My - My * b.Mx, My * b.N - N * b.My, N * b.Mx - Mx * b.N);

    /// <summary>Длина вектора.</summary>
    public double Length => Math.Sqrt(Dot(this));

    /// <summary>Покомпонентное умножение (нормировка).</summary>
    public ForcePoint Scale(ForcePoint s) => new(N * s.N, Mx * s.Mx, My * s.My);
}
