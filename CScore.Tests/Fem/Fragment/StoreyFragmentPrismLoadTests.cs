using CScore.Fem;
using CScore.Fem.Fragment;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentLoadFixtures;

namespace CScore.Tests.Fem.Fragment;

/// <summary>Плита S1 6×6 на z = 3, сетка 4×4 (КЭ 1,5 м), колонна C1 в (0, 0); призма 2 × 2 вокруг колонны.</summary>
public sealed class StoreyFragmentPrismLoadTests
{
    static readonly string[] All = Enumerable.Range(1, 16).Select(i => i.ToString()).ToArray();

    static StoreyFragmentDraft BuildPrism(StoreyFragmentParentSnapshot parent) =>
        StoreyFragmentBuilder.Build(parent, new StoreyFragmentRequest(1.5, 4.5, "Фрагмент", Prism: StoreyFragmentPrismFixtures.Square(1)));

    [Fact]
    public void UniformOnAllElements_LiftedToClippedSlab_ResultantsMatch()
    {
        var d = BuildPrism(Snapshot(elementLoads: [ElementLoad(7, "uniform", "z", [-5000], All)]));

        Assert.False(d.HasErrors);
        var load = Assert.Single(d.ElementLoads);
        Assert.Equal(new[] { "S1" }, load.TargetTags);
        Assert.Equal(4, StoreyFragmentFixtures.Area(d.Members.Single(m => m.Member.ElemTag == "S1").Region!), 6);
        Assert.DoesNotContain(d.Diagnostics, x => x.Code == "fragment_load_resultant_mismatch");
        // Опорная сетка — только КЭ, заходящие в призму: 4 КЭ плиты вокруг колонны + 2 КЭ колонны в полосе.
        Assert.Equal(4, d.RefElements.Count(e => e.ElemType == "shell"));
    }

    [Fact]
    public void DifferentUniform_Field_ResultantsMatch()
    {
        var d = BuildPrism(Snapshot(elementLoads:
            [ElementLoad(7, "uniform", "z", [-5000], All.Except(["6"]).ToArray()), ElementLoad(7, "uniform", "z", [-8000], "6")]));

        Assert.Single(d.FieldLoads);
        Assert.DoesNotContain(d.Diagnostics, x => x.Code == "fragment_load_resultant_mismatch");
    }

    [Fact]
    public void NodeLoadsAndSupportsOutsidePrism_NotTransferred()
    {
        // Узел 1013 — (0, 0, 3) внутри призмы, 1007 — (−1,5; −1,5; 3) снаружи.
        var s = Snapshot(meshNodeLoads:
        [
            new FemMeshNodeLoad { LoadCaseId = 7, MeshNodeTag = "1013", Fz = -1000 },
            new FemMeshNodeLoad { LoadCaseId = 7, MeshNodeTag = "1007", Fz = -500 },
        ]);
        var d = BuildPrism(new StoreyFragmentParentSnapshot
        {
            Nodes = [.. s.Nodes, new FemNode { Id = 20, NodeTag = "20", X = 2, Y = 2, Z = 3, DofMask = 63 }],
            Members = s.Members, Regions = s.Regions, MeshNodes = s.MeshNodes, MeshElements = s.MeshElements,
            LoadCases = s.LoadCases, MeshNodeLoads = s.MeshNodeLoads,
        });

        var nl = Assert.Single(d.NodeLoads);
        Assert.Equal(-1000, nl.Load.Fz);
        Assert.DoesNotContain(d.Nodes, n => n.X == 2 && n.Y == 2);
    }
}
