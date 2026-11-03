using System.Globalization;

namespace CScore.Fem;

/// <summary>
/// Узлы схемы без КЭ, но с закреплением или пружиной (голова сваи, опора без стержня): дискретизация стержней и сетка
/// пластин узлов сетки в них не создают, а закрепления и пружины сводятся к узлам сетки по
/// <see cref="FemMeshNode.SourceNodeTag"/>. Такому узлу добавляется узел сетки в его точке — либо берётся уже
/// имеющийся узел сетки в той же точке без источника (общий узел). Без этого ГУ «не попали в сетку».
/// </summary>
public static class FemIsolatedSupportNodes
{
    /// <summary>Допуск совпадения точек узла схемы и узла сетки, м (как у дискретизации).</summary>
    const double Tolerance = FemMeshDiscretizer.CollinearToleranceM;

    /// <summary>
    /// Дополняет <paramref name="meshNodes"/> узлами сетки для изолированных узлов схемы с закреплением или активной
    /// пружиной, если их ещё нет. Возвращает число добавленных узлов; существующие узлы сетки не меняются, кроме
    /// <see cref="FemMeshNode.SourceNodeTag"/> у совпавшего по точке узла без источника.
    /// </summary>
    public static int Add(List<FemMeshNode> meshNodes, IReadOnlyList<FemNode> nodes, IReadOnlyList<FemSpring> springs, int schemaId)
    {
        ArgumentNullException.ThrowIfNull(meshNodes);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(springs);

        var mapped = meshNodes.Where(n => n.SourceNodeTag != null).Select(n => n.SourceNodeTag!).ToHashSet(StringComparer.Ordinal);
        var springNodes = springs
            .Where(s => s.ActiveMask != 0 && s.TargetKind == FemSpringTargetKinds.Node)
            .Select(s => s.NodeTag).ToHashSet(StringComparer.Ordinal);
        int nextTag = meshNodes.Select(n => int.TryParse(n.NodeTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : 0)
            .DefaultIfEmpty(0).Max() + 1;
        int added = 0;
        foreach (var node in nodes.GroupBy(n => n.NodeTag, StringComparer.Ordinal).Select(g => g.First()))
        {
            bool needed = (node.DofMask & FemBoundaryDofs.All) != 0 || springNodes.Contains(node.NodeTag);
            if (!needed || mapped.Contains(node.NodeTag)) continue;

            var same = meshNodes.FirstOrDefault(n => Math.Abs(n.X - node.X) <= Tolerance && Math.Abs(n.Y - node.Y) <= Tolerance
                                                    && Math.Abs(n.Z - node.Z) <= Tolerance);
            if (same is not null)
            {
                // Узел сетки в точке уже есть (пластина, сетка импорта): без источника — он и есть узел схемы.
                if (same.SourceNodeTag is null)
                {
                    same.SourceNodeTag = node.NodeTag;
                    mapped.Add(node.NodeTag);
                }
                continue;
            }

            meshNodes.Add(new FemMeshNode
            {
                SchemaId = schemaId,
                NodeTag = nextTag++.ToString(CultureInfo.InvariantCulture),
                X = node.X, Y = node.Y, Z = node.Z,
                SourceNodeTag = node.NodeTag,
                Origin = FemMember.MeshSourceGenerated,
            });
            mapped.Add(node.NodeTag);
            added++;
        }
        return added;
    }
}
