using CScore.Fem;
using System.Windows.Media.Media3D;

namespace OpenCS.ViewModels;

/// <summary>Строит набор условных знаков FEM по данным узлов и результирующих нагрузок.</summary>
public static class FemDiagramGlyphFactory
{
    static readonly (int Mask, Vector3D Axis, string Component, FemDiagramGlyphKind Kind)[] Supports =
    [
        (1, new(1, 0, 0), "Tx", FemDiagramGlyphKind.TranslationSupport),
        (2, new(0, 1, 0), "Ty", FemDiagramGlyphKind.TranslationSupport),
        (4, new(0, 0, 1), "Tz", FemDiagramGlyphKind.TranslationSupport),
        (8, new(1, 0, 0), "Rx", FemDiagramGlyphKind.RotationSupport),
        (16, new(0, 1, 0), "Ry", FemDiagramGlyphKind.RotationSupport),
        (32, new(0, 0, 1), "Rz", FemDiagramGlyphKind.RotationSupport)
    ];

    static readonly (int Dof, Vector3D Axis, string Component, bool IsRotation)[] KinematicDofs =
    [
        (1, new(1, 0, 0), "Ux", false),
        (2, new(0, 1, 0), "Uy", false),
        (3, new(0, 0, 1), "Uz", false),
        (4, new(1, 0, 0), "Rx", true),
        (5, new(0, 1, 0), "Ry", true),
        (6, new(0, 0, 1), "Rz", true)
    ];

    /// <summary>Создаёт знаки двух независимо отключаемых слоёв: закреплений и нагрузок
    /// (силовых и кинематических — заданных перемещений/поворотов).</summary>
    public static IReadOnlyList<FemDiagramGlyph> Create(
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemResolvedNodeLoad> loads,
        IReadOnlyList<FemKinematicLoad> kinematicLoads,
        bool showSupports,
        bool showLoads)
    {
        var result = new List<FemDiagramGlyph>();
        if (showSupports)
        {
            foreach (var node in nodes)
                foreach (var support in Supports)
                    if ((node.DofMask & support.Mask) != 0)
                        result.Add(new FemDiagramGlyph(support.Kind, node.Id, support.Axis, 1,
                            support.Component, 0, true));
        }

        if (showLoads)
        {
            foreach (var load in loads)
            {
                AddLoad(result, load.NodeId, new Vector3D(1, 0, 0), "Fx", load.Fx, false);
                AddLoad(result, load.NodeId, new Vector3D(0, 1, 0), "Fy", load.Fy, false);
                AddLoad(result, load.NodeId, new Vector3D(0, 0, 1), "Fz", load.Fz, false);
                AddLoad(result, load.NodeId, new Vector3D(1, 0, 0), "Mx", load.Mx, true);
                AddLoad(result, load.NodeId, new Vector3D(0, 1, 0), "My", load.My, true);
                AddLoad(result, load.NodeId, new Vector3D(0, 0, 1), "Mz", load.Mz, true);
            }
            foreach (var kinematic in kinematicLoads)
                AddKinematic(result, kinematic);
        }

        return result;
    }

    static void AddLoad(List<FemDiagramGlyph> target, int nodeId, Vector3D axis, string component, double value, bool moment)
    {
        if (value == 0) return;
        target.Add(new FemDiagramGlyph(moment ? FemDiagramGlyphKind.Moment : FemDiagramGlyphKind.Force,
            nodeId, axis, Math.Sign(value), component, value, false));
    }

    static void AddKinematic(List<FemDiagramGlyph> target, FemKinematicLoad load)
    {
        if (load.Value == 0) return;
        var entry = KinematicDofs.FirstOrDefault(item => item.Dof == load.Dof);
        if (entry.Component == null) return;
        target.Add(new FemDiagramGlyph(
            entry.IsRotation ? FemDiagramGlyphKind.KinematicRotation : FemDiagramGlyphKind.KinematicDisplacement,
            load.NodeId, entry.Axis, Math.Sign(load.Value), entry.Component, load.Value, false));
    }
}
