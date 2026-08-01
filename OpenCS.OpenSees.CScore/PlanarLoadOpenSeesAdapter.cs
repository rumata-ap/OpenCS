using CScore.Planar;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Boundary set в tag-пространстве OpenSees без solver-specific constraints.</summary>
public sealed record PlanarOpenSeesBoundarySet(
    BoundaryRole Role,
    IReadOnlyList<PlanarBoundaryKey> BoundaryKeys,
    IReadOnlyList<int> NodeTags,
    IReadOnlyList<(int A, int B)> Edges);

/// <summary>Переводит проверенный PlanarLoad mapping в узловые нагрузки OpenSees.</summary>
public static class PlanarLoadOpenSeesAdapter
{
    public static IReadOnlyList<ShellNodalLoad> Map(
        PlanarLoadMappingResult result,
        IReadOnlyDictionary<int, int> nodeIndexToTag)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(nodeIndexToTag);
        EnsureCalculable(result);

        var loads = new List<ShellNodalLoad>(result.NodalLoads.Count);
        foreach ((int nodeIndex, PlanarVector3 force) in result.NodalLoads.OrderBy(item => item.Key))
        {
            if (!nodeIndexToTag.TryGetValue(nodeIndex, out int nodeTag))
                throw new InvalidOperationException($"PlanarLoad mapping ссылается на неизвестный OpenSees node tag для snapshot node {nodeIndex}.");
            loads.Add(new(nodeTag, force.X, force.Y, force.Z, 0, 0, 0));
        }
        return loads;
    }

    public static PlanarOpenSeesBoundarySet MapBoundarySet(
        PlanarBoundarySet set,
        IReadOnlyDictionary<int, int> nodeIndexToTag)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(nodeIndexToTag);
        int Tag(int nodeIndex) => nodeIndexToTag.TryGetValue(nodeIndex, out int tag)
            ? tag
            : throw new InvalidOperationException(
                $"Boundary set ссылается на неизвестный OpenSees node tag для snapshot node {nodeIndex}.");

        return new(
            set.Role,
            set.BoundaryKeys,
            set.NodeIndices.Select(Tag).ToArray(),
            set.Edges.Select(edge => (Tag(edge.A), Tag(edge.B))).ToArray());
    }

    static void EnsureCalculable(PlanarLoadMappingResult result)
    {
        if (!result.IsCalculable)
            throw new InvalidOperationException("Нерасчётный PlanarLoad mapping нельзя передать в OpenSees.");
    }
}
