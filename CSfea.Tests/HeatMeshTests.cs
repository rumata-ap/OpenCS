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
        HeatMesh_PatchTest_LinearField_Interior();
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

    private static void HeatMesh_PatchTest_LinearField_Interior()
    {
        var mesh = CreateStructuredMesh(4, 4);
        var mat = new ConstantHeatMaterial(lambda: 1.0, rhocp: 1.0);
        var k = mesh.AssembleConductivity(mat).ToCsc();
        var t = new double[mesh.NNodes];
        for (int i = 0; i < mesh.NNodes; i++)
            t[i] = 10.0 + 3.0 * mesh.X[i] + 4.0 * mesh.Y[i];

        double max = MaxInteriorResidual(mesh, k, t);
        TestHarness.Check("HeatMesh_PatchTest_LinearField_Interior", max < 1e-6, $"max|K·T|_int={max:E3}");
    }

    static double MaxInteriorResidual(HeatMesh mesh, CscMatrix k, double[] t)
    {
        double max = 0.0;
        const double tol = 1e-12;
        double xmin = mesh.X.Min(), xmax = mesh.X.Max();
        double ymin = mesh.Y.Min(), ymax = mesh.Y.Max();
        for (int i = 0; i < mesh.NNodes; i++)
        {
            double x = mesh.X[i], y = mesh.Y[i];
            if (Math.Abs(x - xmin) < tol || Math.Abs(x - xmax) < tol
                || Math.Abs(y - ymin) < tol || Math.Abs(y - ymax) < tol)
                continue;
            double s = 0.0;
            for (int p = k.ColPtr[i]; p < k.ColPtr[i + 1]; p++)
                s += k.Values[p] * t[k.RowIdx[p]];
            max = Math.Max(max, Math.Abs(s));
        }
        return max;
    }

    private static HeatMesh CreateStructuredMesh(int nx, int ny)
    {
        var x = new double[(nx + 1) * (ny + 1)];
        var y = new double[x.Length];
        int Node(int i, int j) => j * (nx + 1) + i;
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
            {
                int id = Node(i, j);
                x[id] = (double)i / nx;
                y[id] = (double)j / ny;
            }

        var elements = new List<int[]>();
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int n0 = Node(i, j);
                int n1 = Node(i + 1, j);
                int n2 = Node(i, j + 1);
                int n3 = Node(i + 1, j + 1);
                elements.Add([n0, n1, n3]);
                elements.Add([n0, n3, n2]);
            }
        return new HeatMesh(x, y, elements.ToArray());
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
