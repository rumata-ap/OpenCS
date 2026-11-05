using CScore.Fem.Loads;
using CScore.Submodel;

namespace CScore.Fem.Fragment.Masks;

/// <summary>Узловые силы КЭ родителя на словарях: тесты и простые провайдеры (как <see cref="FragmentParentResultTable"/>).</summary>
public sealed class FragmentNodalForcesTable : IFragmentParentNodalForces
{
    static readonly IReadOnlyDictionary<string, Dof6> Empty = new Dictionary<string, Dof6>(StringComparer.Ordinal);
    readonly Dictionary<(FragmentMaskKey, string), Dictionary<string, Dof6>> _forces = [], _loads = [];
    readonly Dictionary<FragmentMaskKey, Dictionary<string, Dof6>> _nodeLoads = [], _reactions = [];
    readonly Dictionary<(FragmentMaskKey, string), List<FemLoadPiece>> _pieces = [];

    public List<(string A, string? B)> Links { get; } = [];
    public IReadOnlyCollection<(string A, string? B)> NodeLinks => Links;

    public void SetElementNodeForce(FragmentMaskKey key, string elemTag, string nodeTag, Dof6 f) => Slot(_forces, (key, elemTag))[nodeTag] = f;
    public void SetElementLoad(FragmentMaskKey key, string elemTag, string nodeTag, Dof6 f) => Slot(_loads, (key, elemTag))[nodeTag] = f;
    public void AddElementLoadPiece(FragmentMaskKey key, string elemTag, FemLoadPiece piece)
    {
        if (!_pieces.TryGetValue((key, elemTag), out var list)) _pieces[(key, elemTag)] = list = [];
        list.Add(piece);
    }
    public void AddNodeLoad(FragmentMaskKey key, string nodeTag, Dof6 f) => Accumulate(Slot(_nodeLoads, key), nodeTag, f);
    public void AddReaction(FragmentMaskKey key, string nodeTag, Dof6 f) => Accumulate(Slot(_reactions, key), nodeTag, f);

    public bool TryElementNodeForces(FragmentMaskKey key, string elemTag, out IReadOnlyDictionary<string, Dof6> forces)
    {
        bool ok = _forces.TryGetValue((key, elemTag), out var d);
        forces = d ?? Empty;
        return ok;
    }

    public IReadOnlyDictionary<string, Dof6> ElementLoads(FragmentMaskKey key, string elemTag) =>
        _loads.TryGetValue((key, elemTag), out var d) ? d : Empty;
    public IReadOnlyList<FemLoadPiece> ElementLoadPieces(FragmentMaskKey key, string elemTag) =>
        _pieces.TryGetValue((key, elemTag), out var list) ? list : [];
    public IReadOnlyDictionary<string, Dof6> NodeLoads(FragmentMaskKey key) => _nodeLoads.TryGetValue(key, out var d) ? d : Empty;
    public IReadOnlyDictionary<string, Dof6> Reactions(FragmentMaskKey key) => _reactions.TryGetValue(key, out var d) ? d : Empty;

    static Dictionary<string, Dof6> Slot<TKey>(Dictionary<TKey, Dictionary<string, Dof6>> map, TKey key) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var d)) map[key] = d = new(StringComparer.Ordinal);
        return d;
    }

    static void Accumulate(Dictionary<string, Dof6> d, string tag, Dof6 f) => d[tag] = d.TryGetValue(tag, out var s) ? s + f : f;
}
