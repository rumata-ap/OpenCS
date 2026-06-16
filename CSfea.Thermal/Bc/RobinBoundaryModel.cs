using CSfea.Sparse;

namespace CSfea.Thermal.Bc;

/// <summary>
/// Граничные условия Робина (конвекция + линеаризованное излучение) на рёбрах контура.
/// </summary>
public sealed class RobinBoundaryModel : IHeatBoundaryModel
{
    private readonly HeatMesh _mesh;
    private readonly HeatBoundaryEdge[] _edges;
    private readonly Func<double, double>? _fireCurve;

    /// <summary>Создать модель Робина для заданных рёбер контура.</summary>
    public RobinBoundaryModel(
        HeatMesh mesh,
        HeatBoundaryEdge[] edges,
        Func<double, double>? fireCurve = null)
    {
        _mesh = mesh;
        _edges = edges;
        _fireCurve = fireCurve;
    }

    /// <inheritdoc/>
    public void Contribute(double time_s, double[] nodalT, IList<CooMatrix> kTargets, double[] f)
    {
        if (kTargets.Count == 0)
            return;
        ApplyRobin(_mesh, _edges, time_s, nodalT, kTargets[0], f, _fireCurve);
    }

    /// <summary>
    /// Добавить вклад Робина к K и F (аналог <c>_apply_robin_bc</c> в Python).
    /// K_edge: (α_lin · L / 6) · [[2, 1], [1, 2]];
    /// F_edge: α_lin · T_∞ · L / 2 на каждый узел ребра.
    /// </summary>
    public static void ApplyRobin(
        HeatMesh mesh,
        IList<HeatBoundaryEdge> edges,
        double time_s,
        double[] nodalT,
        CooMatrix K,
        double[] F,
        Func<double, double>? fireCurve)
    {
        foreach (var edge in edges)
        {
            if (edge.BcType == HeatBoundaryBcType.Adiabatic)
                continue;

            double L = edge.LengthM;
            int a = edge.NodeA;
            int b = edge.NodeB;

            double T_inf = ResolveAmbientTemperature(edge, time_s, fireCurve);
            double T_surf = 0.5 * (nodalT[a] + nodalT[b]);
            double alpha_lin = RobinHeatFlux.ComputeAlphaLin(
                T_surf, T_inf, edge.AlphaConv, edge.Emissivity);

            double diag_val = alpha_lin * L / 3.0;
            double off_val = alpha_lin * L / 6.0;
            K.Add(a, a, diag_val);
            K.Add(a, b, off_val);
            K.Add(b, a, off_val);
            K.Add(b, b, diag_val);

            double f_val = alpha_lin * T_inf * L / 2.0;
            F[a] += f_val;
            F[b] += f_val;
        }
    }

    private static double ResolveAmbientTemperature(
        HeatBoundaryEdge edge,
        double time_s,
        Func<double, double>? fireCurve)
    {
        if (edge.BcType == HeatBoundaryBcType.Fire)
        {
            var curve = edge.FireCurveAtTime ?? fireCurve;
            if (curve == null)
                throw new InvalidOperationException(
                    "Для ребра с типом Fire необходима кривая пожара (FireCurveAtTime или fireCurve).");
            return curve(time_s);
        }

        return edge.TAmbientCelsius;
    }
}
