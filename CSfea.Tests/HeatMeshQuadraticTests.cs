using CSfea.Sparse;
using CSfea.Thermal;
using CSfea.Thermal.Materials;
using CSfea.Thermal.Solvers;

namespace CSfea.Tests;

/// <summary>Тесты повышения T3-сетки до T6.</summary>
public static class HeatMeshQuadraticTests
{
    public static void RunAll()
    {
        TestHarness.Section("HeatMeshQuadratic: promote + сборка");
        Promote_SingleTriangle_HasSixNodes();
        Promote_SharedEdge_HasOneMidNode();
        QuadraticMesh_AssembleK_Symmetric();
        QuadraticTransient_UniformZeroFlux();
    }

    static void Promote_SingleTriangle_HasSixNodes()
    {
        var linear = new HeatMesh([0, 1, 0], [0, 0, 1], [[0, 1, 2]]);
        var quad = HeatMeshQuadratic.Promote(linear);
        TestHarness.Check("Promote_SingleTriangle_HasSixNodes",
            quad.NNodes == 6 && quad.IsQuadratic && quad.Elements[0].Length == 6);
    }

    static void Promote_SharedEdge_HasOneMidNode()
    {
        var linear = Create2x2SquareMesh();
        var quad = HeatMeshQuadratic.Promote(linear);
        int? mid = HeatMeshQuadratic.TryGetMidNode(linear, quad, 0, 1);
        TestHarness.Check("Promote_SharedEdge_HasOneMidNode", mid is int && mid >= linear.NNodes);
    }

    static void QuadraticMesh_AssembleK_Symmetric()
    {
        var quad = HeatMeshQuadratic.Promote(Create2x2SquareMesh());
        var mat = new ConstantHeatMaterial(1.0, 1.0);
        var k = quad.AssembleConductivity(mat);
        TestHarness.Check("QuadraticMesh_AssembleK_Symmetric", HeatMeshTestsHelper.IsSymmetric(k));
    }

    static void QuadraticTransient_UniformZeroFlux()
    {
        var mesh = HeatMeshQuadratic.Promote(CreateSingleTriangleMesh());
        var mat = new ConstantHeatMaterial(1.0, 1.0);
        var options = new TransientHeatOptions
        {
            Duration_s = 5.0,
            TimeStep_s = 1.0,
            SnapshotStep_s = 2.0,
            Theta = 1.0,
            PicardMaxIter = 5,
            PicardTolCelsius = 1e-9,
            TInitCelsius = 25.0,
            AdaptiveFirstMinute = false
        };
        var result = TransientHeatSolver.Solve(mesh, mat, options, []);
        bool ok = result.Snapshots.All(s => s.All(v => Math.Abs(v - 25.0) < 1e-6));
        TestHarness.Check("QuadraticTransient_UniformZeroFlux", ok);
    }

    static HeatMesh Create2x2SquareMesh()
    {
        double[] x = [0, 1, 0, 1];
        double[] y = [0, 0, 1, 1];
        int[][] elements = [[0, 1, 3], [0, 3, 2]];
        return new HeatMesh(x, y, elements);
    }

    static HeatMesh CreateSingleTriangleMesh()
        => new([0, 1, 0], [0, 0, 1], [[0, 1, 2]]);
}

internal static class HeatMeshTestsHelper
{
    internal static bool IsSymmetric(CooMatrix coo, double tol = 1e-10)
    {
        var csc = coo.ToCsc();
        int n = csc.Rows;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (Math.Abs(GetEntry(csc, i, j) - GetEntry(csc, j, i)) > tol)
                    return false;
        return true;
    }

    static double GetEntry(CscMatrix m, int row, int col)
    {
        for (int p = m.ColPtr[col]; p < m.ColPtr[col + 1]; p++)
            if (m.RowIdx[p] == row) return m.Values[p];
        return 0.0;
    }
}
