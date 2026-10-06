using CSfea.Core;
using CSfea.Sparse;

namespace CSfea.Tests;

/// <summary>Совместная сетка оболочек и стержней <see cref="StructuralMesh"/>.</summary>
[HarnessChecks]
public class StructuralMeshTests
{
    private const double E = 30e9;
    private const double Nu = 0.2;

    [Fact]
    public static void RunAll()
    {
        RunFrameEquivalence();
        RunShellEquivalence();
        RunPlateOnColumns();
    }

    private static double MaxRelDiff(double[] a, double[] b)
    {
        double scale = Math.Max(a.Max(Math.Abs), 1e-300);
        double d = 0.0;
        for (int i = 0; i < a.Length; i++) d = Math.Max(d, Math.Abs(a[i] - b[i]));
        return d / scale;
    }

    /// <summary>Рама из стержней в StructuralMesh = FrameMesh3D (линейно и CR).</summary>
    private static void RunFrameEquivalence()
    {
        TestHarness.Section("StructuralMesh: стержни = FrameMesh3D");
        var nodes = new[]
        {
            new[] { 0.0, 0.0, 0.0 }, new[] { 0.0, 0.0, 3.0 }, new[] { 4.0, 0.0, 3.0 }, new[] { 4.0, 1.0, 0.0 },
        };
        var els = new[] { (0, 1), (1, 2), (2, 3) };
        var sec = new BeamSection(E, 0.12, 9e-4, 1.6e-3, 1.2e-3);
        var refVec = new[] { 0.0, 1.0, 0.0 };
        var frame = new FrameMesh3D(nodes, els, sec, refVec);
        var resp = new LinearBeamResponse(sec);
        var mesh = new StructuralMesh(nodes, null, els.Select(e => new StructuralBeam(e.Item1, e.Item2, resp, refVec)).ToArray());

        var f = new double[mesh.NDof];
        f[6 * 1 + 0] = 2e4; f[6 * 2 + 1] = -1e4; f[6 * 2 + 2] = -5e4; f[6 * 1 + 5] = 3e3;
        int[] fixedDofs = Enumerable.Range(0, 6).Concat(Enumerable.Range(18, 6)).ToArray();

        var uF = frame.SolveLinear(f, fixedDofs);
        var uS = mesh.SolveLinear(f, BoundaryConditions.FromArrays(mesh, fixedDofs));
        double d = MaxRelDiff(uF, uS);
        TestHarness.Check("линейно: u совпадают", d < 1e-12, $"max|Δu|/max|u|={d:e2}");

        var f2 = Dense.ScaleV(f, 40.0);
        var (uFn, recF) = frame.SolveNonlinearCR(f2, fixedDofs, nSteps: 4, tol: 1e-7, maxIter: 30);
        var (uSn, recS) = mesh.SolveNonlinear(f2, BoundaryConditions.FromArrays(mesh, fixedDofs),
                                              nSteps: 4, tol: 1e-7, maxIter: 30, lineSearch: false);
        double dn = MaxRelDiff(uFn, uSn);
        TestHarness.Check("CR: обе сошлись", recF.AllConverged() && recS.AllConverged(),
            $"рама: {recF.AllConverged()}, совместная: {recS.AllConverged()} (невязка {recS[^1].Residual:e2})");
        TestHarness.Check("CR: u совпадают", dn < 1e-6, $"max|Δu|/max|u|={dn:e2}");
    }

    /// <summary>Пластина в StructuralMesh = ShellMesh (линейно, фон Карман, CR).</summary>
    private static void RunShellEquivalence()
    {
        TestHarness.Section("StructuralMesh: оболочки = ShellMesh");
        int nn = 6;
        double l = 1.0, h = 0.01, q = 2e5;
        var (nodes, elements, ni) = PlateBuilder.Build(nn, l);
        var resp = new LinearLaminateResponse(PlateBuilder.Plate(210e9, 0.3, h));
        var shellMesh = new ShellMesh(nodes, elements, Enumerable.Repeat<IShellSectionResponse>(resp, elements.Length).ToArray());
        var mesh = new StructuralMesh(nodes, elements.Select(el => new StructuralShell(el, resp)).ToArray(), null);
        var fixedDofs = PlateBuilder.ClampedBoundary(shellMesh, l);
        double ae = (l / nn) * (l / nn);
        var f = new double[mesh.NDof];
        foreach (var el in elements)
            foreach (int node in el) f[6 * node + 2] -= q * ae / 4.0;
        var bc = BoundaryConditions.FromArrays(mesh, fixedDofs);

        double d = MaxRelDiff(shellMesh.SolveLinear(f, fixedDofs), mesh.SolveLinear(f, bc));
        TestHarness.Check("линейно: u совпадают", d < 1e-12, $"{d:e2}");

        var (uVk, _) = shellMesh.SolveNonlinear(f, fixedDofs, nSteps: 3, tol: 1e-10, maxIter: 30, lineSearch: false);
        var (uVkS, hVk) = mesh.SolveNonlinear(f, bc, nSteps: 3, tol: 1e-10, maxIter: 30, lineSearch: false, corotational: false);
        double dvk = MaxRelDiff(uVk, uVkS);
        TestHarness.Check("фон Карман: сошлось и u совпадают", hVk.AllConverged() && dvk < 1e-8, $"{dvk:e2}");

        var shellMeshCr = new ShellMesh(nodes, elements, Enumerable.Repeat<IShellSectionResponse>(resp, elements.Length).ToArray());
        var (uCr, _) = ShellCorotational.SolveNonlinearCR(shellMeshCr, f, fixedDofs, nSteps: 3, tol: 1e-10, maxIter: 30, lineSearch: false);
        var (uCrS, hCr) = mesh.SolveNonlinear(f, bc, nSteps: 3, tol: 1e-10, maxIter: 30, lineSearch: false);
        double dcr = MaxRelDiff(uCr, uCrS);
        TestHarness.Check("CR: сошлось и u совпадают", hCr.AllConverged() && dcr < 1e-8, $"{dcr:e2}");
    }

    /// <summary>Плита на четырёх стойках: равновесие реакций и симметрия.</summary>
    private static void RunPlateOnColumns()
    {
        TestHarness.Section("StructuralMesh: плита на четырёх стойках — сумма реакций");
        int nn = 8;
        double l = 6.0, h = 0.2, hc = 3.0, q = 10e3;
        var (plateNodes, elements, ni) = PlateBuilder.Build(nn, l);
        var nodes = plateNodes.Select(p => new[] { p[0], p[1], hc }).ToList();
        var corners = new[] { ni(0, 0), ni(nn, 0), ni(nn, nn), ni(0, nn) };
        var bases = new List<int>();
        foreach (int c in corners)
        {
            bases.Add(nodes.Count);
            nodes.Add(new[] { nodes[c][0], nodes[c][1], 0.0 });
        }
        var plate = new LinearLaminateResponse(PlateBuilder.Plate(E, Nu, h));
        var col = new LinearBeamResponse(new BeamSection(E, 0.16, 2.13e-3, 2.13e-3, 3.6e-3));
        var beams = corners.Select((c, i) => new StructuralBeam(bases[i], c, col, new[] { 1.0, 0.0, 0.0 })).ToArray();
        var mesh = new StructuralMesh(nodes.ToArray(), elements.Select(el => new StructuralShell(el, plate)).ToArray(), beams);
        var bc = new BoundaryConditions(mesh).Fix(bases);

        var f = new double[mesh.NDof];
        double ae = (l / nn) * (l / nn);
        foreach (var el in elements)
            foreach (int node in el) f[6 * node + 2] -= q * ae / 4.0;
        var u = mesh.SolveLinear(f, bc);
        var r = Reactions.Compute(mesh, u, bc);

        double sz = bases.Sum(b => r[6 * b + 2]);
        double sx = bases.Sum(b => r[6 * b + 0]);
        double sy = bases.Sum(b => r[6 * b + 1]);
        TestHarness.CheckRel("ΣRz = q·A", sz, q * l * l, 1e-9);
        TestHarness.Check("ΣRx ≈ 0, ΣRy ≈ 0", Math.Abs(sx) + Math.Abs(sy) < 1e-6 * q * l * l, $"ΣRx={sx:e2}, ΣRy={sy:e2}");
        var rz = bases.Select(b => r[6 * b + 2]).ToArray();
        TestHarness.Check("симметрия: реакции стоек равны", rz.Max() - rz.Min() < 1e-6 * rz.Max(), string.Join("; ", rz.Select(x => x.ToString("f1"))));
        int center = ni(nn / 2, nn / 2);
        TestHarness.Check("прогиб центра вниз и больше, чем у угла",
            u[6 * center + 2] < u[6 * corners[0] + 2] && u[6 * corners[0] + 2] < 0,
            $"w_c={u[6 * center + 2] * 1e3:f3} мм, w_угла={u[6 * corners[0] + 2] * 1e3:f4} мм");
    }
}
