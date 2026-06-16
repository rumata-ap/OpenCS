using CSfea.Sparse;
using CSfea.Thermal;
using CSfea.Thermal.Materials;

namespace CSfea.Tests;

/// <summary>Тесты сборки глобальных матриц K и C на сетке HeatMesh.</summary>
public static class HeatMeshTests
{
    public static void RunAll()
    {
        TestHarness.Section("HeatMesh: сборка K и C на квадрате 2×2");
        HeatMesh_2x2Square_AssembleK_SizeAndNonzero();
        HeatMesh_2x2Square_AssembleK_Symmetric();
        HeatMesh_2x2Square_AssembleC_Symmetric();
        HeatMesh_2x2Square_AssembleC_PositiveDiagonal();
    }

    private static HeatMesh Create2x2SquareMesh()
    {
        double[] x = [0, 1, 0, 1];
        double[] y = [0, 0, 1, 1];
        int[][] elements = [[0, 1, 3], [0, 3, 2]];
        return new HeatMesh(x, y, elements);
    }

    private static void HeatMesh_2x2Square_AssembleK_SizeAndNonzero()
    {
        var mesh = Create2x2SquareMesh();
        var mat = new ConstantHeatMaterial(lambda: 1.0, rhocp: 1.0);
        var k = mesh.AssembleConductivity(mat);

        bool ok = k.Rows == mesh.NNodes
                  && k.Cols == mesh.NNodes
                  && k.ToCsc().Nnz > 0;
        TestHarness.Check("HeatMesh_2x2Square_AssembleK_SizeAndNonzero", ok,
            $"size={k.Rows}×{k.Cols}, nnz={k.ToCsc().Nnz}");
    }

    private static void HeatMesh_2x2Square_AssembleK_Symmetric()
    {
        var mesh = Create2x2SquareMesh();
        var mat = new ConstantHeatMaterial(lambda: 1.0, rhocp: 1.0);
        var k = mesh.AssembleConductivity(mat);

        TestHarness.Check("HeatMesh_2x2Square_AssembleK_Symmetric",
            IsSymmetric(k), "K симметрична");
    }

    private static void HeatMesh_2x2Square_AssembleC_Symmetric()
    {
        var mesh = Create2x2SquareMesh();
        var mat = new ConstantHeatMaterial(lambda: 1.0, rhocp: 1.0);
        var nodalT = new double[mesh.NNodes];
        var c = mesh.AssembleCapacity(mat, nodalT);

        TestHarness.Check("HeatMesh_2x2Square_AssembleC_Symmetric",
            IsSymmetric(c), "C симметрична");
    }

    private static void HeatMesh_2x2Square_AssembleC_PositiveDiagonal()
    {
        var mesh = Create2x2SquareMesh();
        var mat = new ConstantHeatMaterial(lambda: 1.0, rhocp: 1.0);
        var nodalT = new double[mesh.NNodes];
        var c = mesh.AssembleCapacity(mat, nodalT);
        var csc = c.ToCsc();

        bool ok = true;
        for (int i = 0; i < mesh.NNodes; i++)
        {
            double d = GetEntry(csc, i, i);
            if (d <= 0.0)
                ok = false;
        }
        TestHarness.Check("HeatMesh_2x2Square_AssembleC_PositiveDiagonal", ok,
            "диагональ C > 0");
    }

    private static bool IsSymmetric(CooMatrix coo, double tol = 1e-12)
    {
        var csc = coo.ToCsc();
        int n = csc.Rows;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (Math.Abs(GetEntry(csc, i, j) - GetEntry(csc, j, i)) > tol)
                    return false;
        return true;
    }

    private static double GetEntry(CscMatrix m, int row, int col)
    {
        for (int p = m.ColPtr[col]; p < m.ColPtr[col + 1]; p++)
            if (m.RowIdx[p] == row)
                return m.Values[p];
        return 0.0;
    }
}
