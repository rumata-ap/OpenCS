namespace CScore.Planar;

/// <summary>Представительная точка (u, v) элемента снимка сетки — среднее по вершинам.
/// Точно для T3, стандартная аппроксимация для Q4; используется только для попадания в
/// зону армирования (PlateRebarFieldResolver), не для интегрирования.</summary>
public static class PlanarMeshElementCentroid
{
    public static (double U, double V) Centroid(
        this PlanarMeshElement element, IReadOnlyList<PlanarMeshNode> nodes)
    {
        double u = 0, v = 0;
        foreach (int index in element.NodeIndices)
        {
            u += nodes[index].U;
            v += nodes[index].V;
        }
        int n = element.NodeIndices.Count;
        return (u / n, v / n);
    }
}
