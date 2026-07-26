namespace OpenCS.OpenSees.Structural;

/// <summary>Ортонормированный локальный frame shell-элемента.</summary>
public sealed record ShellFrame(
    ShellVector3 Ex,
    ShellVector3 Ey,
    ShellVector3 Normal)
{
    private const double Tolerance = 1e-9;

    /// <summary>Глобальный ортонормированный frame XY/+Z.</summary>
    public static ShellFrame Identity { get; } = new(
        new ShellVector3(1, 0, 0),
        new ShellVector3(0, 1, 0),
        new ShellVector3(0, 0, 1));

    /// <summary>Проверяет ортонормированность и right-handed ориентацию frame.</summary>
    public void Validate()
    {
        if (!Ex.IsFinite || !Ey.IsFinite || !Normal.IsFinite)
            throw new InvalidOperationException("Shell frame содержит неконечное направление.");

        if (Math.Abs(Ex.Length - 1) > Tolerance ||
            Math.Abs(Ey.Length - 1) > Tolerance ||
            Math.Abs(Normal.Length - 1) > Tolerance)
            throw new InvalidOperationException("Shell frame должен содержать единичные направления.");

        if (Math.Abs(Ex.Dot(Ey)) > Tolerance ||
            Math.Abs(Ex.Dot(Normal)) > Tolerance ||
            Math.Abs(Ey.Dot(Normal)) > Tolerance)
            throw new InvalidOperationException("Shell frame должен быть ортогональным.");

        if (Ex.Cross(Ey).Dot(Normal) < 1 - Tolerance)
            throw new InvalidOperationException("Shell frame должен иметь right-handed ориентацию.");
    }
}
