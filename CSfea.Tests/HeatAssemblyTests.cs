using CSfea.Sparse;
using CSfea.Thermal;
using CSfea.Thermal.Bc;
using CSfea.Thermal.Materials;

namespace CSfea.Tests;

/// <summary>Эквивалентность сборки по постоянному паттерну и COO-сборки.</summary>
public static class HeatAssemblyTests
{
    public static void RunAll()
    {
        TestHarness.Section("HeatAssembly: эквивалентность COO");
        Assembly_K_MatchesCoo();
        Assembly_C_MatchesCoo();
        Assembly_T6_K_MatchesCoo();
    }

    static HeatMesh Grid(int nx, int ny)
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
        var els = new List<int[]>();
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int n0 = Node(i, j), n1 = Node(i + 1, j), n2 = Node(i, j + 1), n3 = Node(i + 1, j + 1);
                els.Add([n0, n1, n3]);
                els.Add([n0, n3, n2]);
            }
        return new HeatMesh(x, y, els.ToArray());
    }

    static double MaxDiff(CscMatrix a, CscMatrix b)
    {
        var da = a.ToDense();
        var db = b.ToDense();
        double m = 0.0;
        for (int i = 0; i < a.Rows; i++)
            for (int j = 0; j < a.Cols; j++)
                m = Math.Max(m, Math.Abs(da[i, j] - db[i, j]));
        return m;
    }

    static void Assembly_K_MatchesCoo()
    {
        var mesh = Grid(3, 3);
        var mat = new ConstantHeatMaterial(1.6, 2.4e6);
        var nodalT = new double[mesh.NNodes];
        Array.Fill(nodalT, 20.0);

        var cooK = mesh.AssembleConductivity(mat, nodalT).ToCsc();

        var asm = HeatAssembly.Build(mesh, Array.Empty<HeatBoundaryEdge>());
        var values = new double[asm.ColPtr[mesh.NNodes]];
        asm.AssembleK(mat, nodalT, values);
        var asmK = asm.ToCsc(values);

        double d = MaxDiff(cooK, asmK);
        TestHarness.Check("Assembly_K_MatchesCoo", d < 1e-12, $"maxDiff={d:E3}");
    }

    static void Assembly_C_MatchesCoo()
    {
        var mesh = Grid(3, 3);
        var mat = new ConstantHeatMaterial(1.6, 2.4e6);
        var nodalT = new double[mesh.NNodes];
        Array.Fill(nodalT, 20.0);

        var cooC = mesh.AssembleCapacity(mat, nodalT).ToCsc();

        var asm = HeatAssembly.Build(mesh, Array.Empty<HeatBoundaryEdge>());
        var values = new double[asm.ColPtr[mesh.NNodes]];
        asm.AssembleC(mat, nodalT, values);
        var asmC = asm.ToCsc(values);

        double d = MaxDiff(cooC, asmC);
        TestHarness.Check("Assembly_C_MatchesCoo", d < 1e-12, $"maxDiff={d:E3}");
    }

    static void Assembly_T6_K_MatchesCoo()
    {
        var linear = Grid(3, 3);
        var mesh = HeatMeshQuadratic.Promote(linear);
        var mat = new ConstantHeatMaterial(1.6, 2.4e6);
        var nodalT = new double[mesh.NNodes];
        Array.Fill(nodalT, 20.0);

        var cooK = mesh.AssembleConductivity(mat, nodalT).ToCsc();

        var asm = HeatAssembly.Build(mesh, Array.Empty<HeatBoundaryEdge>());
        var values = new double[asm.ColPtr[mesh.NNodes]];
        asm.AssembleK(mat, nodalT, values);
        var asmK = asm.ToCsc(values);

        double d = MaxDiff(cooK, asmK);
        TestHarness.Check("Assembly_T6_K_MatchesCoo", d < 1e-12, $"maxDiff={d:E3}");
    }
}
