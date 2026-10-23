using System.Globalization;
using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>Узлы фрагмента по координатам: совпадающие (до 1 мкм) — один узел; теги с 1 по порядку создания.</summary>
sealed class FragmentNodeTable(List<FemNode> nodes)
{
    const double Snap = 1e-6;
    readonly Dictionary<(long, long, long), FemNode> _byKey = [];

    /// <summary>Узел в точке; маска закреплений объединяется с уже имеющейся.</summary>
    public FemNode Get(PlanarVector3 p, int dofMask = 0)
    {
        var key = Key(p);
        if (_byKey.TryGetValue(key, out var node)) { node.DofMask |= dofMask; return node; }
        node = new FemNode
        {
            NodeTag = (nodes.Count + 1).ToString(CultureInfo.InvariantCulture),
            X = p.X, Y = p.Y, Z = p.Z, DofMask = dofMask,
        };
        nodes.Add(node);
        _byKey[key] = node;
        return node;
    }

    /// <summary>Узел в точке, если он уже есть.</summary>
    public FemNode? Find(PlanarVector3 p) => _byKey.GetValueOrDefault(Key(p));

    static (long, long, long) Key(PlanarVector3 p) =>
        ((long)Math.Round(p.X / Snap), (long)Math.Round(p.Y / Snap), (long)Math.Round(p.Z / Snap));
}
