namespace CScore.ShellSimpl;

/// <summary>Предельные деформации п. 8.1.30 для полосы: εb0, εb2 (модули) и εs,ult.</summary>
public sealed record StripNdmLimits(double EpsB0, double EpsB2, double EpsSUlt)
{
    /// <summary>Как в <see cref="ShellLayeredCheck"/>: εb0/εb2 из характеристик бетона вида расчёта
    /// (резерв 0,002/0,0035), εs,ult = 0,025 при физическом пределе текучести и 0,015 — при условном.</summary>
    public static StripNdmLimits From(MaterialChars? concrete, MatType rebarType)
    {
        double e0 = concrete != null && Math.Abs(concrete.Ec0) > 0 ? Math.Abs(concrete.Ec0) : 0.002;
        double e2 = concrete != null && Math.Abs(concrete.Ec2) > 0 ? Math.Abs(concrete.Ec2) : 0.0035;
        return new StripNdmLimits(e0, e2, rebarType == MatType.ReSteelF ? 0.025 : 0.015);
    }
}
