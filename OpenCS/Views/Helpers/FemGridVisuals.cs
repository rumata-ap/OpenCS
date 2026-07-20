using System.Windows.Media.Media3D;

namespace OpenCS.Views.Helpers;

/// <summary>Управляет видимостью всех слоёв расчётной сетки в 3D-виде FEM.</summary>
public static class FemGridVisuals
{
    /// <summary>Удаляет сеточные слои из коллекции и добавляет доступные, если сетка включена.</summary>
    public static void Apply(
        ICollection<Visual3D> children,
        bool showGrid,
        Visual3D? shellEdges,
        Visual3D? mesh,
        Visual3D? meshNodes)
    {
        ArgumentNullException.ThrowIfNull(children);

        Visual3D?[] layers = [shellEdges, mesh, meshNodes];
        foreach (var layer in layers)
        {
            if (layer == null) continue;
            while (children.Remove(layer)) { }
        }

        if (!showGrid) return;

        foreach (var layer in layers)
        {
            if (layer != null) children.Add(layer);
        }
    }
}
