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

            if (edge.NodeMid is int mid)
            {
                ApplyQuadraticRobinEdge(a, mid, b, L, edge, T_inf, nodalT, K, F);
                continue;
            }

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

    /// <summary>Робин на квадратичном 1D-ребре (3 узла): 2-точечная квадратура Гаусса.</summary>
    static void ApplyQuadraticRobinEdge(
        int a, int mid, int b, double L,
        HeatBoundaryEdge edge, double T_inf,
        double[] nodalT, CooMatrix K, double[] F)
    {
        double T_surf = (nodalT[a] + 4.0 * nodalT[mid] + nodalT[b]) / 6.0;
        double alpha_lin = RobinHeatFlux.ComputeAlphaLin(
            T_surf, T_inf, edge.AlphaConv, edge.Emissivity);

        double halfL = 0.5 * L;
        ReadOnlySpan<double> gaussXi = [-0.5773502691896257, 0.5773502691896257];
        Span<double> n = stackalloc double[3];
        int[] nodes = [a, mid, b];
        var ke = new double[3, 3];
        var fe = new double[3];

        foreach (double xi in gaussXi)
        {
            QuadraticEdgeShape(xi, n);
            double factor = alpha_lin * halfL;
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                    ke[i, j] += factor * n[i] * n[j];
                fe[i] += factor * T_inf * n[i];
            }
        }

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                K.Add(nodes[i], nodes[j], ke[i, j]);
            }
            F[nodes[i]] += fe[i];
        }
    }

    static void QuadraticEdgeShape(double xi, Span<double> n)
    {
        n[0] = 0.5 * xi * (xi - 1.0);
        n[1] = 1.0 - xi * xi;
        n[2] = 0.5 * xi * (xi + 1.0);
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
