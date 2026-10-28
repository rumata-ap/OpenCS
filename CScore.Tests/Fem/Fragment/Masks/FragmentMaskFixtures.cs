using CScore.Fem;
using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Tests.Fem.Fragment.Masks;

/// <summary>Сетки родителя для тестов масок.</summary>
static class FragmentMaskFixtures
{
    public static PlanarVector3 V(double x, double y, double z) => new(x, y, z);

    /// <summary>
    /// Стена в плоскости XZ (y = <paramref name="y"/>): x ∈ [0, w], z ∈ [z0, z0 + h], nx × nz четырёхугольников.
    /// Узлы «r·(nx+1)+c+1 + <paramref name="tagBase"/>»; КЭ хранятся «1 2 4 3» (низ-лево, низ-право, верх-лево, верх-право):
    /// оси пластины X1 = X, Y1 = Z, нормаль −Y. <paramref name="flip"/> — для чётных КЭ обратный обход (нормаль +Y).
    /// </summary>
    public static (Dictionary<string, FemMeshNode> Nodes, List<FemElement> Elements) Wall(
        double w, double h, int nx, int nz, double z0 = 0, double y = 0, int tagBase = 0, bool flip = false)
    {
        var nodes = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        string T(int r, int c) => (r * (nx + 1) + c + 1 + tagBase).ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (int r = 0; r <= nz; r++)
            for (int c = 0; c <= nx; c++)
                nodes[T(r, c)] = new FemMeshNode { NodeTag = T(r, c), X = w * c / nx, Y = y, Z = z0 + h * r / nz };
        var elements = new List<FemElement>();
        int n = 0;
        for (int r = 0; r < nz; r++)
            for (int c = 0; c < nx; c++)
            {
                n++;
                string[] ids = flip && n % 2 == 0
                    ? [T(r, c + 1), T(r, c), T(r + 1, c + 1), T(r + 1, c)]
                    : [T(r, c), T(r, c + 1), T(r + 1, c), T(r + 1, c + 1)];
                elements.Add(new FemElement
                {
                    ElemTag = (n + tagBase).ToString(System.Globalization.CultureInfo.InvariantCulture), ElemType = "shell",
                    NodeIdsJson = "[" + string.Join(",", ids) + "]",
                });
            }
        return (nodes, elements);
    }

    public static IEnumerable<(string Tag, FemElementGeometry G)> Shells(
        Dictionary<string, FemMeshNode> nodes, IEnumerable<FemElement> elements) =>
        elements.Select(e => (e.ElemTag, FemElementGeometry.Of(e, nodes)!));
}
