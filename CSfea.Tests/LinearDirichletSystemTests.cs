using CSfea.Core;

namespace CSfea.Tests;

public static class LinearDirichletSystemTests
{
    public static void RunAll()
    {
        TestHarness.Section("LinearDirichletSystem: факторизация переиспользуется между Solve()");

        var laminate = IsotropicLaminate(e: 30e9, nu: 0.0, thickness: 0.2);
        // Единичный T3-элемент 1x1 м.
        double[][] nodes =
        [
            [0, 0, 0], [1, 0, 0], [0, 1, 0],
        ];
        int[][] elements = [[0, 1, 2]];
        var mesh = new ShellMesh(nodes, elements, laminate);

        // Узел 0 — полностью закреплён (6 DOF), узлы 1,2 — закреплены по Ux (индекс DOF 0).
        int[] fixedDofs = [0, 1, 2, 3, 4, 5, 6, 12];
        var system = new LinearDirichletSystem(mesh, fixedDofs);

        TestHarness.Check("FactorizeCount == 1 после конструктора", system.FactorizeCount == 1,
            $"FactorizeCount={system.FactorizeCount}");

        double[] u1 = system.Solve(new double[] { 0, 0, 0, 0, 0, 0, 0, 0.001 });
        double[] u2 = system.Solve(new double[] { 0, 0, 0, 0, 0, 0, 0, 0.002 });

        TestHarness.Check("FactorizeCount не растёт после Solve()", system.FactorizeCount == 1,
            $"FactorizeCount={system.FactorizeCount}");
        TestHarness.Check("Решение линейно масштабируется с uFixed (u2 ≈ 2·u1 на свободных DOF)",
            AreApproximatelyProportional(u1, u2, 2.0, mesh.NDof, fixedDofs),
            $"u1={string.Join(",", u1)} u2={string.Join(",", u2)}");
        TestHarness.Check("Решение конечно", u1.All(double.IsFinite) && u2.All(double.IsFinite), "");
    }

    /// <summary>CSfea.Core не имеет фабричного Laminate.Isotropic — строим однослойный
    /// изотропный ламинат явно через реальный конструктор Laminate(IEnumerable&lt;Ply&gt;).</summary>
    internal static Laminate IsotropicLaminate(double e, double nu, double thickness)
    {
        double g = e / (2.0 * (1.0 + nu));
        var material = new OrthotropicMaterial(e, e, nu, g);
        return new Laminate([new Ply(material, 0.0, thickness)]);
    }

    static bool AreApproximatelyProportional(double[] a, double[] b, double factor, int ndof, int[] fixedDofs)
    {
        var fixedSet = new HashSet<int>(fixedDofs);
        for (int i = 0; i < ndof; i++)
        {
            if (fixedSet.Contains(i)) continue;
            double scale = Math.Max(Math.Abs(a[i]) * factor, 1e-12);
            if (Math.Abs(b[i] - factor * a[i]) > 1e-6 * scale) return false;
        }
        return true;
    }
}
