using CScore.Fem;
using CScore.Planar;
using CSfea.Core;

namespace CSfea.CScoreBridge;

/// <summary>Переводит проверенный Planar boundary mapping в nodal vector и Dirichlet input CSfea.</summary>
public static class PlanarBoundaryActionShellMeshAdapter
{
    public static PlanarBoundaryShellMeshResult Apply(
        ShellMesh mesh,
        PlanarBoundaryActionMeshMappingResult mapping)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(mapping);

        var diagnostics = new List<FemValidationDiagnostic>(mapping.Diagnostics);
        var forceVector = new double[mesh.NDof];
        var prescribed = new Dictionary<int, double>();

        if (mesh.DofsPerNode != 6)
            diagnostics.Add(new("planar_boundary_csfea_dof_count", "CSfea ShellMesh должен иметь ровно 6 DOF на узел."));

        if (!mapping.IsCalculable)
            diagnostics.Add(new("planar_boundary_csfea_mapping_invalid", "Нерасчётный Planar boundary mapping нельзя передать в CSfea."));

        foreach (var action in mapping.NodalActions)
        {
            if (!IsValidNode(action.NodeIndex, mesh))
            {
                diagnostics.Add(new("planar_boundary_csfea_node_unknown",
                    $"Nodal action ссылается на snapshot node {action.NodeIndex}, отсутствующий в CSfea ShellMesh."));
                continue;
            }

            int offset = mesh.DofsPerNode * action.NodeIndex;
            forceVector[offset] += action.ForceGlobal.X;
            forceVector[offset + 1] += action.ForceGlobal.Y;
            forceVector[offset + 2] += action.ForceGlobal.Z;
            forceVector[offset + 3] += action.MomentGlobal.X;
            forceVector[offset + 4] += action.MomentGlobal.Y;
            forceVector[offset + 5] += action.MomentGlobal.Z;
        }

        foreach (var (nodeIndex, dof) in mapping.PreservedSupportDofs)
        {
            if (!TryGetGlobalDof(nodeIndex, dof, mesh, diagnostics, out int globalDof)) continue;
            AddPrescribed(prescribed, globalDof, 0.0, nodeIndex, dof, diagnostics);
        }

        foreach (var ((nodeIndex, dof), value) in mapping.PrescribedDofs)
        {
            if (!TryGetGlobalDof(nodeIndex, dof, mesh, diagnostics, out int globalDof)) continue;
            AddPrescribed(prescribed, globalDof, value, nodeIndex, dof, diagnostics);
        }

        var fixedDofs = prescribed.Keys.OrderBy(dof => dof).ToArray();
        var uFixed = fixedDofs.Select(dof => prescribed[dof]).ToArray();
        return new()
        {
            NodalForceVector = forceVector,
            FixedDofs = fixedDofs,
            UFixed = uFixed,
            Diagnostics = diagnostics,
            SourceMapping = mapping
        };
    }

    static bool IsValidNode(int nodeIndex, ShellMesh mesh) =>
        nodeIndex >= 0 && nodeIndex < mesh.NNodes;

    static bool TryGetGlobalDof(
        int nodeIndex,
        int dof,
        ShellMesh mesh,
        ICollection<FemValidationDiagnostic> diagnostics,
        out int globalDof)
    {
        globalDof = 0;
        if (!IsValidNode(nodeIndex, mesh))
        {
            diagnostics.Add(new("planar_boundary_csfea_node_unknown",
                $"Boundary condition ссылается на snapshot node {nodeIndex}, отсутствующий в CSfea ShellMesh."));
            return false;
        }
        if (dof < 0 || dof >= 6)
        {
            diagnostics.Add(new("planar_boundary_csfea_dof_unknown",
                $"Boundary condition содержит недопустимый локальный DOF {dof}."));
            return false;
        }

        globalDof = nodeIndex * mesh.DofsPerNode + dof;
        return true;
    }

    static void AddPrescribed(
        IDictionary<int, double> prescribed,
        int globalDof,
        double value,
        int nodeIndex,
        int dof,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        if (prescribed.TryGetValue(globalDof, out double existing))
        {
            if (Math.Abs(existing - value) > 1e-12)
                diagnostics.Add(new("planar_boundary_csfea_dof_conflict",
                    $"DOF {dof} узла {nodeIndex} одновременно задан как fixed/prescribed с разными значениями."));
            return;
        }
        prescribed.Add(globalDof, value);
    }
}
