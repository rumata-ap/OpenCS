using CScore.Planar;
using CSfea.Core;

namespace CSfea.CScoreBridge;

/// <summary>Переводит проверенный PlanarLoad mapping в полный вектор сил CSfea.</summary>
public static class PlanarLoadShellMeshAdapter
{
    public static double[] ToNodalForceVector(
        PlanarLoadMappingResult result,
        ShellMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(mesh);
        if (!result.IsCalculable)
            throw new InvalidOperationException("Нерасчётный PlanarLoad mapping нельзя передать в CSfea.");

        var vector = new double[mesh.NDof];
        foreach ((int nodeIndex, PlanarVector3 force) in result.NodalLoads)
        {
            if (nodeIndex < 0 || nodeIndex >= mesh.NNodes)
                throw new InvalidOperationException(
                    $"PlanarLoad mapping ссылается на snapshot node {nodeIndex}, отсутствующий в CSfea ShellMesh.");
            int dof = mesh.DofsPerNode * nodeIndex;
            vector[dof] += force.X;
            vector[dof + 1] += force.Y;
            vector[dof + 2] += force.Z;
        }
        return vector;
    }

    public static PlanarBoundarySet MapBoundarySet(
        PlanarBoundarySet set,
        ShellMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(mesh);
        foreach (int nodeIndex in set.NodeIndices)
            ValidateNode(nodeIndex, mesh);
        foreach ((int a, int b) in set.Edges)
        {
            ValidateNode(a, mesh);
            ValidateNode(b, mesh);
        }
        return set;
    }

    static void ValidateNode(int nodeIndex, ShellMesh mesh)
    {
        if (nodeIndex < 0 || nodeIndex >= mesh.NNodes)
            throw new InvalidOperationException(
                $"Boundary set ссылается на snapshot node {nodeIndex}, отсутствующий в CSfea ShellMesh.");
    }
}
