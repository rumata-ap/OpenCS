using System.Windows.Media.Media3D;

namespace OpenCS.ViewModels;

/// <summary>Строит линейные глифы узлов сохранённой расчётной сетки.</summary>
public static class FemMeshNodeGlyphFactory
{
    /// <summary>Возвращает три ортогональных отрезка для каждого узла.</summary>
    public static Point3DCollection Create(IEnumerable<Point3D> nodes, double halfSize = 0.03)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (halfSize <= 0 || !double.IsFinite(halfSize))
            throw new ArgumentOutOfRangeException(nameof(halfSize));

        var points = new Point3DCollection();
        foreach (var node in nodes)
        {
            points.Add(new Point3D(node.X - halfSize, node.Y, node.Z));
            points.Add(new Point3D(node.X + halfSize, node.Y, node.Z));
            points.Add(new Point3D(node.X, node.Y - halfSize, node.Z));
            points.Add(new Point3D(node.X, node.Y + halfSize, node.Z));
            points.Add(new Point3D(node.X, node.Y, node.Z - halfSize));
            points.Add(new Point3D(node.X, node.Y, node.Z + halfSize));
        }
        return points;
    }
}
