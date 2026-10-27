using CScore.Fem;
using CScore.Fem.Fragment;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentFixtures;
using static CScore.Tests.Fem.Fragment.StoreyFragmentLoadFixtures;

namespace CScore.Tests.Fem.Fragment;

/// <summary>Отбор КонЭ по полосе и отпечаток снимка родителя (срез 3).</summary>
public class StoreyFragmentBandTests
{
    static readonly StoreyFragmentRequest Band = new(1.5, 4.5, "Этаж 2");

    /// <summary>Родитель фикстуры + колонна C0 ниже полосы (узлы 3–4), плита S9 на z = 9, нагрузки на них.</summary>
    static StoreyFragmentParentSnapshot Wide(int lcId = 7, double q = -5000, double nodeZ2 = 6)
    {
        var baseSnap = Snapshot(loadCases: [Lc(lcId, "Полезная")]);
        var c0 = Bar("C0", "3", "4");
        c0.Id = 2;
        var (s9, r9) = Slab("S9", 9, 9);
        s9.Id = 3;
        var slab = baseSnap.Members.Single(m => m.ElemTag == "S1");
        slab.Id = 4;
        return new StoreyFragmentParentSnapshot
        {
            Nodes = new[] { N("1", 0, 0, 0), N("2", 0, 0, nodeZ2), N("3", 2, 0, -3), N("4", 2, 0, -0.5) }.Select((n, k) => { n.Id = k + 1; return n; }).ToList(),
            Members = [.. baseSnap.Members, c0, s9],
            Regions = [.. baseSnap.Regions, r9!],
            MeshNodes = baseSnap.MeshNodes,
            MeshElements = baseSnap.MeshElements,
            LoadCases = baseSnap.LoadCases,
            MemberLoads =
            [
                new FemMemberLoad { LoadCaseId = lcId, MemberId = 1, QzStart = q, QzEnd = q, CoordinateSystem = "global" },
                new FemMemberLoad { LoadCaseId = lcId, MemberId = 2, QzStart = -1, QzEnd = -1, CoordinateSystem = "global" },
            ],
            NodeLoads =
            [
                new FemNodeLoad { LoadCaseId = lcId, NodeId = 2, Fz = -10 },
            ],
            ElementLoads = [ElementLoad(lcId, FemElementLoadKinds.Uniform, "z", [q], "1", "2")],
        };
    }

    [Fact]
    public void KeepMembers_DropsMembersOutsideBand()
    {
        var s = Wide();
        var kept = StoreyFragmentBand.KeepMembers(s.Members, s.Nodes, s.Regions, Band).Select(m => m.ElemTag);
        Assert.Equal(["C1", "S1"], kept);
    }

    [Fact]
    public void KeepMembers_SlabOnCutPlane_IsKept()
    {
        var (slab, region) = Slab("S0", 5, 1.5);
        var kept = StoreyFragmentBand.KeepMembers([slab], [], [region!], Band);
        Assert.Single(kept);
    }

    [Fact]
    public void Trim_KeepsOnlyLoadsOfBandMembers_AndBuildsSameDraft()
    {
        var full = Wide();
        var trimmed = StoreyFragmentBand.Trim(full, Band);
        Assert.Equal(["C1", "S1"], trimmed.Members.Select(m => m.ElemTag));
        Assert.Single(trimmed.Regions);
        Assert.Equal(["1", "2"], trimmed.Nodes.Select(n => n.NodeTag));
        Assert.Single(trimmed.MemberLoads);

        var a = StoreyFragmentBuilder.Build(full, Band);
        var b = StoreyFragmentBuilder.Build(trimmed, Band);
        Assert.False(b.HasErrors);
        Assert.Equal(a.Members.Select(m => m.Member.ElemTag), b.Members.Select(m => m.Member.ElemTag));
        Assert.Equal(a.Cuts.Count, b.Cuts.Count);
        Assert.Equal(a.MemberLoads.Count, b.MemberLoads.Count);
        Assert.Equal(a.ElementLoads.Count + a.FieldLoads.Count, b.ElementLoads.Count + b.FieldLoads.Count);
    }

    [Fact]
    public void Fingerprint_IgnoresIdsAndOrder_TracksContent()
    {
        string a = StoreyFragmentFingerprint.Compute(Wide(lcId: 7));
        string b = StoreyFragmentFingerprint.Compute(Wide(lcId: 70));
        Assert.Equal(a, b);

        var reordered = Wide();
        var shuffled = new StoreyFragmentParentSnapshot
        {
            Nodes = reordered.Nodes.Reverse().ToList(), Members = reordered.Members.Reverse().ToList(),
            Regions = reordered.Regions, MeshNodes = reordered.MeshNodes.Reverse().ToList(),
            MeshElements = reordered.MeshElements.Reverse().ToList(), LoadCases = reordered.LoadCases,
            MemberLoads = reordered.MemberLoads.Reverse().ToList(), NodeLoads = reordered.NodeLoads,
            ElementLoads = reordered.ElementLoads,
        };
        Assert.Equal(a, StoreyFragmentFingerprint.Compute(shuffled));

        Assert.NotEqual(a, StoreyFragmentFingerprint.Compute(Wide(q: -5001)));
        Assert.NotEqual(a, StoreyFragmentFingerprint.Compute(Wide(nodeZ2: 6.01)));

        var moved = Wide();
        moved.MeshNodes[0].Z += 0.001;
        Assert.NotEqual(a, StoreyFragmentFingerprint.Compute(moved));

        var factor = Wide();
        factor.LoadCases[0].SelfWeightFactor = 1.1;
        Assert.NotEqual(a, StoreyFragmentFingerprint.Compute(factor));

        var renamed = Wide();
        renamed.Members[0].ElemTag = "C1a";
        Assert.NotEqual(a, StoreyFragmentFingerprint.Compute(renamed));
    }
}
