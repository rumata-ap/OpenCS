using CSfea.Thermal;
using CSfea.Thermal.Bc;
using CSfea.Thermal.Materials;
using CSfea.Thermal.Solvers;

namespace CSfea.Tests;

/// <summary>Тесты нестационарного решателя теплопроводности TransientHeatSolver.</summary>
public static class HeatTransientTests
{
    public static void RunAll()
    {
        TestHarness.Section("HeatTransient: θ-схема + Пикар");
        UniformZeroFlux();
        HeatingFromRobin();
        PicardConverges();
    }

    /// <summary>
    /// При отсутствии граничного теплообмена температура должна сохраняться равной начальной.
    /// </summary>
    private static void UniformZeroFlux()
    {
        var mesh = CreateSingleTriangleMesh();
        var mat = new ConstantHeatMaterial(lambda: 1.0, rhocp: 1.0);
        var options = new TransientHeatOptions
        {
            Duration_s = 10.0,
            TimeStep_s = 1.0,
            SnapshotStep_s = 2.0,
            Theta = 1.0,
            PicardMaxIter = 10,
            PicardTolCelsius = 1e-9,
            TInitCelsius = 20.0,
            AdaptiveFirstMinute = false
        };

        var result = TransientHeatSolver.Solve(mesh, mat, options, boundaryEdges: []);

        bool ok = true;
        foreach (double[] snap in result.Snapshots)
        {
            for (int i = 0; i < snap.Length; i++)
            {
                if (Math.Abs(snap[i] - 20.0) > 1e-6)
                {
                    ok = false;
                    break;
                }
            }
            if (!ok)
                break;
        }

        TestHarness.Check("HeatTransient_UniformZeroFlux", ok,
            $"snapshots={result.Snapshots.Length}, finalT0={result.Snapshots[^1][0]:F6}");
    }

    /// <summary>
    /// Нагрев со стороны одного ребра по Робину: температура у огневой стороны растёт.
    /// </summary>
    private static void HeatingFromRobin()
    {
        var mesh = CreateStripMesh();
        var mat = new ConstantHeatMaterial(lambda: 1.0, rhocp: 2.0e6);

        var edge = new HeatBoundaryEdge(
            NodeA: 0,
            NodeB: 1,
            LengthM: 0.25,
            BcType: HeatBoundaryBcType.Fire,
            AlphaConv: 25.0,
            Emissivity: 0.0,
            TAmbientCelsius: 20.0);

        var options = new TransientHeatOptions
        {
            Duration_s = 30.0,
            TimeStep_s = 1.0,
            SnapshotStep_s = 5.0,
            Theta = 1.0,
            PicardMaxIter = 20,
            PicardTolCelsius = 0.5,
            TInitCelsius = 20.0,
            AdaptiveFirstMinute = false
        };

        var result = TransientHeatSolver.Solve(
            mesh,
            mat,
            options,
            boundaryEdges: [edge],
            fireCurve: _ => 100.0);

        double[] last = result.Snapshots[^1];
        bool warmed = last[0] > 20.0 + 1e-6;
        bool gradient = last[0] >= last[4];

        TestHarness.Check("HeatTransient_HeatingFromRobin", warmed && gradient,
            $"TfireNode={last[0]:F4}, TfarNode={last[4]:F4}");
    }

    /// <summary>
    /// Проверка наличия записей лога и выполнения критерия сходимости Пикара.
    /// </summary>
    private static void PicardConverges()
    {
        var mesh = CreateStripMesh();
        var mat = new ConstantHeatMaterial(lambda: 1.2, rhocp: 1.5e6);

        var edge = new HeatBoundaryEdge(
            NodeA: 0,
            NodeB: 1,
            LengthM: 0.25,
            BcType: HeatBoundaryBcType.Fire,
            AlphaConv: 25.0,
            Emissivity: 0.2,
            TAmbientCelsius: 20.0);

        var options = new TransientHeatOptions
        {
            Duration_s = 8.0,
            TimeStep_s = 1.0,
            SnapshotStep_s = 2.0,
            Theta = 1.0,
            PicardMaxIter = 20,
            PicardTolCelsius = 0.5,
            TInitCelsius = 20.0,
            AdaptiveFirstMinute = false
        };

        var result = TransientHeatSolver.Solve(
            mesh,
            mat,
            options,
            boundaryEdges: [edge],
            fireCurve: _ => 120.0);

        bool hasLog = result.ConvergenceLog.Count > 0;
        bool iterOk = hasLog && result.ConvergenceLog.All(r => r.NPicardIter >= 1);
        bool residOk = hasLog && result.ConvergenceLog.All(r => r.MaxResidualCelsius < options.PicardTolCelsius + 1e-9);

        TestHarness.Check("HeatTransient_PicardConverges", hasLog && iterOk && residOk,
            hasLog
                ? $"steps={result.ConvergenceLog.Count}, lastIter={result.ConvergenceLog[^1].NPicardIter}, lastRes={result.ConvergenceLog[^1].MaxResidualCelsius:F4}"
                : "empty log");
    }

    private static HeatMesh CreateSingleTriangleMesh()
        => new(
            x: [0.0, 1.0, 0.0],
            y: [0.0, 0.0, 1.0],
            elements: [[0, 1, 2]]);

    private static HeatMesh CreateStripMesh(double height = 0.1)
    {
        double[] x = [0.0, 0.25, 0.5, 0.75, 1.0, 0.5];
        double[] y = [0.0, 0.0, 0.0, 0.0, 0.0, height];
        int[][] elements = [[0, 1, 5], [1, 2, 5], [2, 3, 5], [3, 4, 5]];
        return new HeatMesh(x, y, elements);
    }
}
