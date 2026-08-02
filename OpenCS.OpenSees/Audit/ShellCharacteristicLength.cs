using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Audit;

/// <summary>Характеристическая длина shell-элемента: sqrt(area).</summary>
public sealed record ShellElementCharacteristicLength(
    int ElementTag,
    ShellElementKind ElementKind,
    double Area,
    double CharacteristicLength);

/// <summary>Вычисляет площадь и характеристическую длину Q4/T3 элемента по координатам узлов.</summary>
public static class ShellCharacteristicLength
{
    /// <summary>Вычисляет characteristic length как sqrt площади элемента.</summary>
    public static ShellElementCharacteristicLength Compute(
        NormalizedShellElement element,
        IReadOnlyDictionary<int, NormalizedShellNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(nodes);

        double area = element.Kind switch
        {
            ShellElementKind.ASDShellQ4 => QuadArea(element, nodes),
            ShellElementKind.ASDShellT3 => TriangleArea(element, nodes),
            _ => throw new ArgumentOutOfRangeException(nameof(element), $"Неизвестный тип элемента {element.Kind}.")
        };

        if (!double.IsFinite(area) || area <= 0)
            throw new ArgumentException($"Элемент {element.Tag} имеет вырожденную площадь {area}.", nameof(element));

        return new ShellElementCharacteristicLength(element.Tag, element.Kind, area, Math.Sqrt(area));
    }

    private static double QuadArea(NormalizedShellElement element, IReadOnlyDictionary<int, NormalizedShellNode> nodes)
    {
        double[] a = Node(element, nodes, element.NodeTags[0]);
        double[] b = Node(element, nodes, element.NodeTags[1]);
        double[] c = Node(element, nodes, element.NodeTags[2]);
        double[] d = Node(element, nodes, element.NodeTags[3]);
        return 0.5 * Norm(Cross(Sub(c, a), Sub(d, b)));
    }

    private static double TriangleArea(NormalizedShellElement element, IReadOnlyDictionary<int, NormalizedShellNode> nodes)
    {
        double[] a = Node(element, nodes, element.NodeTags[0]);
        double[] b = Node(element, nodes, element.NodeTags[1]);
        double[] c = Node(element, nodes, element.NodeTags[2]);
        return 0.5 * Norm(Cross(Sub(b, a), Sub(c, a)));
    }

    private static double[] Node(NormalizedShellElement element, IReadOnlyDictionary<int, NormalizedShellNode> nodes, int tag)
    {
        if (!nodes.TryGetValue(tag, out NormalizedShellNode? node))
            throw new ArgumentException($"Элемент {element.Tag} ссылается на неизвестный узел {tag}.", nameof(nodes));
        return [node.X, node.Y, node.Z];
    }

    private static double[] Sub(double[] left, double[] right) =>
        [left[0] - right[0], left[1] - right[1], left[2] - right[2]];

    private static double[] Cross(double[] left, double[] right) =>
    [
        left[1] * right[2] - left[2] * right[1],
        left[2] * right[0] - left[0] * right[2],
        left[0] * right[1] - left[1] * right[0]
    ];

    private static double Norm(double[] vector) =>
        Math.Sqrt(vector[0] * vector[0] + vector[1] * vector[1] + vector[2] * vector[2]);
}
