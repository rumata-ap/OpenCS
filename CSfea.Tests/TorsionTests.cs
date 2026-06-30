using CSfea.Torsion;
using CScore;

namespace CSfea.Tests;

public static class TorsionTests
{
    public static void SmokePropsConstruction()
    {
        TestHarness.Section("TorsionProps: конструктор и TauMax");
        var props = new TorsionProps { It = 1.5, TauUnitMax = 2.0 };
        TestHarness.CheckRel("It", props.It, 1.5, 1e-9);
        // τ_max = G·Θ·τ_unit = 1000·0.01·2.0 = 20
        TestHarness.CheckRel("TauMax", props.TauMax(1000.0, 0.01), 20.0, 1e-9);
    }

    public static void BoundaryFromMaterialArea()
    {
        TestHarness.Section("TorsionBoundary: из MaterialArea с отверстием");
        // Внешний контур 10×10 (CCW), отверстие 2×2 по центру (CW)
        var area = new MaterialArea();
        var hull = new Contour(
            new[] { new StressPoint(0.0, 0.0), new StressPoint(10.0, 0.0),
                    new StressPoint(10.0, 10.0), new StressPoint(0.0, 10.0) }, "hull");
        hull.Type = ContourType.Hull;
        area.Contours.Add(hull);
        var hole = new Contour(
            new[] { new StressPoint(4.0, 4.0), new StressPoint(6.0, 4.0),
                    new StressPoint(6.0, 6.0), new StressPoint(4.0, 6.0) }, "hole");
        hole.Type = ContourType.Hole;
        area.Contours.Add(hole);

        var b = area.FromMaterialArea();
        TestHarness.Check("Outer не null", b.OuterX != null && b.OuterX.Length == 4);
        TestHarness.Check("Holes есть", b.Holes != null && b.Holes.Count == 1);
        TestHarness.Check("Hole[0] размер", b.Holes![0].X.Length == 4);
    }

    public static void PrandtlTri3ElementMatrices()
    {
        TestHarness.Section("PrandtlTri3: матрицы K и Load прямоугольного tri");
        // Прямоугольный треугольник с катетами вдоль осей: (0,0),(2,0),(0,3). Площадь A=3.
        double[] coords = { 0, 0, 2, 0, 0, 3 };
        double[,] k = PrandtlTri3.ElementK(coords);
        // Сумма всех элементов K для скалярного Лапласа на T3 равна 0 (постоянный режим → нулевой поток)
        double kSum = 0;
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) kSum += k[i, j];
        TestHarness.Check("K sum == 0", Math.Abs(kSum) < 1e-12, $"kSum={kSum}");
        // K симметрична и положительно полуопределена: диагональ > 0
        TestHarness.Check("K diag > 0", k[0, 0] > 0 && k[1, 1] > 0 && k[2, 2] > 0);
        // Load: F_i = -2·(A/3) = -2·(3/3) = -2 для каждого
        double[] f = PrandtlTri3.LoadVector(coords);
        TestHarness.CheckRel("F_i = -2", f[0], -2.0, 1e-9);
        TestHarness.CheckRel("F_1 = -2", f[1], -2.0, 1e-9);
        // Mass integral: ∫N_i dA = A/3 = 1
        double[] m = PrandtlTri3.MassVector(coords);
        TestHarness.CheckRel("M_i = A/3 = 1", m[0], 1.0, 1e-9);
    }
}

