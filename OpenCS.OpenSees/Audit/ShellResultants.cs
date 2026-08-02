namespace OpenCS.OpenSees.Audit;

/// <summary>Шестикомпонентный глобальный resultant: силы в Н и моменты в Н·м.</summary>
public sealed record ShellResultant(double Fx, double Fy, double Fz, double Mx, double My, double Mz)
{
    /// <summary>Нулевой resultant.</summary>
    public static ShellResultant Zero => new(0, 0, 0, 0, 0, 0);

    /// <summary>Складывает два resultanta.</summary>
    public static ShellResultant operator +(ShellResultant left, ShellResultant right) =>
        new(left.Fx + right.Fx, left.Fy + right.Fy, left.Fz + right.Fz,
            left.Mx + right.Mx, left.My + right.My, left.Mz + right.Mz);

    /// <summary>Умножает resultant на скаляр.</summary>
    public static ShellResultant operator *(ShellResultant value, double scale) =>
        new(value.Fx * scale, value.Fy * scale, value.Fz * scale,
            value.Mx * scale, value.My * scale, value.Mz * scale);

    /// <summary>Максимальная по модулю компонента.</summary>
    public double MaxAbsoluteComponent =>
        new[] { Fx, Fy, Fz, Mx, My, Mz }.Max(Math.Abs);
}

/// <summary>Математика глобальных resultants: момент от узловой силы в точке r
/// вычисляется как r × F + M.</summary>
public static class ShellResultantMath
{
    /// <summary>Вычисляет resultant силы и заданного момента относительно глобального начала.</summary>
    public static ShellResultant NodalForce(double rx, double ry, double rz, ShellResultant force) => new(
        force.Fx, force.Fy, force.Fz,
        ry * force.Fz - rz * force.Fy + force.Mx,
        rz * force.Fx - rx * force.Fz + force.My,
        rx * force.Fy - ry * force.Fx + force.Mz);
}
