using CSfea.Core;

namespace CSfea.Tests;

/// <summary>Проверки балочных элементов: линейные консоли и CR-сворачивание.</summary>
public static class BeamTests
{
    private const double E = 210e9;

    private static (double A, double I) Square(double b) => (b * b, b * b * b * b / 12.0);

    private static (double[][] Nodes, (int, int)[] Elems) Cantilever2D(int nEl, double l)
    {
        var nodes = new double[nEl + 1][];
        for (int i = 0; i <= nEl; i++) nodes[i] = new[] { i * l / nEl, 0.0 };
        var el = new (int, int)[nEl];
        for (int i = 0; i < nEl; i++) el[i] = (i, i + 1);
        return (nodes, el);
    }

    /// <summary>2D-консоль: поперечный и осевой прогиб vs аналитика.</summary>
    public static void RunLinearCantilever2D()
    {
        TestHarness.Section("Балка 2D: линейная консоль vs аналитика");
        double l = 2.0;
        var (a, inertia) = Square(0.05);
        var section = new BeamSection(E, a, inertia);
        int nEl = 8;
        var (nodes, elems) = Cantilever2D(nEl, l);
        var mesh = new FrameMesh2D(nodes, elems, section);
        int last = nEl;
        var fixedDofs = new[] { 0, 1, 2 };

        // Поперечная сила на конце.
        double p = 1000.0;
        var f = new double[mesh.NDof];
        f[3 * last + 1] = p;
        var u = mesh.SolveLinear(f, fixedDofs);
        double vTip = u[3 * last + 1];
        double vAnalytic = p * l * l * l / (3.0 * E * inertia);
        TestHarness.CheckRel("v_tip (P·L³/3EI)", vTip, vAnalytic, 0.02);

        // Осевая сила.
        double pa = 5e5;
        var fa = new double[mesh.NDof];
        fa[3 * last + 0] = pa;
        var ua = mesh.SolveLinear(fa, fixedDofs);
        double uTip = ua[3 * last + 0];
        double uAnalytic = pa * l / (E * a);
        TestHarness.CheckRel("u_tip (P·L/EA)", uTip, uAnalytic, 1e-6);
    }

    /// <summary>2D CR: консоль под концевым моментом сворачивается в дугу.</summary>
    public static void RunCrRollup2D()
    {
        TestHarness.Section("Балка 2D CR: сворачивание в дугу концевым моментом");
        double l = 2.0;
        var (a, inertia) = Square(0.05);
        double ei = E * inertia;
        var section = new BeamSection(E, a, inertia);
        int nEl = 12;
        var (nodes, elems) = Cantilever2D(nEl, l);
        var mesh = new FrameMesh2D(nodes, elems, section);
        int last = nEl;
        var fixedDofs = new[] { 0, 1, 2 };

        double kappaL = Math.PI / 2.0;
        double kappa = kappaL / l;
        double m = kappa * ei;
        var f = new double[mesh.NDof];
        f[3 * last + 2] = m;

        var (u, recs) = mesh.SolveNonlinearCR(f, fixedDofs, nSteps: 12, tol: 1e-8, maxIter: 40);

        double uxTip = u[3 * last + 0];
        double uyTip = u[3 * last + 1];
        double xExact = Math.Sin(kappaL) / kappa;
        double yExact = (1.0 - Math.Cos(kappaL)) / kappa;
        double uxExact = xExact - l;
        double uyExact = yExact;

        Console.WriteLine($"  tip u=({uxTip:f4},{uyTip:f4}), аналитика=({uxExact:f4},{uyExact:f4})");
        TestHarness.CheckRel("u_x кончика", uxTip, uxExact, 0.02);
        TestHarness.CheckRel("u_y кончика", uyTip, uyExact, 0.02);
        TestHarness.Check("CR-сходимость 2D", recs[^1].Converged);
    }

    /// <summary>3D-консоль: поперечный прогиб по обеим главным осям vs аналитика.</summary>
    public static void RunLinearCantilever3D()
    {
        TestHarness.Section("Балка 3D: линейная консоль vs аналитика");
        double l = 2.0;
        var (a, inertia) = Square(0.05);
        var section = new BeamSection(E, a, inertia); // I_y = I_z
        int nEl = 8;
        var nodes = new double[nEl + 1][];
        for (int i = 0; i <= nEl; i++) nodes[i] = new[] { i * l / nEl, 0.0, 0.0 };
        var elems = new (int, int)[nEl];
        for (int i = 0; i < nEl; i++) elems[i] = (i, i + 1);
        var mesh = new FrameMesh3D(nodes, elems, section);
        int last = nEl;
        var fixedDofs = Enumerable.Range(0, 6).ToArray();

        double p = 1000.0;
        double vAnalytic = p * l * l * l / (3.0 * E * inertia);

        foreach (int comp in new[] { 1, 2 }) // global Y, Z
        {
            var f = new double[mesh.NDof];
            f[6 * last + comp] = p;
            var u = mesh.SolveLinear(f, fixedDofs);
            double vTip = u[6 * last + comp];
            TestHarness.CheckRel($"v_tip comp={comp} (P·L³/3EI)", vTip, vAnalytic, 0.02);
        }
    }

    /// <summary>3D CR: консоль под концевым моментом сворачивается в дугу (в плоскости xy).</summary>
    public static void RunCrRollup3D()
    {
        TestHarness.Section("Балка 3D CR: сворачивание в дугу концевым моментом");
        double l = 2.0;
        var (a, inertia) = Square(0.05);
        double ei = E * inertia;
        var section = new BeamSection(E, a, inertia);
        int nEl = 12;
        var nodes = new double[nEl + 1][];
        for (int i = 0; i <= nEl; i++) nodes[i] = new[] { i * l / nEl, 0.0, 0.0 };
        var elems = new (int, int)[nEl];
        for (int i = 0; i < nEl; i++) elems[i] = (i, i + 1);
        var mesh = new FrameMesh3D(nodes, elems, section);
        int last = nEl;
        var fixedDofs = Enumerable.Range(0, 6).ToArray();

        // Момент вокруг глобальной Z → изгиб в плоскости xy.
        double kappaL = Math.PI / 2.0;
        double kappa = kappaL / l;
        double m = kappa * ei;
        var f = new double[mesh.NDof];
        f[6 * last + 5] = m; // θz момент

        var (u, recs) = mesh.SolveNonlinearCR(f, fixedDofs, nSteps: 12, tol: 1e-7, maxIter: 40);

        double uxTip = u[6 * last + 0];
        double uyTip = u[6 * last + 1];
        double uxExact = Math.Sin(kappaL) / kappa - l;
        double uyExact = (1.0 - Math.Cos(kappaL)) / kappa;
        Console.WriteLine($"  tip u=({uxTip:f4},{uyTip:f4}), аналитика=({uxExact:f4},{uyExact:f4})");
        TestHarness.CheckRel("u_x кончика 3D", uxTip, uxExact, 0.03);
        TestHarness.CheckRel("u_y кончика 3D", uyTip, uyExact, 0.03);
        TestHarness.Check("CR-сходимость 3D", recs[^1].Converged);
    }
}
