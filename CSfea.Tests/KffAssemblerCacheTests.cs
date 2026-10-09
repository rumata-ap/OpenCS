using CSfea.Core;
using Xunit;

namespace CSfea.Tests;

/// <summary>
/// Кэш вклада КЭ с постоянной матрицей в сборке K_ff: после смены секущих и замены записи КЭ решение на той же
/// сетке совпадает с решением на свежей сетке (без кэша). Стойка на жёсткой связи — ветка КЭ с ведомыми DOF.
/// </summary>
public class KffAssemblerCacheTests
{
    const int N = 4;
    const double L = 3.0;

    static readonly ShellTangent Elastic = new LinearLaminateResponse(
        new Laminate([new Ply(new OrthotropicMaterial(30e9, 30e9, 0.2, 12.5e9), 0.0, 0.2)])).Tangent([0, 0, 0], [0, 0, 0], [0, 0]);

    [Fact]
    public void CachedAssemblyMatchesFreshMesh()
    {
        var (nodes, quads, edge, top, bottom, center) = Model();
        var secant = new SecantShellResponse[quads.Count];
        var shells = new List<StructuralShell>();
        for (int e = 0; e < quads.Count; e++)
        {
            IShellSectionResponse s;
            if (e % 2 == 0) s = secant[e] = new SecantShellResponse(Elastic);
            else s = new LinearLaminateResponse(new Laminate([new Ply(new OrthotropicMaterial(30e9, 30e9, 0.2, 12.5e9), 0.0, 0.2)]));
            shells.Add(new StructuralShell(quads[e], e % 3 == 0 ? new RotatedShellResponse(s, 0.3) : s));
        }
        var beams = new List<StructuralBeam> { new(top, bottom, new LinearBeamResponse(new BeamSection(30e9, 0.09, 6.75e-4, 6.75e-4, 1.1e-3, 12.5e9))) };
        var links = new[] { new RigidLink(center, top) };
        var mesh = new StructuralMesh(nodes, shells, beams, links);
        var f = Load(mesh);

        var u1 = mesh.SolveLinear(f, Bc(mesh, edge, bottom));
        Assert.True(Diff(u1, Fresh(nodes, shells, beams, links, edge, bottom, f)) < 1e-12);

        // Секущие изменились: переменные КЭ пересобираются, постоянные — из кэша.
        for (int e = 0; e < secant.Length; e++)
            if (secant[e] is { } s)
                s.Update(new ShellTangent(Scale(Elastic.A, 0.7), Elastic.B, Scale(Elastic.D, 0.3 + 0.05 * e), Elastic.As));
        var bc = Bc(mesh, edge, bottom);
        var u2 = mesh.SolveLinear(f, bc);
        Assert.True(Diff(u2, u1) > 1e-3);
        Assert.True(Diff(u2, Fresh(nodes, shells, beams, links, edge, bottom, f)) < 1e-12);

        // Запись постоянного КЭ заменена (другая жёсткость) — кэш пересобирается.
        shells[1] = new StructuralShell(quads[1], new LinearLaminateResponse(
            new Laminate([new Ply(new OrthotropicMaterial(3e9, 3e9, 0.2, 1.25e9), 0.0, 0.2)])));
        var u3 = mesh.SolveLinear(f, bc);
        Assert.True(Diff(u3, u2) > 1e-6);
        Assert.True(Diff(u3, Fresh(nodes, shells, beams, links, edge, bottom, f)) < 1e-12);
    }

    static double[] Fresh(double[][] nodes, List<StructuralShell> shells, List<StructuralBeam> beams, RigidLink[] links,
        List<int> edge, int bottom, double[] f)
    {
        var mesh = new StructuralMesh(nodes, shells.ToList(), beams.ToList(), links);
        return mesh.SolveLinear(f, Bc(mesh, edge, bottom));
    }

    static BoundaryConditions Bc(StructuralMesh mesh, List<int> edge, int bottom)
    {
        var bc = new BoundaryConditions(mesh);
        foreach (int n in edge) bc.Fix([n], [0, 1, 2]);
        bc.Fix([bottom], [0, 1, 2, 3, 4, 5]);
        return bc;
    }

    static double[] Load(StructuralMesh mesh)
    {
        var f = new double[mesh.NDof];
        for (int n = 0; n < (N + 1) * (N + 1); n++) f[6 * n + 2] = -1e3 * (1 + n % 3);
        return f;
    }

    // Пластина N × N в плоскости z = 0, над центром — узел стойки (жёсткая связь с центром), низ стойки защемлён.
    static (double[][] Nodes, List<int[]> Quads, List<int> Edge, int Top, int Bottom, int Center) Model()
    {
        var nodes = new List<double[]>();
        int Id(int i, int j) => i * (N + 1) + j;
        for (int i = 0; i <= N; i++)
            for (int j = 0; j <= N; j++) nodes.Add([L * i / N, L * j / N, 0.0]);
        var quads = new List<int[]>();
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++) quads.Add([Id(i, j), Id(i + 1, j), Id(i + 1, j + 1), Id(i, j + 1)]);
        var edge = new List<int>();
        for (int i = 0; i <= N; i++)
            for (int j = 0; j <= N; j++)
                if (i == 0 || j == 0 || i == N || j == N) edge.Add(Id(i, j));
        int center = Id(N / 2, N / 2);
        int top = nodes.Count;
        nodes.Add([L / 2, L / 2, -0.1]);
        int bottom = nodes.Count;
        nodes.Add([L / 2, L / 2, -3.0]);
        return (nodes.ToArray(), quads, edge, top, bottom, center);
    }

    static double[,] Scale(double[,] m, double k)
    {
        var r = (double[,])m.Clone();
        for (int i = 0; i < r.GetLength(0); i++)
            for (int j = 0; j < r.GetLength(1); j++) r[i, j] *= k;
        return r;
    }

    static double Diff(double[] a, double[] b)
        => a.Zip(b, (x, y) => Math.Abs(x - y)).Max() / Math.Max(b.Max(Math.Abs), 1e-300);
}
