using CSfea.Core;

namespace CSfea.Tests;

/// <summary>Поворот сечения оболочки в локальные оси КЭ.</summary>
[HarnessChecks]
public class RotatedShellResponseTests
{
    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("RotatedShellResponse: ламинат под углом = ламинат с Ply.Angle + α");
        var mat = new OrthotropicMaterial(140e9, 10e9, 0.3, 5e9, 5e9, 3.5e9);
        // Несимметричная укладка — чтобы B ≠ 0 и проверялся весь блок ABD.
        Laminate Lam(double add) => new(new[]
        {
            new Ply(mat, 0.2 + add, 0.002), new Ply(mat, -0.7 + add, 0.003), new Ply(mat, 1.1 + add, 0.001),
        });
        var eps = new[] { 1.1e-4, -0.4e-4, 2.3e-4 };
        var kap = new[] { 3e-3, 1.5e-3, -2e-3 };
        var gam = new[] { 1e-4, -2.5e-4 };

        foreach (double alpha in new[] { 0.0, 0.4, -1.3, 2.7 })
        {
            var rotated = new RotatedShellResponse(new LinearLaminateResponse(Lam(0.0)), alpha);
            var direct = new LinearLaminateResponse(Lam(alpha));
            var tr = rotated.Tangent(eps, kap, gam);
            var td = direct.Tangent(eps, kap, gam);
            double err = Math.Max(Math.Max(RelDiff(tr.A, td.A), RelDiff(tr.D, td.D)),
                                  Math.Max(RelDiff(tr.B, td.B, td.A, 0.005), RelDiff(tr.As, td.As)));
            var fr = rotated.Forces(eps, kap, gam);
            var fd = direct.Forces(eps, kap, gam);
            double errF = Math.Max(Math.Max(RelV(fr.N, fd.N), RelV(fr.M, fd.M)), RelV(fr.Q, fd.Q));
            TestHarness.Check($"α={alpha}: ABD/As совпадают", err < 1e-12, $"отн.={err:e2}");
            TestHarness.Check($"α={alpha}: N, M, Q совпадают", errF < 1e-12, $"отн.={errF:e2}");
        }
    }

    private static double RelDiff(double[,] a, double[,] b, double[,]? scaleRef = null, double scaleLen = 1.0)
    {
        double scale = 0.0, d = 0.0;
        var s = scaleRef ?? b;
        foreach (double v in s) scale = Math.Max(scale, Math.Abs(v));
        scale *= scaleLen;
        for (int i = 0; i < a.GetLength(0); i++)
            for (int j = 0; j < a.GetLength(1); j++)
                d = Math.Max(d, Math.Abs(a[i, j] - b[i, j]));
        return d / scale;
    }

    private static double RelV(double[] a, double[] b)
    {
        double scale = b.Max(Math.Abs), d = 0.0;
        for (int i = 0; i < a.Length; i++) d = Math.Max(d, Math.Abs(a[i] - b[i]));
        return d / scale;
    }
}
