using CScore.Fem;
using CScore.Planar;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Переводит проверенный Planar boundary mapping в shell OpenSees model.</summary>
public static class PlanarBoundaryActionOpenSeesAdapter
{
    /// <summary>Добавляет force/sp actions в выбранную стадию без nearest-node mapping.</summary>
    public static PlanarBoundaryOpenSeesResult Apply(
        ShellOpenSeesModel model,
        PlanarBoundaryActionMeshMappingResult mapping,
        IReadOnlyDictionary<int, int> snapshotNodeToOpenSeesTag,
        int stageIndex)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(snapshotNodeToOpenSeesTag);

        var diagnostics = new List<FemValidationDiagnostic>(mapping.Diagnostics);
        if (!mapping.IsCalculable)
            return new() { Diagnostics = diagnostics, SourceMapping = mapping, SnapshotNodeToOpenSeesTag = snapshotNodeToOpenSeesTag };
        if (stageIndex < 0 || stageIndex >= model.Stages.Count)
        {
            diagnostics.Add(new("planar_boundary_opensees_stage_missing", $"Не найдена стадия OpenSees с индексом {stageIndex}."));
            return new() { Diagnostics = diagnostics, SourceMapping = mapping, SnapshotNodeToOpenSeesTag = snapshotNodeToOpenSeesTag };
        }

        var nodes = model.Nodes.ToDictionary(node => node.Tag);
        var updatedNodes = nodes.Values.ToDictionary(node => node.Tag, node => node);
        foreach ((int nodeIndex, int dof) in mapping.PreservedSupportDofs)
        {
            if (!TryResolveNode(nodeIndex, snapshotNodeToOpenSeesTag, nodes, diagnostics, out int tag)) continue;
            if (dof is < 0 or >= 6)
            {
                diagnostics.Add(new("planar_boundary_opensees_dof_invalid", $"Snapshot node {nodeIndex}: DOF {dof} вне диапазона 0..5."));
                continue;
            }
            bool[] fixedDofs = (bool[])updatedNodes[tag].Fixed.Clone();
            fixedDofs[dof] = true;
            updatedNodes[tag] = updatedNodes[tag] with { Fixed = fixedDofs };
        }

        var stage = model.Stages[stageIndex];
        var loads = stage.Loads.ToList();
        foreach (PlanarNodalAction action in mapping.NodalActions)
        {
            if (!TryResolveNode(action.NodeIndex, snapshotNodeToOpenSeesTag, nodes, diagnostics, out int tag)) continue;
            loads.Add(new ShellNodalLoad(
                tag,
                action.ForceGlobal.X,
                action.ForceGlobal.Y,
                action.ForceGlobal.Z,
                action.MomentGlobal.X,
                action.MomentGlobal.Y,
                action.MomentGlobal.Z));
        }

        var kinematic = stage.KinematicLoads.ToList();
        foreach (((int nodeIndex, int dof), double value) in mapping.PrescribedDofs)
        {
            if (!TryResolveNode(nodeIndex, snapshotNodeToOpenSeesTag, nodes, diagnostics, out int tag)) continue;
            if (dof is < 0 or >= 6)
            {
                diagnostics.Add(new("planar_boundary_opensees_dof_invalid", $"Snapshot node {nodeIndex}: DOF {dof} вне диапазона 0..5."));
                continue;
            }
            if (updatedNodes[tag].Fixed[dof])
            {
                diagnostics.Add(new("planar_boundary_opensees_fixed_kinematic_conflict", $"Узел {tag}, DOF {dof} одновременно fixed и prescribed."));
                continue;
            }
            if (mapping.PreservedSupportDofs.Contains((nodeIndex, dof)))
            {
                diagnostics.Add(new("planar_boundary_opensees_fixed_kinematic_conflict", $"Узел {tag}, DOF {dof} одновременно preserve_support и prescribed."));
                continue;
            }
            kinematic.Add(new ShellKinematicLoad(tag, dof + 1, value));
        }

        if (diagnostics.Any(diagnostic => diagnostic.IsError))
            return new() { Diagnostics = diagnostics, SourceMapping = mapping, SnapshotNodeToOpenSeesTag = snapshotNodeToOpenSeesTag };

        var stages = model.Stages.ToArray();
        stages[stageIndex] = new ShellNonlinearStage
        {
            Tag = stage.Tag,
            Loads = loads,
            KinematicLoads = kinematic,
            LoadFactorStep = stage.LoadFactorStep,
            MaxLoadFactor = stage.MaxLoadFactor
        };
        var mappedModel = model with
        {
            Nodes = updatedNodes.Values.OrderBy(node => node.Tag).ToArray(),
            Stages = stages
        };
        try { mappedModel.Validate(); }
        catch (InvalidOperationException ex)
        {
            diagnostics.Add(new("planar_boundary_opensees_model_invalid", ex.Message));
            return new() { Diagnostics = diagnostics, SourceMapping = mapping, SnapshotNodeToOpenSeesTag = snapshotNodeToOpenSeesTag };
        }

        return new()
        {
            Model = mappedModel,
            Diagnostics = diagnostics,
            SourceMapping = mapping,
            SnapshotNodeToOpenSeesTag = snapshotNodeToOpenSeesTag
        };
    }

    static bool TryResolveNode(
        int snapshotIndex,
        IReadOnlyDictionary<int, int> snapshotNodeToOpenSeesTag,
        IReadOnlyDictionary<int, NormalizedShellNode> nodes,
        ICollection<FemValidationDiagnostic> diagnostics,
        out int tag)
    {
        if (!snapshotNodeToOpenSeesTag.TryGetValue(snapshotIndex, out tag) || !nodes.ContainsKey(tag))
        {
            diagnostics.Add(new("planar_boundary_opensees_node_unknown", $"Snapshot node {snapshotIndex} не сопоставлен с OpenSees node tag."));
            tag = 0;
            return false;
        }
        return true;
    }
}
