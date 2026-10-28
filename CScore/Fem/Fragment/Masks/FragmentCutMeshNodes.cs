using System.Globalization;
using CScore.Planar;

namespace CScore.Fem.Fragment.Masks;

/// <summary>Узел сетки фрагмента на отрезке кромки: <see cref="S"/> — координата маски (по всем отрезкам), м.</summary>
public sealed record FragmentCutNode(string Tag, double S, PlanarVector3 X);

/// <summary>
/// Узлы сетки фрагмента на одном разрезе (спека 5.2): конец стержня — один узел (<see cref="EndNode"/>); кромка — узлы
/// каждого отрезка по возрастанию <see cref="FragmentCutNode.S"/> (<see cref="Segments"/>; узел на стыке отрезков входит
/// в оба). <see cref="Error"/> — разрез не лёг на сетку.
/// </summary>
public sealed record FragmentCutMeshNodes(string? EndNode, IReadOnlyList<IReadOnlyList<FragmentCutNode>> Segments, string? Error)
{
    /// <summary>Все узлы разреза без повторов.</summary>
    public IEnumerable<string> Tags => EndNode != null ? [EndNode] : Segments.SelectMany(s => s).Select(n => n.Tag).Distinct();

    /// <summary>
    /// Узлы разреза. Конец стержня — узел сетки с <see cref="FemMeshNode.SourceNodeTag"/> = <see cref="FragmentCut.NodeTag"/>,
    /// иначе ближайший к точке конца в пределах <paramref name="tol"/>. Кромка — узлы на отрезке (расстояние ≤ tol, проекция
    /// в его пределах); на концах отрезка узлы обязательны (сетка КонЭ стены их содержит).
    /// </summary>
    public static FragmentCutMeshNodes Find(FragmentMaskCutInput cut, IReadOnlyList<FemMeshNode> meshNodes, double tol)
    {
        ArgumentNullException.ThrowIfNull(cut);
        ArgumentNullException.ThrowIfNull(meshNodes);
        var c = cut.Cut;
        if (c.Kind == FragmentCutKind.End)
        {
            var bySource = c.NodeTag is { } tag ? meshNodes.FirstOrDefault(n => n.SourceNodeTag == tag) : null;
            if (bySource != null) return new(bySource.NodeTag, [], null);
            if (cut.EndPoint is { } p)
            {
                var near = meshNodes.Where(n => (Point(n) - p).Length <= tol).MinBy(n => (Point(n) - p).Length);
                if (near != null) return new(near.NodeTag, [], null);
            }
            return new(null, [], $"узла сетки у конца «{c.NodeTag}» нет");
        }

        var segments = new List<IReadOnlyList<FragmentCutNode>>();
        double offset = 0;
        foreach (var (a, b) in c.Segments)
        {
            double len = (b - a).Length;
            if (len <= tol) { offset += len; continue; }
            var axis = (b - a) * (1 / len);
            var on = new List<FragmentCutNode>();
            foreach (var n in meshNodes)
            {
                var x = Point(n);
                double t = (x - a).Dot(axis);
                if (t < -tol || t > len + tol) continue;
                if ((x - (a + axis * t)).Length > tol) continue;
                on.Add(new FragmentCutNode(n.NodeTag, offset + Math.Clamp(t, 0, len), x));
            }
            on.Sort((u, v) => u.S.CompareTo(v.S));
            // Совпавшие узлы (двойники по координате) — первый.
            var unique = new List<FragmentCutNode>();
            foreach (var n in on)
                if (unique.Count == 0 || n.S - unique[^1].S > tol) unique.Add(n);
            if (unique.Count < 2 || unique[0].S - offset > tol || offset + len - unique[^1].S > tol)
                return new(null, [], string.Create(CultureInfo.CurrentCulture,
                    $"отрезок кромки ({a.X:0.###}; {a.Y:0.###}) – ({b.X:0.###}; {b.Y:0.###}) не лёг на сетку: нет узлов на его концах"));
            unique[0] = unique[0] with { S = offset };
            unique[^1] = unique[^1] with { S = offset + len };
            segments.Add(unique);
            offset += len;
        }
        if (segments.Count == 0) return new(null, [], "у кромки нет отрезков");
        return new(null, segments, null);
    }

    static PlanarVector3 Point(FemMeshNode n) => new(n.X, n.Y, n.Z);
}
