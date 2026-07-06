using CSfea.Sparse;

namespace CSfea.Torsion;

/// <summary>Сборка глобальной системы МКЭ для функции Прандтля: K·φ = F. Работает и с T3, и с T6 сеткой.</summary>
public static class PrandtlAssembler
{
    /// <summary>Собирает глобальную матрицу K (CooMatrix) и вектор F.</summary>
    public static (CooMatrix K, double[] F) Assemble(TorsionMesh mesh)
    {
        int ndof = mesh.NodesX.Length;
        int nodesPerElement = mesh.Triangles.Length > 0 ? mesh.Triangles[0].Length : 3;
        var coo = new CooMatrix(ndof, ndof, mesh.Triangles.Length * nodesPerElement * nodesPerElement);
        var f = new double[ndof];
        foreach (var el in mesh.Triangles)
        {
            double[] c = BuildCoords(mesh, el);
            double[,] ke;
            double[] fe;
            if (el.Length == 6)
            {
                ke = PrandtlTri6.ElementK(c);
                fe = PrandtlTri6.LoadVector(c);
            }
            else
            {
                ke = PrandtlTri3.ElementK(c);
                fe = PrandtlTri3.LoadVector(c);
            }
            coo.AddBlock(el, ke);
            for (int k = 0; k < el.Length; k++) f[el[k]] += fe[k];
        }
        return (coo, f);
    }

    /// <summary>Координаты узлов элемента [x0,y0, x1,y1, ...] в порядке el.</summary>
    static double[] BuildCoords(TorsionMesh mesh, int[] el)
    {
        var c = new double[el.Length * 2];
        for (int k = 0; k < el.Length; k++)
        {
            c[2 * k] = mesh.NodesX[el[k]];
            c[2 * k + 1] = mesh.NodesY[el[k]];
        }
        return c;
    }
}
