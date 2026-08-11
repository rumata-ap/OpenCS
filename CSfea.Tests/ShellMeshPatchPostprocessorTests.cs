using CSfea.Core;
using CSfea.CScoreBridge;

namespace CSfea.Tests;

public static class ShellMeshPatchPostprocessorTests
{
    public static void RunAll()
    {
        TestHarness.Section("ShellMeshPatchPostprocessor: area-averaging в общем базисе патча");
        UniformAxialTension_TwoTriangles_MatchesAnalytical();
        RotatedElementConnectivity_SameAnalyticalAxialField_MatchesUnrotatedMesh();
    }

    static void UniformAxialTension_TwoTriangles_MatchesAnalytical()
    {
        const double e = 30e9, nu = 0.0, h = 0.2, epsX = 1e-4;
        var laminate = LinearDirichletSystemTests.IsotropicLaminate(e, nu, h);
        // Квадрат 1x1, разбитый по одной диагонали на 2 треугольника — общая ось патча = мировые X/Y.
        double[][] nodes = [[0, 0, 0], [1, 0, 0], [1, 1, 0], [0, 1, 0]];
        int[][] elements = [[0, 1, 2], [0, 2, 3]];
        var mesh = new ShellMesh(nodes, elements, laminate);

        // Однородное осевое поле Ux = epsX * X, остальные компоненты нулевые.
        var u = new double[mesh.NDof];
        for (int n = 0; n < nodes.Length; n++)
            u[6 * n + 0] = epsX * nodes[n][0];

        double[,] patchBasis = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        var results = ShellMeshPatchPostprocessor.SectionResultantsAt(mesh, u, [0, 0, 0], patchBasis);
        var avg = ShellMeshPatchPostprocessor.Average(results);

        double expectedNx = e * epsX * h; // Па·м = Н/м
        TestHarness.Check("Nx совпадает с аналитикой (однородное растяжение)",
            Math.Abs(avg.Nx - expectedNx) < 0.02 * Math.Abs(expectedNx),
            $"Nx={avg.Nx:e6}, ожидание={expectedNx:e6}");
        TestHarness.Check("Ny ≈ 0", Math.Abs(avg.Ny) < 0.02 * Math.Abs(expectedNx), $"Ny={avg.Ny:e6}");
        TestHarness.Check("Mx/My/Mxy ≈ 0 (чисто мембранное поле)",
            Math.Abs(avg.Mx) < 1.0 && Math.Abs(avg.My) < 1.0 && Math.Abs(avg.Mxy) < 1.0,
            $"Mx={avg.Mx:e3}, My={avg.My:e3}, Mxy={avg.Mxy:e3}");
    }

    static void RotatedElementConnectivity_SameAnalyticalAxialField_MatchesUnrotatedMesh()
    {
        // Та же геометрия/поле, что выше, но порядок узлов второго треугольника развёрнут —
        // ShellGeometry.LocalFrame(coords) даст другой LocalX для этого элемента (первое ребро
        // другое), если бы постпроцессор ошибочно использовал per-element frame вместо общего
        // patchBasis. С правильной (shared-frame) реализацией результат должен совпасть с первым
        // тестом с точностью до допуска.
        const double e = 30e9, nu = 0.0, h = 0.2, epsX = 1e-4;
        var laminate = LinearDirichletSystemTests.IsotropicLaminate(e, nu, h);
        double[][] nodes = [[0, 0, 0], [1, 0, 0], [1, 1, 0], [0, 1, 0]];
        int[][] elements = [[0, 1, 2], [2, 3, 0]]; // тот же треугольник, обход начат с другой вершины
        var mesh = new ShellMesh(nodes, elements, laminate);

        var u = new double[mesh.NDof];
        for (int n = 0; n < nodes.Length; n++)
            u[6 * n + 0] = epsX * nodes[n][0];

        double[,] patchBasis = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        var results = ShellMeshPatchPostprocessor.SectionResultantsAt(mesh, u, [0, 0, 0], patchBasis);
        var avg = ShellMeshPatchPostprocessor.Average(results);

        double expectedNx = e * epsX * h;
        TestHarness.Check("Nx не зависит от порядка обхода узлов элемента (shared frame, не per-element)",
            Math.Abs(avg.Nx - expectedNx) < 0.02 * Math.Abs(expectedNx),
            $"Nx={avg.Nx:e6}, ожидание={expectedNx:e6}");
    }
}
