using CSTriangulation.Ruppert;

namespace CScore.Planar;

/// <summary>Плоская триангуляция PlanarRegion (Hull+Holes) для 3D-визуализации (не для расчёта) —
/// голый CDT без рефайнмента (DoRefine=false), вершины в локальных 2D координатах PlanarRegion.
/// Используется Fem3DVM.BuildPlanarRegionMesh для заливки полигона в 3D-виде схемы.</summary>
public static class PlanarRegionTriangulation
{
    public static ((double X, double Y)[] Vertices, (int A, int B, int C)[] Triangles) Triangulate(PlanarRegion region)
    {
        var outer = ToOpenLoop(region.RequireHull());
        var holes = region.Holes.Select(ToOpenLoop).ToArray();

        var result = Triangulator.FromPolygon(outer, holes, new TriangulationParams { DoRefine = false });
        return (result.Vertices, result.Triangles);
    }

    static Vec2[] ToOpenLoop(Contour c)
    {
        int n = c.X.Count - 1; // последняя точка дублирует первую (закрытый контур PlanarRegion)
        var pts = new Vec2[n];
        for (int i = 0; i < n; i++) pts[i] = new Vec2(c.X[i], c.Y[i]);
        return pts;
    }
}
