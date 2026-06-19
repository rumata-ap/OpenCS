using CSfea.Sparse;
using CSfea.Thermal;
using CSfea.Thermal.Elements;
using CSfea.Thermal.Materials;

namespace CSfea.Tests;

/// <summary>Тесты квадратичного T6-элемента теплопроводности HeatTri6.</summary>
public static class HeatTri6Tests
{
    static readonly double[] UnitTri6 =
    [
        0, 0, 1, 0, 0, 1,
        0.5, 0, 0.5, 0.5, 0, 0.5
    ];

    public static void RunAll()
    {
        TestHarness.Section("HeatTri6: матрицы элемента");
        HeatTri6_AreaMatchesTri3();
        HeatTri6_ConstantK_IsSymmetricPositiveDiagonal();
        HeatTri6_ConstantK_RowSumsNearZero();
        HeatTri6_ConstantM_TotalCapacityMatchesArea();
        HeatTri6_EnergyLinearField();
        HeatTri6_InterpolatesLinearField();
        HeatTri6_PatchTest_LinearField_Mesh();
        HeatTri6_ShapeFunctionsPartitionOfUnity();
    }

    static void HeatTri6_ShapeFunctionsPartitionOfUnity()
    {
        Span<double> n = stackalloc double[6];
        HeatTri6.ShapeFunctions(0.2, 0.3, n);
        double sum = 0;
        for (int i = 0; i < 6; i++) sum += n[i];
        TestHarness.CheckRel("HeatTri6_ShapeFunctionsPartitionOfUnity", sum, 1.0, 1e-12);
    }

    static void HeatTri6_AreaMatchesTri3()
    {
        double a6 = HeatTri6.AreaFromCorners(UnitTri6);
        double a3 = HeatTri3.Area(0, 0, 1, 0, 0, 1);
        TestHarness.CheckRel("HeatTri6_AreaMatchesTri3", a6, a3, 1e-12);
    }

    static void HeatTri6_ConstantK_IsSymmetricPositiveDiagonal()
    {
        var ke = HeatTri6.ElementK(1.6, UnitTri6);
        bool symmetric = true;
        bool positiveDiag = true;
        for (int i = 0; i < 6; i++)
        {
            if (ke[i, i] <= 0.0) positiveDiag = false;
            for (int j = i + 1; j < 6; j++)
                if (Math.Abs(ke[i, j] - ke[j, i]) > 1e-10) symmetric = false;
        }
        TestHarness.Check("HeatTri6_ConstantK_IsSymmetricPositiveDiagonal", symmetric && positiveDiag);
    }

    static void HeatTri6_ConstantK_RowSumsNearZero()
    {
        var ke = HeatTri6.ElementK(1.0, UnitTri6);
        bool ok = true;
        for (int i = 0; i < 6; i++)
        {
            double s = 0;
            for (int j = 0; j < 6; j++) s += ke[i, j];
            if (Math.Abs(s) > 1e-9) ok = false;
        }
        TestHarness.Check("HeatTri6_ConstantK_RowSumsNearZero", ok);
    }

    static void HeatTri6_ConstantM_TotalCapacityMatchesArea()
    {
        const double rhocp = 2.4e6;
        var me = HeatTri6.ElementM(rhocp, UnitTri6);
        double sum = 0;
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 6; j++)
                sum += me[i, j];
        double expected = rhocp * HeatTri6.AreaFromCorners(UnitTri6);
        TestHarness.CheckRel("HeatTri6_ConstantM_TotalCapacityMatchesArea", sum, expected, 1e-6);
    }

    /// <summary>
    /// Энергия λ∫|∇T|²dA для линейного поля — сильнее одноэлементного K·T.
    /// </summary>
    static void HeatTri6_EnergyLinearField()
    {
        var ke = HeatTri6.ElementK(1.0, UnitTri6);
        var t = new double[6];
        for (int i = 0; i < 6; i++)
            t[i] = 2.0 + 3.0 * UnitTri6[2 * i] + 4.0 * UnitTri6[2 * i + 1];
        double energy = 0.0;
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 6; j++)
                energy += 0.5 * t[i] * ke[i, j] * t[j];
        double expected = 0.5 * 25.0 * HeatTri6.AreaFromCorners(UnitTri6);
        TestHarness.CheckRel("HeatTri6_EnergyLinearField", energy, expected, 1e-6);
    }

    static void HeatTri6_InterpolatesLinearField()
    {
        var t = new double[6];
        for (int i = 0; i < 6; i++)
            t[i] = 2.0 + 3.0 * UnitTri6[2 * i] + 4.0 * UnitTri6[2 * i + 1];

        Span<double> n = stackalloc double[6];
        double maxErr = 0.0;
        foreach (var (l1, l2) in new (double, double)[] { (0.2, 0.3), (0.65, 0.2), (1.0 / 3.0, 1.0 / 3.0) })
        {
            HeatTri6.ShapeFunctions(l1, l2, n);
            double th = 0;
            for (int i = 0; i < 6; i++) th += n[i] * t[i];
            double x = l2;
            double y = 1.0 - l1 - l2;
            double exact = 2.0 + 3.0 * x + 4.0 * y;
            maxErr = Math.Max(maxErr, Math.Abs(th - exact));
        }
        TestHarness.Check("HeatTri6_InterpolatesLinearField", maxErr < 1e-12, $"maxErr={maxErr:E3}");
    }

    /// <summary>
    /// Patch-тест на сетке 4×4: K·T = 0 во внутренних узлах для T = a + bx + cy.
    /// </summary>
    static void HeatTri6_PatchTest_LinearField_Mesh()
    {
        var mesh = HeatMeshQuadratic.Promote(CreateStructuredMesh(4, 4));
        var mat = new ConstantHeatMaterial(1.0, 1.0);
        var k = mesh.AssembleConductivity(mat).ToCsc();
        var t = new double[mesh.NNodes];
        for (int i = 0; i < mesh.NNodes; i++)
            t[i] = 10.0 + 3.0 * mesh.X[i] + 4.0 * mesh.Y[i];

        double max = MaxResidualOnInterior(mesh, k, t);
        TestHarness.Check("HeatTri6_PatchTest_LinearField_Mesh", max < 1e-6, $"max|K·T|_int={max:E3}");
    }

    static double MaxResidualOnInterior(HeatMesh mesh, CscMatrix k, double[] t)
    {
        double max = 0.0;
        const double tol = 1e-12;
        for (int i = 0; i < mesh.NNodes; i++)
        {
            if (IsOnBoundary(mesh, i, tol))
                continue;
            double s = 0.0;
            for (int p = k.ColPtr[i]; p < k.ColPtr[i + 1]; p++)
                s += k.Values[p] * t[k.RowIdx[p]];
            max = Math.Max(max, Math.Abs(s));
        }
        return max;
    }

    static bool IsOnBoundary(HeatMesh mesh, int node, double tol)
    {
        double xmin = mesh.X.Min();
        double xmax = mesh.X.Max();
        double ymin = mesh.Y.Min();
        double ymax = mesh.Y.Max();
        double x = mesh.X[node];
        double y = mesh.Y[node];
        return Math.Abs(x - xmin) < tol || Math.Abs(x - xmax) < tol
            || Math.Abs(y - ymin) < tol || Math.Abs(y - ymax) < tol;
    }

    static HeatMesh CreateStructuredMesh(int nx, int ny)
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
}
