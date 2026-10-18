using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>
/// Стойка с упругими закреплениями (<see cref="RcRestrainedColumn"/>, Масленников А.М., 2017, с. 218): случаи Эйлера,
/// заделка по концам со смещением, шарнирная стойка на поперечной пружине, номограммы ASCE 1997 (ур. D.3 со смещением
/// и без смещения, ρ = 6/G и 2/G).
/// </summary>
[HarnessChecks]
public class RcRestrainedColumnTests
{
    private const double Inf = double.PositiveInfinity;

    [Fact]
    public static void RunAll()
    {
        RunEuler();
        RunAlignmentCharts();
    }

    private static double Mu(double rho1, double rho2, double rho3) =>
        RcRestrainedColumn.Solve(new PlaneRestraint(rho1, rho2, rho3), 1.0, 1.0).Mu;

    private static void RunEuler()
    {
        TestHarness.Section("Стойка с упругими закреплениями: случаи Эйлера");
        TestHarness.CheckRel("шарнир — шарнир", Mu(0, 0, Inf), 1.0, 1e-9);
        TestHarness.CheckRel("консоль", Mu(Inf, 0, 0), 2.0, 1e-9);
        TestHarness.CheckRel("заделка — заделка", Mu(Inf, Inf, Inf), 0.5, 1e-9);
        TestHarness.CheckRel("заделка — шарнир", Mu(Inf, 0, Inf), Math.PI / 4.493409457909064, 1e-9);
        TestHarness.CheckRel("шарнир — заделка", Mu(0, Inf, Inf), Math.PI / 4.493409457909064, 1e-9);
        TestHarness.CheckRel("заделки со смещением", Mu(Inf, Inf, 0), 1.0, 1e-9);
        // N_cr = r₃·l: u² = ρ₃ → μ = π/√ρ₃; при ρ₃ > π² — изгиб между шарнирами, μ = 1.
        TestHarness.CheckRel("шарниры, слабая пружина", Mu(0, 0, 0.3 * Math.PI * Math.PI), 1 / Math.Sqrt(0.3), 1e-9);
        TestHarness.CheckRel("шарниры, жёсткая пружина", Mu(0, 0, 3 * Math.PI * Math.PI), 1.0, 1e-9);
        TestHarness.Check("ближайший корень: заделка — заделка, около 0,35",
            Math.Abs(RcRestrainedColumn.Solve(new PlaneRestraint(Inf, Inf, Inf), 1, 1, Math.PI / 0.35).Mu - 0.3494) < 1e-3, "");
    }

    /// <summary>
    /// Номограммы ASCE: со смещением (G_A·G_B·v² − 36)/(6(G_A + G_B)) = v/tg v, без смещения
    /// G_A·G_B/4·v² + (G_A + G_B)/2·(1 − v/tg v) + 2·tg(v/2)/v = 1, v = π/K; ригели — 6EI/L и 2EI/L, ρ = 6/G и 2/G.
    /// </summary>
    private static void RunAlignmentCharts()
    {
        TestHarness.Section("Стойка с упругими закреплениями: номограммы ASCE (D.3)");
        foreach (var (ga, gb) in new[] { (1.0, 1.0), (1.0, 2.0), (0.5, 3.0) })
        {
            double sway = Bisect(v => (ga * gb * v * v - 36) / (6 * (ga + gb)) - v / Math.Tan(v), 1e-3, Math.PI - 1e-9);
            TestHarness.CheckRel($"со смещением, G = {ga}; {gb}", Mu(6 / ga, 6 / gb, 0), Math.PI / sway, 1e-7);
            double braced = Bisect(v => ga * gb / 4 * v * v + (ga + gb) / 2 * (1 - v / Math.Tan(v)) + 2 * Math.Tan(v / 2) / v - 1,
                Math.PI + 1e-9, 2 * Math.PI - 1e-9);
            TestHarness.CheckRel($"без смещения, G = {ga}; {gb}", Mu(2 / ga, 2 / gb, Inf), Math.PI / braced, 1e-7);
        }
        TestHarness.CheckRel("со смещением, G = 1; 1 — K = 1,32", Mu(6, 6, 0), 1.3164, 1e-3);
        TestHarness.CheckRel("без смещения, G = 1; 1 — K = 0,774", Mu(2, 2, Inf), 0.7743, 1e-3);
    }

    private static double Bisect(Func<double, double> f, double a, double b)
    {
        double fa = f(a);
        for (int i = 0; i < 200; i++)
        {
            double m = 0.5 * (a + b), fm = f(m);
            if (fa * fm <= 0) b = m;
            else (a, fa) = (m, fm);
        }
        return 0.5 * (a + b);
    }
}
