using CSfea.Core;

namespace CSfea.Tests;

/// <summary>Признак сходимости шаговых нелинейных решателей (фон Карман, CR оболочек, CR рамы).</summary>
[HarnessChecks]
public class NonlinearConvergenceTests
{
    private const double E = 210e9;
    private const double Nu = 0.3;
    private const double H = 0.01;
    private const double L = 1.0;

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Признак сходимости: фон Карман и CR оболочек, CR рамы");
        int nn = 8;
        var (nodes, elements, ni) = PlateBuilder.Build(nn, L);
        var lam = PlateBuilder.Plate(E, Nu, H);
        double ae = (L / nn) * (L / nn);
        double q = 1e6; // сильная геометрическая нелинейность (w ≈ 2h)

        double[] Load(ShellMesh m)
        {
            var f = new double[m.NDof];
            foreach (var el in m.Elements)
                foreach (int node in el)
                    f[6 * node + 2] -= q * ae / 4.0;
            return f;
        }

        var mesh = new ShellMesh(nodes, elements, lam);
        var fixedDofs = PlateBuilder.ClampedBoundary(mesh, L);
        var f = Load(mesh);

        var (_, ok) = mesh.SolveNonlinear(f, fixedDofs, nSteps: 4, tol: 1e-8, maxIter: 30);
        TestHarness.Check("фон Карман, maxIter=30: AllConverged", ok.AllConverged(),
                          $"шагов={ok[^1].Step}, последняя невязка={ok[^1].Residual:e2}");
        TestHarness.Check("фон Карман: у сошедшегося шага последняя запись Converged",
                          ok[^1].Converged);

        var mesh1 = new ShellMesh(nodes, elements, lam);
        var (_, bad) = mesh1.SolveNonlinear(f, fixedDofs, nSteps: 4, tol: 1e-8, maxIter: 1);
        TestHarness.Check("фон Карман, maxIter=1: AllConverged = false", !bad.AllConverged());
        TestHarness.Check("фон Карман, maxIter=1: первый несошедшийся шаг = 1",
                          bad.FirstFailedStep() == 1, $"FirstFailedStep={bad.FirstFailedStep()}");
        TestHarness.Check("фон Карман, maxIter=1: ни одной записи Converged", bad.All(r => !r.Converged));

        var meshCr = new ShellMesh(nodes, elements, lam);
        var (_, badCr) = ShellCorotational.SolveNonlinearCR(meshCr, Load(meshCr), fixedDofs,
            nSteps: 2, tol: 1e-8, maxIter: 1);
        TestHarness.Check("CR оболочек, maxIter=1: AllConverged = false", !badCr.AllConverged(),
                          $"FirstFailedStep={badCr.FirstFailedStep()}");

        // Сводка по синтетической истории: шаг 1 сошёлся, шаг 2 нет, шаг 3 сошёлся.
        var synthetic = new List<ShellMesh.NewtonRecord>
        {
            new(1, 1, 1.0), new(1, 2, 1e-9, true),
            new(2, 1, 1.0), new(2, 2, 1e-3),
            new(3, 1, 1.0), new(3, 2, 1e-9, true),
        };
        TestHarness.Check("синтетическая история: первый несошедшийся = 2", synthetic.FirstFailedStep() == 2);

        // Рама: консоль под большой поперечной силой, одна итерация на шаг.
        var frameNodes = Enumerable.Range(0, 5).Select(i => new[] { i * 0.25, 0.0, 0.0 }).ToArray();
        var frameEls = Enumerable.Range(0, 4).Select(i => (i, i + 1)).ToArray();
        var section = new BeamSection(E, 1e-3, 1e-7, 1e-7, 2e-7);
        var frame = new FrameMesh3D(frameNodes, frameEls, section, refVec: new[] { 0.0, 0.0, 1.0 });
        var ff = new double[frame.NDof];
        ff[6 * 4 + 1] = 2.0e3;
        int[] fixedFrame = { 0, 1, 2, 3, 4, 5 };
        var (_, recOk) = frame.SolveNonlinearCR(ff, fixedFrame, nSteps: 4, maxIter: 30);
        TestHarness.Check("CR рамы, maxIter=30: AllConverged", recOk.AllConverged());
        var frame1 = new FrameMesh3D(frameNodes, frameEls, section, refVec: new[] { 0.0, 0.0, 1.0 });
        var (_, recBad) = frame1.SolveNonlinearCR(ff, fixedFrame, nSteps: 4, maxIter: 1);
        TestHarness.Check("CR рамы, maxIter=1: первый несошедшийся шаг = 1", recBad.FirstFailedStep() == 1);
    }
}
