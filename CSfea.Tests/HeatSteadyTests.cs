using CSfea.Thermal;
using CSfea.Thermal.Materials;
using CSfea.Thermal.Solvers;

namespace CSfea.Tests;

/// <summary>Тесты стационарного решателя теплопроводности HeatSteadySolver.</summary>
public static class HeatSteadyTests
{
    public static void RunAll()
    {
        TestHarness.Section("HeatSteady: 1D-полоска, T_left=100, T_right=0");
        HeatSteady_1DStrip_MiddleNodeNear50();
    }

    /// <summary>
    /// Полоска из 5 узлов вдоль x и 4 тонких треугольника (веер от вершины над серединой).
    /// Узел 0: T=100, узел 4: T=0; узел 2 (x=0.5) ≈ 50.
    /// </summary>
    private static void HeatSteady_1DStrip_MiddleNodeNear50()
    {
        var mesh = Create1DStripMesh();
        var mat = new ConstantHeatMaterial(lambda: 1.0, rhocp: 1.0);
        var K = mesh.AssembleConductivity(mat);
        var F = new double[mesh.NNodes];

        int[] fixedDofs = [0, 4];
        double[] uFixed = [100.0, 0.0];

        var T = HeatSteadySolver.Solve(mesh, K, F, fixedDofs, uFixed);

        double Tmid = T[2];
        double expected = 50.0;
        double relErr = Math.Abs(Tmid - expected) / expected;
        bool ok = relErr < 0.10;

        TestHarness.Check("HeatSteady_1DStrip_MiddleNodeNear50", ok,
            $"T[2]={Tmid:F4}, ожид.≈{expected}, relErr={relErr:P2}");
    }

    /// <summary>
    /// 5 узлов на нижней грани x∈[0,1], 4 CST-треугольника с общей вершиной (0.5, h).
    /// </summary>
    private static HeatMesh Create1DStripMesh(double height = 0.1)
    {
        double[] x =
        [
            0.0, 0.25, 0.5, 0.75, 1.0,   // нижний ряд
            0.5                           // вершина веера
        ];
        double[] y =
        [
            0.0, 0.0, 0.0, 0.0, 0.0,
            height
        ];
        int[][] elements =
        [
            [0, 1, 5],
            [1, 2, 5],
            [2, 3, 5],
            [3, 4, 5]
        ];
        return new HeatMesh(x, y, elements);
    }
}
