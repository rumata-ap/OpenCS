using CSfea.Core;
using CSfea.Sparse;

namespace CSfea.Tests;

/// <summary>Проверки коротационной формулировки оболочек.</summary>
public static class CrShellTests
{
    private const double E = 210e9;
    private const double Nu = 0.3;
    private const double H = 0.01;
    private const double L = 1.0;

    /// <summary>Инвариантность F_int к жёсткому повороту элемента.</summary>
    public static void RunRigidRotation()
    {
        TestHarness.Section("CR: инвариантность к жёсткому повороту (F_int ≈ 0)");
        var coordsRef = new[]
        {
            new[] { 0.0, 0.0, 0.0 },
            new[] { 1.0, 0.0, 0.0 },
            new[] { 1.0, 1.0, 0.0 },
            new[] { 0.0, 1.0, 0.0 },
        };
        var section = new LinearLaminateResponse(PlateBuilder.Plate(E, Nu, H));

        foreach (var theta in new[] { new[] { 0.0, 0.0, 0.3 }, new[] { 0.2, -0.1, 0.4 }, new[] { 0.5, 0.5, 0.5 } })
        {
            var rot = So3.Exp(theta);
            var u = new double[24];
            for (int i = 0; i < 4; i++)
            {
                var xNew = Dense.MatVec(rot, coordsRef[i]); // жёсткий поворот вокруг начала
                for (int k = 0; k < 3; k++) u[6 * i + k] = xNew[k] - coordsRef[i][k];
                for (int k = 0; k < 3; k++) u[6 * i + 3 + k] = theta[k];
            }
            var (fGlobal, kGlobal) = ShellCorotational.ElementCR(coordsRef, section, u);
            double scale = Dense.MaxAbs(Dense.Diagonal(kGlobal)) * Math.Max(Dense.Norm(u), 1.0);
            double rel = Dense.Norm(fGlobal) / scale;
            TestHarness.Check($"||F_int||/масштаб при θ=[{theta[0]:f1},{theta[1]:f1},{theta[2]:f1}]",
                              rel < 1e-8, $"rel={rel:e2}");
        }
    }

    /// <summary>CR совпадает с фон Карманом при умеренной нагрузке.</summary>
    public static void RunAgreementWithVonKarman()
    {
        TestHarness.Section("CR ↔ фон Карман: согласие при умеренной нагрузке");
        int nn = 10;
        var (nodes, elements, ni) = PlateBuilder.Build(nn, L);
        var lam = PlateBuilder.Plate(E, Nu, H);
        int center = ni(nn / 2, nn / 2);
        double ae = (L / nn) * (L / nn);

        var meshVk = new ShellMesh(nodes, elements, lam);
        var meshCr = new ShellMesh(nodes, elements, lam);
        var fixedVk = PlateBuilder.ClampedBoundary(meshVk, L);
        var fixedCr = PlateBuilder.ClampedBoundary(meshCr, L);

        double q = 4e4;
        var fVk = new double[meshVk.NDof];
        var fCr = new double[meshCr.NDof];
        foreach (var el in elements)
            foreach (int node in el)
            {
                fVk[6 * node + 2] -= q * ae / 4.0;
                fCr[6 * node + 2] -= q * ae / 4.0;
            }

        var (uVk, _) = meshVk.SolveNonlinear(fVk, fixedVk, nSteps: 5, tol: 1e-8, maxIter: 30);
        var (uCr, histCr) = ShellCorotational.SolveNonlinearCR(meshCr, fCr, fixedCr,
            nSteps: 5, tol: 1e-7, maxIter: 30, numericalTangent: true);

        double wVk = Math.Abs(uVk[6 * center + 2]);
        double wCr = Math.Abs(uCr[6 * center + 2]);
        Console.WriteLine($"  w_vk={wVk:e4}, w_cr={wCr:e4}, отн.разн={Math.Abs(wCr - wVk) / wVk * 100:f3}%");
        TestHarness.CheckRel("w_cr vs w_vk", wCr, wVk, 0.03);
        TestHarness.Check("CR сходимость", histCr[^1].Residual < 1e-6, $"resid={histCr[^1].Residual:e2}");
    }
}
