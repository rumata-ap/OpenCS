namespace CSfea.Thermal.Bc;

/// <summary>
/// Ребро внешнего контура сетки с параметрами граничного условия Робина.
/// </summary>
public sealed record HeatBoundaryEdge(
    int NodeA,
    int NodeB,
    double LengthM,
    HeatBoundaryBcType BcType,
    double AlphaConv,
    double Emissivity,
    double TAmbientCelsius,
    Func<double, double>? FireCurveAtTime = null);
