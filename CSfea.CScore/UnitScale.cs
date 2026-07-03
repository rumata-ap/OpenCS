namespace CSfea.CScoreBridge;

/// <summary>
/// Масштабирование между единицами CScore (кН, кН·м) и CSfea (Н, Н·м).
/// </summary>
public static class UnitScale
{
    /// <summary>кН → Н.</summary>
    public const double Force = 1000.0;

    /// <summary>кН·м → Н·м.</summary>
    public const double Moment = 1000.0;

    /// <summary>кН/м → Н/м (мембранные усилия оболочки).</summary>
    public const double ShellForce = 1000.0;

    /// <summary>кН·м/м → Н·м/м (изгибающие моменты оболочки).</summary>
    public const double ShellMoment = 1000.0;

    public static double ToCsfeaForce(double kN) => kN * Force;
    public static double ToCsfeaMoment(double kNm) => kNm * Moment;
    public static double ToCsfeaShellForce(double kNPerM) => kNPerM * ShellForce;
    public static double ToCsfeaShellMoment(double kNmPerM) => kNmPerM * ShellMoment;
}
