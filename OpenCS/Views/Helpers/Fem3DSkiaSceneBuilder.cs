using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using OpenCS.Render3D;
using OpenCS.ViewModels;

namespace OpenCS.Views.Helpers;

/// <summary>
/// Переводит геометрию схемы КЭ из <see cref="Fem3DVM"/> (WPF 3D-типы) в нейтральную
/// <see cref="Scene3D"/> для Skia-рендера. Пробная версия: только просмотр — стержни, оболочки,
/// рёбра сетки и узлы; условные знаки нагрузок и закреплений не переносятся.
/// </summary>
public static class Fem3DSkiaSceneBuilder
{
    public static Scene3D Build(Fem3DVM vm, bool showNodes, bool showGrid)
    {
        var scene = new Scene3D();

        foreach (var group in vm.BarGroups)
            AddLines(scene, group.Points, group.Color, (float)group.Thickness);

        bool isHighlight = vm.HiShellMesh != null;
        AddMesh(scene, vm.ShellMesh, isHighlight ? Fem3DVM.ShellBgColor : Fem3DVM.ShellColor);
        AddMesh(scene, vm.HiShellMesh, Fem3DVM.ShellHiColor);

        foreach (var pv in vm.PlanarRegionVisuals)
        {
            AddMesh(scene, pv.Mesh, Fem3DVM.PlanarRegionMeshColor);
            AddLines(scene, pv.EdgePoints, Colors.SteelBlue, 1.2f);
        }

        if (showGrid)
        {
            AddLines(scene, vm.MeshLinePoints, Colors.MediumTurquoise, 2f);
            AddLines(scene, vm.ShellEdgePoints, Colors.DimGray, 0.5f);
            AddLines(scene, vm.PlanarRegionMeshEdgePoints, Colors.DimGray, 0.5f);
        }

        if (showNodes && vm.NodePoints is { } nodes)
        {
            uint argb = Argb(Colors.DimGray);
            foreach (var p in nodes) scene.Points.Add(new Scene3D.Point(V(p), argb, 3f));
        }

        return scene;
    }

    /// <summary>Пары точек — отрезки (как в <c>LinesVisual3D</c>).</summary>
    static void AddLines(Scene3D scene, Point3DCollection? pts, Color color, float width)
    {
        if (pts == null) return;
        uint argb = Argb(color);
        for (int i = 0; i + 1 < pts.Count; i += 2)
            scene.Lines.Add(new Scene3D.Line(V(pts[i]), V(pts[i + 1]), argb, width));
    }

    static void AddMesh(Scene3D scene, MeshGeometry3D? mesh, Color color)
    {
        if (mesh == null) return;
        uint argb = Argb(color);
        var pos = mesh.Positions;
        var idx = mesh.TriangleIndices;
        if (idx.Count == 0)
        {
            for (int i = 0; i + 2 < pos.Count; i += 3)
                scene.Triangles.Add(new Scene3D.Triangle(V(pos[i]), V(pos[i + 1]), V(pos[i + 2]), argb));
            return;
        }
        for (int i = 0; i + 2 < idx.Count; i += 3)
            scene.Triangles.Add(new Scene3D.Triangle(V(pos[idx[i]]), V(pos[idx[i + 1]]), V(pos[idx[i + 2]]), argb));
    }

    static Vector3 V(Point3D p) => new((float)p.X, (float)p.Y, (float)p.Z);

    static uint Argb(Color c) => (uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B);
}
