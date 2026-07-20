using System.Windows.Media.Media3D;

namespace OpenCS.ViewModels;

/// <summary>Строит ленты эпюр усилий вдоль стержней как MeshGeometry3D.</summary>
public static class FemForceDiagramFactory
{
    /// <summary>Каждый сегмент — четырёхугольник от оси стержня со смещениями на концах.</summary>
    public static MeshGeometry3D BuildRibbons(
        IEnumerable<(Point3D A, Point3D B, Vector3D OffA, Vector3D OffB)> segments)
    {
        var mesh = new MeshGeometry3D();
        foreach (var s in segments)
        {
            int n = mesh.Positions.Count;
            mesh.Positions.Add(s.A);
            mesh.Positions.Add(s.B);
            mesh.Positions.Add(s.B + s.OffB);
            mesh.Positions.Add(s.A + s.OffA);

            // Лицевая и обратная стороны (для видимости с любого ракурса)
            mesh.TriangleIndices.Add(n); mesh.TriangleIndices.Add(n + 1); mesh.TriangleIndices.Add(n + 2);
            mesh.TriangleIndices.Add(n); mesh.TriangleIndices.Add(n + 2); mesh.TriangleIndices.Add(n + 3);
            mesh.TriangleIndices.Add(n); mesh.TriangleIndices.Add(n + 2); mesh.TriangleIndices.Add(n + 1);
            mesh.TriangleIndices.Add(n); mesh.TriangleIndices.Add(n + 3); mesh.TriangleIndices.Add(n + 2);
        }
        return mesh;
    }
}
