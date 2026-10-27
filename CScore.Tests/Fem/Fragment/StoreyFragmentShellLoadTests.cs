using CScore.Fem;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentLoadFixtures;

namespace CScore.Tests.Fem.Fragment;

/// <summary>Плита S1 6×6 на z = 3, сетка 4×4: КЭ «1» = [−3; −1,5]², теги 1…16 построчно.</summary>
public sealed class StoreyFragmentShellLoadTests
{
    static readonly string[] All = Enumerable.Range(1, 16).Select(i => i.ToString()).ToArray();

    [Fact]
    public void SameUniformOnAllElements_LiftedToMember()
    {
        var d = Build(Snapshot(elementLoads: [ElementLoad(7, "uniform", "z", [-5000], All)]));

        var load = Assert.Single(d.ElementLoads);
        Assert.Equal((FemLoadTargetKinds.Members, "uniform", "global", "z", 7), (load.TargetKind, load.LoadKind, load.CoordinateSystem, load.Axis, load.LoadCaseId));
        Assert.Equal(new[] { "S1" }, load.TargetTags);
        Assert.Equal(new[] { -5000.0 }, load.Values);
        Assert.Empty(d.FieldLoads);
    }

    [Fact]
    public void MemberTarget_AndLocalNormal_LiftedAsGlobal()
    {
        var load = ElementLoad(7, "uniform", "z", [-5000], "S1");
        load.TargetKind = FemLoadTargetKinds.Members;
        load.CoordinateSystem = "local";   // нормаль КЭ сетки — +Z

        var lifted = Assert.Single(Build(Snapshot(elementLoads: [load])).ElementLoads);

        Assert.Equal(("global", "z", -5000.0), (lifted.CoordinateSystem, lifted.Axis, lifted.Values[0]));
    }

    [Fact]
    public void DifferentUniform_BecomesField()
    {
        var d = Build(Snapshot(elementLoads:
            [ElementLoad(7, "uniform", "z", [-5000], All[1..]), ElementLoad(7, "uniform", "z", [-8000], "1")]));

        Assert.Empty(d.ElementLoads);
        var f = Assert.Single(d.FieldLoads);
        Assert.Equal((7, "uniform", "z"), (f.LoadCaseId, f.LoadKind, f.Axis));
        Assert.Equal(16, f.Entries.Count);
        Assert.Equal(new[] { -8000.0 }, f.Entries.Single(x => x.RefTag == "1").Values);
    }

    [Fact]
    public void NodalAndEdge_BecomeField_EqualNodal_Lifted()
    {
        var d = Build(Snapshot(loadCases: [Lc(7, "П"), Lc(8, "В")], elementLoads:
        [
            ElementLoad(7, "nodal", "z", [1, 2, 3, 4], "1"),
            ElementLoad(7, "edge", "z", [1, 2, 100, 200], "2"),
            ElementLoad(8, "nodal", "z", [5, 5, 5, 5], All),
        ]));

        Assert.Equal(new[] { 1.0, 2, 3, 4 }, Assert.Single(d.FieldLoads, f => f.LoadKind == "nodal").Entries.Single().Values);
        Assert.Equal(new[] { 1.0, 2, 100, 200 }, Assert.Single(d.FieldLoads, f => f.LoadKind == "edge").Entries.Single().Values);
        var lifted = Assert.Single(d.ElementLoads);
        Assert.Equal((8, 5.0), (lifted.LoadCaseId, lifted.Values[0]));
    }

    [Fact]
    public void ShellPoint_BecomesNodeLoadAtPoint()
    {
        var d = Build(Snapshot(elementLoads: [ElementLoad(7, "point", "z", [-700, 0.5, 0.25], "1")]));

        var nl = Assert.Single(d.NodeLoads);
        var node = d.Nodes.Single(n => n.NodeTag == nl.NodeTag);
        Assert.Equal(-2.5, node.X, 9);
        Assert.Equal(-2.75, node.Y, 9);
        Assert.Equal((3.0, -700.0), (node.Z, nl.Load.Fz));
        Assert.Empty(d.FieldLoads);
    }

    [Fact]
    public void SelfWeight_SameK_Lifted_MixedK_Warned()
    {
        var same = Build(Snapshot(elementLoads: [ElementLoad(7, "self_weight", "z", [1.1], All)]));
        var sw = Assert.Single(same.ElementLoads);
        Assert.Equal(("self_weight", 1.1, "S1"), (sw.LoadKind, sw.Values[0], sw.TargetTags[0]));

        var mixed = Build(Snapshot(elementLoads: [ElementLoad(7, "self_weight", "z", [1.1], All[1..]), ElementLoad(7, "self_weight", "z", [1.3], "1")]));
        Assert.Empty(mixed.ElementLoads);
        Assert.Contains(mixed.Diagnostics, x => x.Code == "fragment_self_weight_mixed" && !x.IsError);
    }
}
