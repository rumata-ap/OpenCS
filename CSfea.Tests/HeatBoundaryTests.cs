using CSfea.Sparse;
using CSfea.Thermal;
using CSfea.Thermal.Bc;

namespace CSfea.Tests;

/// <summary>Тесты физики граничного потока Робина (конвекция + излучение).</summary>
public static class HeatBoundaryTests
{
    public static void RunAll()
    {
        TestHarness.Section("HeatBoundary: поток и линеаризация Робина");
        PureConvectionAtLowTemp();
        ZeroFluxWhenEqual();
        HRadiationLinearizedRecoversFlux();

        TestHarness.Section("HeatBoundary: матрица ребра Робина");
        EdgeMatrixTwoNodeContribution();
    }

    private static void PureConvectionAtLowTemp()
    {
        double q = RobinHeatFlux.ComputeBoundaryFlux(
            T_surface: 50.0, T_ambient: 100.0, alpha_conv: 25.0, emissivity: 0.0);
        TestHarness.CheckRel("HeatBoundary_PureConvection", q, 25.0 * (100.0 - 50.0), 1e-6);
    }

    private static void ZeroFluxWhenEqual()
    {
        double q = RobinHeatFlux.ComputeBoundaryFlux(
            T_surface: 300.0, T_ambient: 300.0, alpha_conv: 25.0, emissivity: 0.7);
        TestHarness.Check("HeatBoundary_ZeroFluxWhenEqual", Math.Abs(q) < 1e-6, $"q={q:e4}");
    }

    private static void HRadiationLinearizedRecoversFlux()
    {
        const double Ts = 400.0;
        const double Tinf = 800.0;
        const double eps = 0.7;
        double h = RobinHeatFlux.ComputeHRadiationLinearized(Ts, Tinf, eps);
        double q_lin = h * (Tinf - Ts);
        double Ts_K = Ts + 273.15;
        double Tinf_K = Tinf + 273.15;
        double q_exact = eps * RobinHeatFlux.SigmaStefanBoltzmann
            * (Tinf_K * Tinf_K * Tinf_K * Tinf_K - Ts_K * Ts_K * Ts_K * Ts_K);
        TestHarness.CheckRel("HeatBoundary_HRadLinearization", q_lin, q_exact, 1e-6);
    }

    private static void EdgeMatrixTwoNodeContribution()
    {
        const int n = 2;
        var mesh = new HeatMesh([0.0, 1.0], [0.0, 0.0], [[0, 1, 0]]);
        var edge = new HeatBoundaryEdge(
            NodeA: 0,
            NodeB: 1,
            LengthM: 1.0,
            BcType: HeatBoundaryBcType.Ambient,
            AlphaConv: 10.0,
            Emissivity: 0.0,
            TAmbientCelsius: 20.0);

        var K = new CooMatrix(n, n);
        var F = new double[n];
        var nodalT = new double[n] { 50.0, 50.0 };

        RobinBoundaryModel.ApplyRobin(mesh, [edge], 0.0, nodalT, K, F, null);

        const double alpha_lin = 10.0;
        double diag = alpha_lin / 3.0;
        double off = alpha_lin / 6.0;
        var dense = K.ToDense();

        bool kOk = Math.Abs(dense[0, 0] - diag) < 1e-12
            && Math.Abs(dense[1, 1] - diag) < 1e-12
            && Math.Abs(dense[0, 1] - off) < 1e-12
            && Math.Abs(dense[1, 0] - off) < 1e-12;

        double f_expected = alpha_lin * 20.0 * 0.5;
        bool fOk = Math.Abs(F[0] - f_expected) < 1e-12 && Math.Abs(F[1] - f_expected) < 1e-12;

        TestHarness.Check("HeatBoundary_EdgeMatrixK", kOk,
            $"diag={dense[0, 0]:g6}, off={dense[0, 1]:g6}, expected diag={diag:g6}, off={off:g6}");
        TestHarness.Check("HeatBoundary_EdgeMatrixF", fOk,
            $"F0={F[0]:g6}, F1={F[1]:g6}, expected={f_expected:g6}");
    }
}
