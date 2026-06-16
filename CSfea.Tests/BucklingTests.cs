using CSfea.Core;

namespace CSfea.Tests;

/// <summary>Линейная устойчивость: буклинг шарнирно-опёртой пластины под N_x.</summary>
public static class BucklingTests
{
    public static void RunSimplySupportedPlate()
    {
        TestHarness.Section("Буклинг квадратной пластины под N_x vs Тимошенко");

        double e = 210e9, nu = 0.3, l = 1.0, h = 0.01;
        double d = e * h * h * h / (12.0 * (1.0 - nu * nu));
        double nCrRef = 4.0 * Math.PI * Math.PI * d / (l * l);   // m=1, квадрат

        int n = 16;
        var (nodes, elements, ni) = PlateBuilder.Build(n, l);
        var mesh = new ShellMesh(nodes, elements, PlateBuilder.Plate(e, nu, h));

        // Шарнир по всем рёбрам (w=0), левый край u=0, нижний край v=0,
        // запрет drilling — как в эталонном демо Python.
        var fixedList = new List<int>();
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
            {
                int nid = ni(i, j);
                bool onBoundary = i == 0 || i == n || j == 0 || j == n;
                if (onBoundary) fixedList.Add(6 * nid + 2);
                if (i == 0) fixedList.Add(6 * nid + 0);
                if (j == 0) fixedList.Add(6 * nid + 1);
            }
        var fixedDofs = ShellMesh.UnionDofs(fixedList.ToArray(), ShellMesh.FixAllDrilling(mesh));

        // Равномерное сжатие правого края, суммарно 1 Н (масштабируется λ).
        var f = new double[mesh.NDof];
        double dy = l / n, pTotal = -1.0;
        for (int j = 0; j <= n; j++)
        {
            int nid = ni(n, j);
            f[6 * nid + 0] = (j == 0 || j == n) ? pTotal * dy / 2 : pTotal * dy;
        }

        var res = mesh.SolveBuckling(f, fixedDofs, nModes: 3);
        double lamCr = res.LambdaCr.Where(v => v > 0).DefaultIfEmpty(double.NaN).Min();
        double nCrNum = lamCr * 1.0 / l;

        TestHarness.Section($"  λ_cr = {lamCr:e4}, N_cr_num = {nCrNum:e4}, N_cr_ref = {nCrRef:e4}");
        TestHarness.Check("N_cr пластины (k=4)", Math.Abs(nCrNum - nCrRef) / nCrRef < 0.05,
            $"err={(nCrNum - nCrRef) / nCrRef * 100:+0.00;-0.00}%");
    }
}
