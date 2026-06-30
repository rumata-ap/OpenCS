using Rupp = CSTriangulation.Ruppert;

namespace CSfea.Torsion;

/// <summary>Результат построения сетки для МКЭ-кручения.</summary>
public sealed class TorsionMesh
{
    public double[] NodesX { get; init; } = [];
    public double[] NodesY { get; init; } = [];
    /// <summary>Треугольники: [i0, i1, i2] на элемент.</summary>
    public int[][] Triangles { get; init; } = [];
    /// <summary>Индексы граничных узлов (внешний контур + контуры отверстий).</summary>
    public int[] FixedDofs { get; init; } = [];
}

/// <summary>Построение сетки области через триангуляцию Рупперта.</summary>
public static class MeshBuilder
{
    /// <summary>
    /// Трианглирует область (внешний контур CCW + отверстия CW), возвращает сетку и
    /// индексы граничных узлов (для краевого условия φ=0). maxElementSize — целевой размер элемента.
    /// </summary>
    public static TorsionMesh Build(TorsionBoundary boundary, double maxElementSize)
    {
        var outerPts = new Rupp.Vec2[boundary.OuterX.Length];
        for (int i = 0; i < outerPts.Length; i++)
            outerPts[i] = new Rupp.Vec2(boundary.OuterX[i], boundary.OuterY[i]);
        Rupp.Vec2[][]? holes = null;
        if (boundary.Holes is { Count: > 0 })
        {
            holes = new Rupp.Vec2[boundary.Holes.Count][];
            for (int k = 0; k < boundary.Holes.Count; k++)
            {
                var (hx, hy) = boundary.Holes[k];
                var pts = new Rupp.Vec2[hx.Length];
                for (int i = 0; i < pts.Length; i++)
                    pts[i] = new Rupp.Vec2(hx[i], hy[i]);
                holes[k] = pts;
            }
        }

        var parms = new Rupp.TriangulationParams
        {
            MinAngleDeg = 26.0,
            MaxEdgeLen = maxElementSize,
            DoRefine = true
        };
        Rupp.RuppertResult result = Rupp.Triangulator.FromPolygon(outerPts, holes, parms);

        int n = result.Vertices.Length;
        var nx = new double[n];
        var ny = new double[n];
        for (int i = 0; i < n; i++) { nx[i] = result.Vertices[i].X; ny[i] = result.Vertices[i].Y; }

        var tris = new int[result.Triangles.Length][];
        for (int t = 0; t < tris.Length; t++)
            tris[t] = new[] { result.Triangles[t].Item1, result.Triangles[t].Item2, result.Triangles[t].Item3 };

        var boundarySet = new HashSet<int>();
        foreach (var (a, b) in result.ConstrainedEdges) { boundarySet.Add(a); boundarySet.Add(b); }

        return new TorsionMesh
        {
            NodesX = nx,
            NodesY = ny,
            Triangles = tris,
            FixedDofs = boundarySet.Order().ToArray()
        };
    }
}
