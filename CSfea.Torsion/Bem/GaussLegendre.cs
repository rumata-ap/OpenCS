namespace CSfea.Torsion;

/// <summary>Узлы и веса квадратуры Гаусса–Лежандра 4-го порядка на [-1, 1].</summary>
public static class GaussLegendre
{
    /// <summary>Узлы (4 точки).</summary>
    public static readonly double[] Xi =
        { -0.8611363115940526, -0.3399810435848563, 0.3399810435848563, 0.8611363115940526 };

    /// <summary>Веса (4 точки).</summary>
    public static readonly double[] W =
        { 0.3478548451374538, 0.6521451548625461, 0.6521451548625461, 0.3478548451374538 };
}
