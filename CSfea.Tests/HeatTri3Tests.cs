using CSfea.Thermal.Elements;

namespace CSfea.Tests;

/// <summary>Тесты CST-элемента теплопроводности HeatTri3.</summary>
public static class HeatTri3Tests
{
    private static readonly double[] UnitRightTriangle = [0, 0, 1, 0, 0, 1];

    public static void RunAll()
    {
        TestHarness.Section("HeatTri3: геометрия и матрицы элемента");
        HeatTri3_UnitRightTriangle_AreaIsHalf();
        HeatTri3_ConstantK_IsSymmetricPositiveDiagonal();
        HeatTri3_ConstantK_RowSumsNearZero();
    }

    private static void HeatTri3_UnitRightTriangle_AreaIsHalf()
    {
        double area = HeatTri3.Area(0, 0, 1, 0, 0, 1);
        TestHarness.CheckRel("HeatTri3_UnitRightTriangle_AreaIsHalf", area, 0.5, 1e-12);
    }

    private static void HeatTri3_ConstantK_IsSymmetricPositiveDiagonal()
    {
        const double lambda = 1.6;
        var ke = HeatTri3.ElementK(lambda, UnitRightTriangle);

        bool symmetric = true;
        bool positiveDiag = true;
        for (int i = 0; i < 3; i++)
        {
            if (ke[i, i] <= 0.0)
                positiveDiag = false;
            for (int j = i + 1; j < 3; j++)
            {
                if (Math.Abs(ke[i, j] - ke[j, i]) > 1e-12)
                    symmetric = false;
            }
        }

        TestHarness.Check("HeatTri3_ConstantK_IsSymmetricPositiveDiagonal",
            symmetric && positiveDiag,
            $"K00={ke[0, 0]:g6}, K11={ke[1, 1]:g6}, K22={ke[2, 2]:g6}");
    }

    private static void HeatTri3_ConstantK_RowSumsNearZero()
    {
        var ke = HeatTri3.ElementK(1.6, UnitRightTriangle);
        const double tol = 1e-12;
        bool ok = true;
        for (int i = 0; i < 3; i++)
        {
            double rowSum = ke[i, 0] + ke[i, 1] + ke[i, 2];
            if (Math.Abs(rowSum) > tol)
                ok = false;
        }
        TestHarness.Check("HeatTri3_ConstantK_RowSumsNearZero", ok, "суммы строк K ≈ 0");
    }
}
