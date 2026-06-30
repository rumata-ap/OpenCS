using CSfea.Sparse;

namespace CSfea.Torsion;

/// <summary>Сборка глобальной системы МКЭ для функции Прандтля: K·φ = F.</summary>
public static class PrandtlAssembler
{
    /// <summary>Собирает глобальную матрицу K (CooMatrix) и вектор F.</summary>
    public static (CooMatrix K, double[] F) Assemble(TorsionMesh mesh)
    {
        int ndof = mesh.NodesX.Length;
        var coo = new CooMatrix(ndof, ndof, mesh.Triangles.Length * 9);
        var f = new double[ndof];
        foreach (var el in mesh.Triangles)
        {
            double[] c =
            {
                mesh.NodesX[el[0]], mesh.NodesY[el[0]],
                mesh.NodesX[el[1]], mesh.NodesY[el[1]],
                mesh.NodesX[el[2]], mesh.NodesY[el[2]]
            };
            double[,] ke = PrandtlTri3.ElementK(c);
            coo.AddBlock(el, ke);
            double[] fe = PrandtlTri3.LoadVector(c);
            f[el[0]] += fe[0]; f[el[1]] += fe[1]; f[el[2]] += fe[2];
        }
        return (coo, f);
    }
}
