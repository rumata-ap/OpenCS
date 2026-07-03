using CSfea.Core;
using CSfea.Sparse;
using CSfea.Sparse.CSparseBackend;

namespace CSfea.Tests;

/// <summary>Кросс-валидация разреженных решателей на реальной FEM-матрице.</summary>
public static class SolverTests
{
    public static void RunCrossValidation()
    {
        TestHarness.Section("Решатели: SparseLU vs CSparse vs CG на K пластины");
        int nn = 10;
        double l = 1.0;
        var (nodes, elements, ni) = PlateBuilder.Build(nn, l);
        var mesh = new ShellMesh(nodes, elements, PlateBuilder.Plate(210e9, 0.3, 0.01));
        var fixedDofs = PlateBuilder.ClampedBoundary(mesh, l);

        var f = new double[mesh.NDof];
        f[6 * ni(nn / 2, nn / 2) + 2] = -1.0;

        var k = mesh.AssembleK();
        var reduced = DirichletReducer.Reduce(k, f, fixedDofs, null);
        var kff = reduced.Kff;
        var rhs = reduced.Fmod;

        var xLu = SparseLuSolver.SolveOnce(kff, rhs);
        var xCs = CSparseSolver.SolveOnce(kff, rhs);
        var cg = ConjugateGradient.Solve(kff, rhs, rtol: 1e-12);

        double scale = Math.Max(Dense.MaxAbs(xLu), 1e-30);
        TestHarness.Check("SparseLU ↔ CSparse", MaxDiff(xLu, xCs) / scale < 1e-8,
            $"reldiff={MaxDiff(xLu, xCs) / scale:e2}");
        TestHarness.Check("SparseLU ↔ CG", cg.Converged && MaxDiff(xLu, cg.X) / scale < 1e-6,
            $"reldiff={MaxDiff(xLu, cg.X) / scale:e2}, cg_iter={cg.Iterations}");

        // Невязка прямого решения.
        var resid = Dense.SubV(kff.Multiply(xLu), rhs);
        double residNorm = Dense.Norm(resid) / Math.Max(Dense.Norm(rhs), 1e-30);
        TestHarness.Check("Невязка SparseLU", residNorm < 1e-10, $"||Kx−b||/||b||={residNorm:e2}");
    }

    private static double MaxDiff(double[] a, double[] b)
    {
        double m = 0.0;
        for (int i = 0; i < a.Length; i++) m = Math.Max(m, Math.Abs(a[i] - b[i]));
        return m;
    }
}
