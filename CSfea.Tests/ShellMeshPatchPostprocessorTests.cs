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
        DistortedQ4_WeightSum_MatchesShoelaceArea();
        HeterogeneousMaterial_DifferentElementAreas_WeightedAverageMatchesAnalytical();
    }

    static double ShoelaceArea(double[][] nodesXy)
    {
        double area2 = 0;
        for (int i = 0; i < nodesXy.Length; i++)
        {
            var p0 = nodesXy[i];
            var p1 = nodesXy[(i + 1) % nodesXy.Length];
            area2 += p0[0] * p1[1] - p1[0] * p0[1];
        }
        return Math.Abs(area2) / 2.0;
    }

    static void DistortedQ4_WeightSum_MatchesShoelaceArea()
    {
        const double e = 30e9, nu = 0.0, h = 0.2, epsX = 1e-4;
        var laminate = LinearDirichletSystemTests.IsotropicLaminate(e, nu, h);
        // Заметно искажённый (не параллелограмм) Q4.
        double[][] nodes = [[0, 0, 0], [1, 0, 0], [1.3, 1, 0], [0, 0.7, 0]];
        int[][] elements = [[0, 1, 2, 3]];
        var mesh = new ShellMesh(nodes, elements, laminate);

        var u = new double[mesh.NDof];
        for (int n = 0; n < nodes.Length; n++)
            u[6 * n + 0] = epsX * nodes[n][0];

        double[,] patchBasis = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        var points = ShellMeshPatchPostprocessor.SectionResultantsAt(mesh, u, [0, 0, 0], patchBasis);

        double expectedArea = ShoelaceArea(nodes);
        double weightSum = points.Sum(p => p.Weight);

        TestHarness.Check("Сумма detJ-весов точек интегрирования равна реальной площади искажённого Q4",
            Math.Abs(weightSum - expectedArea) < 1e-6 * expectedArea,
            $"weightSum={weightSum:e6}, expectedArea={expectedArea:e6}");

        double expectedNx = e * epsX * h;
        var avg = ShellMeshPatchPostprocessor.Average(points);
        TestHarness.Check("Среднее Nx совпадает с аналитикой (детерминированность результата, не зависит от весов на однородном линейном поле)",
            Math.Abs(avg.Nx - expectedNx) < 0.02 * Math.Abs(expectedNx),
            $"Nx={avg.Nx:e6}, ожидание={expectedNx:e6}");
    }

    static void HeterogeneousMaterial_DifferentElementAreas_WeightedAverageMatchesAnalytical()
    {
        const double e1 = 30e9, e2 = 15e9, nu = 0.0, h = 0.2, epsX = 1e-4;
        var lam1 = LinearDirichletSystemTests.IsotropicLaminate(e1, nu, h);
        var lam2 = LinearDirichletSystemTests.IsotropicLaminate(e2, nu, h);
        // Два T3 разной площади: элемент 0 — маленький (у оси Y), элемент 1 — большой.
        double[][] nodes = [[0, 0, 0], [0.2, 0, 0], [0, 1, 0], [3, 0, 0], [3, 1, 0]];
        int[][] elements = [[0, 1, 2], [1, 3, 4]];
        var mesh = new ShellMesh(nodes, elements, new[] { lam1, lam2 });

        var u = new double[mesh.NDof];
        for (int n = 0; n < nodes.Length; n++)
            u[6 * n + 0] = epsX * nodes[n][0];

        double[,] patchBasis = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        var points = ShellMeshPatchPostprocessor.SectionResultantsAt(mesh, u, [0, 0, 0], patchBasis);
        var avg = ShellMeshPatchPostprocessor.Average(points);

        double area1 = ShoelaceArea([nodes[0], nodes[1], nodes[2]]);
        double area2 = ShoelaceArea([nodes[1], nodes[3], nodes[4]]);
        double nx1 = e1 * epsX * h, nx2 = e2 * epsX * h;
        double expectedWeighted = (nx1 * area1 + nx2 * area2) / (area1 + area2);
        double naiveUnweighted = points.Average(p => p.Nx); // по 1 точке на T3 — эквивалент (nx1+nx2)/2.

        TestHarness.Check("Взвешенное среднее Nx совпадает с аналитическим area-weighted значением",
            Math.Abs(avg.Nx - expectedWeighted) < 0.02 * Math.Abs(expectedWeighted),
            $"avg.Nx={avg.Nx:e6}, expectedWeighted={expectedWeighted:e6}");
        TestHarness.Check("Взвешенное и наивное (невзвешенное) среднее реально различаются на элементах разной площади — доказывает, что Average() использует Weight, а не игнорирует его",
            Math.Abs(avg.Nx - naiveUnweighted) > 0.05 * Math.Abs(expectedWeighted),
            $"weighted={avg.Nx:e6}, naive={naiveUnweighted:e6}");

        foreach (var p in points)
            TestHarness.Check("Вес точки положителен и конечен",
                double.IsFinite(p.Weight) && p.Weight > 0, $"Weight={p.Weight:e6}");
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
