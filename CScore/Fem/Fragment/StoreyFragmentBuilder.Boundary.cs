using CScore.Planar;

namespace CScore.Fem.Fragment;

public static partial class StoreyFragmentBuilder
{
    /// <summary>
    /// Пружины и закрепления узлов сетки родителя в полосе (срез 4): узел сетки → узел схемы фрагмента в той же точке
    /// (внутри плиты мешер его вставит, как у узловых нагрузок), закрепление — в его <see cref="FemNode.DofMask"/>,
    /// пружина — <see cref="FemSpringTargetKinds.Node"/> с жёсткостями и происхождением родителя; пружины узлов схемы
    /// родителя в полосе — так же. Несколько пружин в одном узле фрагмента складываются (параллельно).
    /// </summary>
    static void TransferBoundary(Ctx c, StoreyFragmentParentSnapshot parent)
    {
        var meshNodes = parent.MeshNodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var nodes = parent.Nodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var s in parent.MeshNodeSupports)
            if ((s.Mask & FemBoundaryDofs.All) != 0 && meshNodes.TryGetValue(s.NodeTag, out var n) && c.InBox(P(n)))
                c.Nodes.Get(new PlanarVector3(n.X, n.Y, n.Z), s.Mask & FemBoundaryDofs.All);

        var byNode = new Dictionary<string, FemSpring>(StringComparer.Ordinal);
        foreach (var s in parent.Springs)
        {
            if (s.ActiveMask == 0) continue;
            PlanarVector3? at = s.TargetKind == FemSpringTargetKinds.MeshNode
                ? meshNodes.TryGetValue(s.NodeTag, out var m) ? new PlanarVector3(m.X, m.Y, m.Z) : null
                : nodes.TryGetValue(s.NodeTag, out var sn) ? P(sn) : null;
            if (at is not { } p || !c.InBox(p)) continue;
            var node = c.Nodes.Get(p);
            if (byNode.TryGetValue(node.NodeTag, out var sum))
            {
                var k = sum.Stiffnesses;
                var add = s.Stiffnesses;
                for (int i = 0; i < 6; i++) k[i] += add[i];
                sum.SetStiffnesses(k);
                continue;
            }
            var copy = new FemSpring
            {
                TargetKind = FemSpringTargetKinds.Node, NodeTag = node.NodeTag, Origin = s.Origin, SourceElemTag = s.SourceElemTag,
            };
            copy.SetStiffnesses(s.Stiffnesses);
            byNode[node.NodeTag] = copy;
            c.Draft.Springs.Add(copy);
        }
    }
}
