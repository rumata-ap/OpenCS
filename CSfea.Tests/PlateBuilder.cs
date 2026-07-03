using CSfea.Core;

namespace CSfea.Tests;

/// <summary>Утилиты построения регулярной пластины N×N из элементов Shell4.</summary>
public static class PlateBuilder
{
    /// <summary>Регулярная сетка квадрата L×L, N×N элементов Shell4.</summary>
    public static (double[][] Nodes, int[][] Elements, Func<int, int, int> Ni)
        Build(int n, double l)
    {
        int Ni(int i, int j) => j * (n + 1) + i;
        var nodes = new double[(n + 1) * (n + 1)][];
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
                nodes[Ni(i, j)] = new[] { i * l / n, j * l / n, 0.0 };

        var elements = new int[n * n][];
        int e = 0;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                elements[e++] = new[] { Ni(i, j), Ni(i + 1, j), Ni(i + 1, j + 1), Ni(i, j + 1) };
        return (nodes, elements, Ni);
    }

    /// <summary>Изотропный материал (E, ν) под плоское напряжённое состояние.</summary>
    public static OrthotropicMaterial Isotropic(double e, double nu)
        => new(e, e, nu, e / (2.0 * (1.0 + nu)));

    /// <summary>Однослойная изотропная оболочка толщиной h.</summary>
    public static Laminate Plate(double e, double nu, double h)
        => new(new[] { new Ply(Isotropic(e, nu), 0.0, h) });

    /// <summary>DOF защемления контура (все 6 компонент) ∪ все drilling θz.</summary>
    public static int[] ClampedBoundary(ShellMesh mesh, double l, double tol = 1e-9)
    {
        var boundary = ShellMesh.DofsOnBoundary(mesh.Nodes, p =>
            Math.Abs(p[0]) < tol || Math.Abs(p[0] - l) < tol ||
            Math.Abs(p[1]) < tol || Math.Abs(p[1] - l) < tol);
        return ShellMesh.UnionDofs(boundary, ShellMesh.FixAllDrilling(mesh));
    }
}
