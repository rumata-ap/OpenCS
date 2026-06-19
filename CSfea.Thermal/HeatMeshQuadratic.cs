namespace CSfea.Thermal;

/// <summary>
/// Повышение линейной T3-сетки до квадратичной T6 (середины сторон).
/// </summary>
public static class HeatMeshQuadratic
{
    /// <summary>
    /// Построить 6-узловую сетку: для каждого ребра линейной сетки добавляется один узел в середине.
    /// Порядок узлов элемента: [v0, v1, v2, m01, m12, m20].
    /// </summary>
    public static HeatMesh Promote(HeatMesh linear)
    {
        ArgumentNullException.ThrowIfNull(linear);
        foreach (var el in linear.Elements)
        {
            if (el.Length != 3)
                throw new ArgumentException("Promote ожидает линейные элементы из 3 узлов.", nameof(linear));
        }

        var x = linear.X.ToList();
        var y = linear.Y.ToList();
        var midIndex = new Dictionary<(int, int), int>();

        int MidNode(int a, int b)
        {
            int i = Math.Min(a, b);
            int j = Math.Max(a, b);
            if (!midIndex.TryGetValue((i, j), out int mid))
            {
                mid = x.Count;
                midIndex[(i, j)] = mid;
                x.Add(0.5 * (linear.X[i] + linear.X[j]));
                y.Add(0.5 * (linear.Y[i] + linear.Y[j]));
            }
            return mid;
        }

        var elements = new int[linear.Elements.Length][];
        for (int e = 0; e < linear.Elements.Length; e++)
        {
            int n0 = linear.Elements[e][0];
            int n1 = linear.Elements[e][1];
            int n2 = linear.Elements[e][2];
            elements[e] = [n0, n1, n2, MidNode(n0, n1), MidNode(n1, n2), MidNode(n2, n0)];
        }

        return new HeatMesh([.. x], [.. y], elements);
    }

    /// <summary>Индекс середины ребра (a,b) в квадратичной сетке, полученной из линейной.</summary>
    public static int? TryGetMidNode(HeatMesh linear, HeatMesh quadratic, int a, int b)
    {
        int i = Math.Min(a, b);
        int j = Math.Max(a, b);
        double mx = 0.5 * (linear.X[i] + linear.X[j]);
        double my = 0.5 * (linear.Y[i] + linear.Y[j]);
        const double tol = 1e-9;
        for (int k = linear.NNodes; k < quadratic.NNodes; k++)
        {
            if (Math.Abs(quadratic.X[k] - mx) < tol && Math.Abs(quadratic.Y[k] - my) < tol)
                return k;
        }
        return null;
    }

    /// <summary>Сетка содержит 6-узловые элементы.</summary>
    public static bool IsQuadratic(HeatMesh mesh)
        => mesh.Elements.Length > 0 && mesh.Elements[0].Length == 6;
}
