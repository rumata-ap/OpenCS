using CSfea.Core;
using CSfea.Sparse;

namespace CSfea.Tests;

/// <summary>Проверки оболочечного слоя против аналитики и чисел из fea/README.md.</summary>
public static class ShellTests
{
    private const double E = 210e9;
    private const double Nu = 0.3;
    private const double H = 0.01;
    private const double L = 1.0;

    private static double FlexuralRigidity()
        => E * H * H * H / (12.0 * (1.0 - Nu * Nu));

    /// <summary>Симметрия локальной K и нулевая энергия жёстких трансляций.</summary>
    public static void RunElementChecks()
    {
        TestHarness.Section("Локальный элемент Shell4: симметрия и жёсткие моды");
        var coords = new[]
        {
            new[] { 0.0, 0.0, 0.0 },
            new[] { 1.0, 0.0, 0.0 },
            new[] { 1.0, 1.0, 0.0 },
            new[] { 0.0, 1.0, 0.0 },
        };
        var lam = PlateBuilder.Plate(E, Nu, H);
        var k = ShellElementMatrices.ElementKLinearGlobal(coords, lam);

        int n = k.GetLength(0);
        double maxAbs = 0.0, asym = 0.0;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                maxAbs = Math.Max(maxAbs, Math.Abs(k[i, j]));
                asym = Math.Max(asym, Math.Abs(k[i, j] - k[j, i]));
            }
        TestHarness.Check("Симметрия K", asym / maxAbs < 1e-10, $"asym/max={asym / maxAbs:e2}");

        // Жёсткие трансляции по x, y, z: K·t ≈ 0.
        for (int comp = 0; comp < 3; comp++)
        {
            var t = new double[n];
            for (int node = 0; node < 4; node++) t[6 * node + comp] = 1.0;
            var kt = Dense.MatVec(k, t);
            double res = Dense.Norm(kt) / maxAbs;
            TestHarness.Check($"Жёсткая трансляция comp={comp}", res < 1e-9, $"||K·t||/max={res:e2}");
        }
    }

    /// <summary>Защемлённая пластина под центральной силой: сходимость к Тимошенко.</summary>
    public static void RunClampedPlateLinear()
    {
        TestHarness.Section("Защемлённая пластина, P в центре (Тимошенко w=0.00560 P a²/D)");
        double d = FlexuralRigidity();
        double wAnalytic = 0.00560 * 1.0 * L * L / d;

        var tolByN = new Dictionary<int, double> { [8] = 0.05, [16] = 0.015, [24] = 0.01 };
        foreach (int nn in new[] { 8, 16, 24 })
        {
            var (nodes, elements, ni) = PlateBuilder.Build(nn, L);
            var mesh = new ShellMesh(nodes, elements, PlateBuilder.Plate(E, Nu, H));
            var fixedDofs = PlateBuilder.ClampedBoundary(mesh, L);

            var f = new double[mesh.NDof];
            int center = ni(nn / 2, nn / 2);
            f[6 * center + 2] = -1.0;

            var u = mesh.SolveLinear(f, fixedDofs);
            double w = Math.Abs(u[6 * center + 2]);
            TestHarness.CheckRel($"w_center {nn}×{nn}", w, wAnalytic, tolByN[nn]);
        }
    }

    /// <summary>Фон Карман: укрепление защемлённой пластины 12×12 под равномерной q.</summary>
    public static void RunVonKarman()
    {
        TestHarness.Section("Фон Карман: защемлённая пластина 12×12, равномерная q");
        int nn = 12;
        var (nodes, elements, ni) = PlateBuilder.Build(nn, L);
        var lam = PlateBuilder.Plate(E, Nu, H);
        var mesh = new ShellMesh(nodes, elements, lam);
        var fixedDofs = PlateBuilder.ClampedBoundary(mesh, L);
        int center = ni(nn / 2, nn / 2);
        double ae = (L / nn) * (L / nn);

        // Эталоны fea/README.md: q -> (w_lin/h, w_nl/h, stiffening).
        var cases = new (double Q, double WLin, double WNl, double Stiff)[]
        {
            (4e4, 0.262, 0.254, 1.03),
            (2e5, 1.312, 0.918, 1.43),
            (1e6, 6.559, 2.027, 3.24),
        };

        foreach (var c in cases)
        {
            var f = LumpedUniform(mesh, c.Q, ae);
            var uLin = mesh.SolveLinear(f, fixedDofs);
            double wLin = Math.Abs(uLin[6 * center + 2]) / H;

            var (uNl, hist) = mesh.SolveNonlinear(f, fixedDofs, nSteps: 10, tol: 1e-8, maxIter: 30);
            double wNl = Math.Abs(uNl[6 * center + 2]) / H;
            double stiff = wNl != 0.0 ? wLin / wNl : 0.0;
            double lastResid = hist[^1].Residual;

            Console.WriteLine($"  q={c.Q:e1}: w_lin/h={wLin:f3} (ref {c.WLin}), " +
                              $"w_nl/h={wNl:f3} (ref {c.WNl}), укрепление={stiff:f2} (ref {c.Stiff})");
            TestHarness.CheckRel($"w_lin/h q={c.Q:e0}", wLin, c.WLin, 0.05);
            TestHarness.CheckRel($"w_nl/h q={c.Q:e0}", wNl, c.WNl, 0.10);
            TestHarness.Check($"стиффенинг q={c.Q:e0} (w_nl<w_lin)", wNl <= wLin + 1e-9);
            TestHarness.Check($"сходимость NR q={c.Q:e0}", lastResid < 1e-7, $"resid={lastResid:e2}");
        }
    }

    private static double[] LumpedUniform(ShellMesh mesh, double q, double ae)
    {
        var f = new double[mesh.NDof];
        foreach (var el in mesh.Elements)
        {
            double share = -q * ae / 4.0;  // вниз
            foreach (int node in el)
                f[6 * node + 2] += share;
        }
        return f;
    }
}
