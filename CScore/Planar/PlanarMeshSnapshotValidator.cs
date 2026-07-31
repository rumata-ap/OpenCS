using CScore.Fem;

namespace CScore.Planar;

/// <summary>Проверяет связность и локальную геометрию сохранённого снимка сетки.</summary>
public static class PlanarMeshSnapshotValidator
{
    public static IReadOnlyList<FemValidationDiagnostic> Validate(PlanarMeshSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = new List<FemValidationDiagnostic>();
        var nodes = new Dictionary<int, PlanarMeshNode>();

        foreach (var node in snapshot.Nodes)
        {
            if (!nodes.TryAdd(node.Index, node))
                diagnostics.Add(new("planar_mesh_node_duplicate", $"Узел сетки {node.Index} повторяется."));
            if (!double.IsFinite(node.U) || !double.IsFinite(node.V) || !double.IsFinite(node.X) ||
                !double.IsFinite(node.Y) || !double.IsFinite(node.Z))
                diagnostics.Add(new("planar_mesh_node_nonfinite", $"Узел сетки {node.Index} содержит нечисловую координату."));
        }

        foreach (var element in snapshot.Elements)
        {
            var expectedCount = element.Kind == PlanarMeshElementKind.Triangle3 ? 3 : 4;
            if (element.NodeIndices.Count != expectedCount)
            {
                diagnostics.Add(new("planar_mesh_element_node_count", $"Элемент {element.Index} имеет неверное число узлов."));
                continue;
            }

            if (element.NodeIndices.Any(index => !nodes.ContainsKey(index)))
            {
                diagnostics.Add(new("planar_mesh_element_unknown_node", $"Элемент {element.Index} ссылается на отсутствующий узел."));
                continue;
            }

            if (Math.Abs(SignedArea(element.NodeIndices.Select(index => nodes[index]).ToArray())) <= 1e-12)
                diagnostics.Add(new("planar_mesh_element_degenerate", $"Элемент {element.Index} имеет нулевую площадь."));
        }

        return diagnostics;
    }

    static double SignedArea(IReadOnlyList<PlanarMeshNode> nodes)
    {
        double twiceArea = 0;
        for (var index = 0; index < nodes.Count; index++)
        {
            var current = nodes[index];
            var next = nodes[(index + 1) % nodes.Count];
            twiceArea += current.U * next.V - next.U * current.V;
        }
        return twiceArea / 2;
    }
}
