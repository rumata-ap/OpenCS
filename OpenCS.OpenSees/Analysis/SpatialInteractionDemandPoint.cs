namespace OpenCS.OpenSees.Analysis;

/// <summary>Расчётная точка ForceSet для проверки относительно поверхности N–Mx–My.</summary>
public sealed class SpatialInteractionDemandPoint
{
    /// <summary>Номер строки исходного набора усилий.</summary>
    public int Num { get; init; }

    /// <summary>Метка строки исходного набора усилий.</summary>
    public string Label { get; init; } = "";

    /// <summary>Продольная сила в Н.</summary>
    public double AxialForceN { get; init; }

    /// <summary>Изгибающий момент Mx в Н·м.</summary>
    public double MomentMxNm { get; init; }

    /// <summary>Изгибающий момент My в Н·м.</summary>
    public double MomentMyNm { get; init; }
}
