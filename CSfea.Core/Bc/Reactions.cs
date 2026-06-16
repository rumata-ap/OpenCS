using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>Постпроцессорный вычислитель опорных реакций.
/// Порт <c>boundary_conditions.py: compute_reactions</c>.</summary>
public static class Reactions
{
    /// <summary>
    /// Вектор реакций (ndof). Для закреплённых DOF:
    /// R = (K_mesh + K_spring)·u + F_nl. Для DOF с нелинейными пружинами
    /// вне Дирихле: R = −(K_spring·u + F_nl).
    /// </summary>
    public static double[] Compute(IFeaMesh mesh, double[] u, BoundaryConditions? bc = null)
    {
        bc ??= new BoundaryConditions(mesh);
        int ndof = mesh.NDof;

        var kSpring = bc.AssembleKSpring();
        var fNl = bc.AssembleFSpringNonlinear(u);
        var kMesh = mesh.AssembleK();

        // K_total = K_mesh + K_spring
        var total = new CooMatrix(ndof, ndof, kMesh.Count + kSpring.Count);
        AppendCoo(total, kMesh);
        AppendCoo(total, kSpring);
        var kTotalU = total.ToCsc().Multiply(u);
        var kSpringU = kSpring.ToCsc().Multiply(u);

        var r = new double[ndof];
        var fd = bc.FixedDofs;
        var fdSet = new HashSet<int>(fd);
        foreach (int g in fd)
            r[g] = kTotalU[g] + fNl[g];

        foreach (var (node, dof) in bc.NonlinearNodeDofs())
        {
            int g = node * mesh.DofsPerNode + dof;
            if (!fdSet.Contains(g))
                r[g] = -(kSpringU[g] + fNl[g]);
        }
        return r;
    }

    private static void AppendCoo(CooMatrix target, CooMatrix src)
    {
        var csc = src.ToCsc();
        for (int c = 0; c < csc.Cols; c++)
            for (int p = csc.ColPtr[c]; p < csc.ColPtr[c + 1]; p++)
                target.Add(csc.RowIdx[p], c, csc.Values[p]);
    }
}
