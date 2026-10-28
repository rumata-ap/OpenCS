using CScore.Submodel;

namespace CScore.Fem.Fragment.Masks;

/// <summary>Источник результата родителя на словарях: заполняют провайдеры (CSfea, ЛИРА, SCAD) и тесты.</summary>
public sealed class FragmentParentResultTable : IFragmentParentResult
{
    readonly List<FragmentMaskKey> _keys = [];
    readonly Dictionary<(FragmentMaskKey, string), Dof6> _disp = [];
    readonly Dictionary<(FragmentMaskKey, string), BeamEndForces> _beam = [];
    readonly Dictionary<(FragmentMaskKey, string), FragmentShellForces> _shell = [];
    readonly Dictionary<(FragmentMaskKey, string), List<FragmentBeamSpanLoad>> _loads = [];

    public IReadOnlyList<FragmentMaskKey> Keys => _keys;
    public bool KnowsBeamLoads { get; set; } = true;

    public void AddKey(FragmentMaskKey key)
    {
        if (!_keys.Contains(key)) _keys.Add(key);
    }

    public void SetDisplacement(FragmentMaskKey key, string nodeTag, Dof6 u) => _disp[(key, nodeTag)] = u;
    public void SetBeamEndForces(FragmentMaskKey key, string elemTag, BeamEndForces f) => _beam[(key, elemTag)] = f;
    public void SetShellForces(FragmentMaskKey key, string elemTag, FragmentShellForces f) => _shell[(key, elemTag)] = f;

    public void AddBeamLoad(FragmentMaskKey key, string elemTag, FragmentBeamSpanLoad load)
    {
        if (!_loads.TryGetValue((key, elemTag), out var list)) _loads[(key, elemTag)] = list = [];
        list.Add(load);
    }

    public bool TryNodeDisplacement(FragmentMaskKey key, string meshNodeTag, out Dof6 displacement)
    {
        bool ok = _disp.TryGetValue((key, meshNodeTag), out var d);
        displacement = d!;
        return ok;
    }

    public bool TryBeamEndForces(FragmentMaskKey key, string elemTag, out BeamEndForces forces)
    {
        bool ok = _beam.TryGetValue((key, elemTag), out var f);
        forces = f!;
        return ok;
    }

    public bool TryShellForces(FragmentMaskKey key, string elemTag, out FragmentShellForces forces)
    {
        bool ok = _shell.TryGetValue((key, elemTag), out var f);
        forces = f!;
        return ok;
    }

    public IReadOnlyList<FragmentBeamSpanLoad> BeamLoads(FragmentMaskKey key, string elemTag) =>
        _loads.TryGetValue((key, elemTag), out var list) ? list : [];
}
