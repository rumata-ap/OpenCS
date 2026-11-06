using CSfea.Core;
using CSfea.Sparse;

namespace CSfea.Tests;

/// <summary>
/// Запасной LU многоправого линейного расчёта (<see cref="StructuralMesh.SolveLinear(IReadOnlyList{double[]}, BoundaryConditions, Func{int, double[,]}?, IReadOnlyList{double[]?}?)"/>):
/// матрица не SPD — одно разложение на все правые части, решения как у одиночного пути, признак <see cref="StructuralMesh.LastLinearNotSpd"/>.
/// </summary>
[HarnessChecks]
public class LinearLuFallbackTests
{
    private const double E = 30e9;
    private const double I = 2.13e-3;
    private static readonly BeamSection Sec = new(E, 0.16, I, I, 3.6e-3);
    private static readonly double[] RefY = { 0.0, 1.0, 0.0 };

    /// <summary>Отрицательная пружина uz на конце: диагональ uz отрицательна — K не SPD, но невырождена.</summary>
    private const double NegativeSpring = -10 * 12 * E * I;

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Запасной LU: одно разложение на все правые части");
        var nodes = new[] { new[] { 0.0, 0.0, 0.0 }, new[] { 1.0, 0.0, 0.0 }, new[] { 2.0, 0.0, 0.0 } };
        var resp = new LinearBeamResponse(Sec);
        var mesh = new StructuralMesh(nodes, null, new[] { new StructuralBeam(0, 1, resp, RefY), new StructuralBeam(1, 2, resp, RefY) });
        var f1 = new double[mesh.NDof];
        f1[6 * 2 + 2] = -1e4;
        var f2 = new double[mesh.NDof];
        f2[6 * 2 + 1] = 1e4;

        var bc = new BoundaryConditions(mesh).Fix(new[] { 0 }).Spring(2, 2, NegativeSpring);
        var both = mesh.SolveLinear(new[] { f1, f2 }, bc);
        string? where = mesh.LastLinearNotSpd;
        TestHarness.Check("не SPD: признак задан", where != null, "LastLinearNotSpd = null");
        var one1 = mesh.SolveLinear(f1, bc);
        var one2 = mesh.SolveLinear(f2, bc);
        TestHarness.Check("правая часть 1 = одиночный путь", MaxRelDiff(one1, both[0]) < 1e-10, $"{MaxRelDiff(one1, both[0]):e2}");
        TestHarness.Check("правая часть 2 = одиночный путь", MaxRelDiff(one2, both[1]) < 1e-10, $"{MaxRelDiff(one2, both[1]):e2}");

        // Невязка K·u − f на свободных DOF (K с пружиной).
        var k = mesh.AssembleK().ToCsc();
        foreach (var (u, f, name) in new[] { (both[0], f1, "1"), (both[1], f2, "2") })
        {
            var r = k.Multiply(u);
            r[6 * 2 + 2] += NegativeSpring * u[6 * 2 + 2];
            double res = 0, scale = 0;
            for (int d = 6; d < mesh.NDof; d++)
            {
                res = Math.Max(res, Math.Abs(r[d] - f[d]));
                scale = Math.Max(scale, Math.Abs(f[d]));
            }
            TestHarness.Check($"правая часть {name}: K·u = f", res / scale < 1e-8, $"{res / scale:e2}");
        }

        var spd = new BoundaryConditions(mesh).Fix(new[] { 0 });
        mesh.SolveLinear(new[] { f1, f2 }, spd);
        TestHarness.Check("SPD: признак снят", mesh.LastLinearNotSpd == null, mesh.LastLinearNotSpd ?? "");
    }

    private static double MaxRelDiff(double[] a, double[] b)
    {
        double max = 0, scale = 0;
        for (int i = 0; i < a.Length; i++)
        {
            max = Math.Max(max, Math.Abs(a[i] - b[i]));
            scale = Math.Max(scale, Math.Abs(a[i]));
        }
        return scale > 0 ? max / scale : max;
    }
}
