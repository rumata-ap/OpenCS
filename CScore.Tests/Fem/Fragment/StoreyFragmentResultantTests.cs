using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Loads;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentLoadFixtures;

namespace CScore.Tests.Fem.Fragment;

public sealed class StoreyFragmentResultantTests
{
    const string Mismatch = "fragment_load_resultant_mismatch";

    [Fact]
    public void TrialMesh_CoversSlabAndBars()
    {
        var d = Build(Snapshot(meshSize: 0.5));
        var (nodes, elements) = FragmentTrialMesh.Build(d);
        var mesh = new FemLoadMeshContext(nodes, elements);

        var shells = elements.Where(e => e.SourceMemberTag == "S1").Select(e => mesh.Geometry(e)!).ToList();
        Assert.Equal(36, shells.Sum(g => g.Area), 9);
        Assert.All(shells, g => Assert.True(Enumerable.Range(0, 3).Max(k => (g.Points[k] - g.Points[(k + 1) % 3]).Length) <= 0.5 + 1e-9));
        Assert.Equal(3, elements.Where(e => e.ElemType == "beam").Sum(e => mesh.Geometry(e)!.Length), 9);
    }

    [Fact]
    public void LiftedUniform_BarAndNodeLoads_NoMismatch()
    {
        var d = Build(Snapshot(
            elementLoads: [ElementLoad(7, "uniform", "z", [-5000], Enumerable.Range(1, 16).Select(i => i.ToString()).ToArray()),
                           ElementLoad(7, "trapezoid", "x", [0, 0, 1500, 1.5], "503")],
            memberLoads: [new FemMemberLoad { LoadCaseId = 7, MemberId = 1, CoordinateSystem = "local", DistributionType = "trapezoidal", QyStart = 100, QyEnd = 700 }],
            meshNodeLoads: [new FemMeshNodeLoad { LoadCaseId = 7, MeshNodeTag = "1007", Fz = -1000, Mx = 50 }]));

        Assert.False(d.HasErrors);
        Assert.DoesNotContain(d.Diagnostics, x => x.Code == Mismatch);
    }

    [Fact]
    public void Field_FineAlignedTrialMesh_NoMismatch()
    {
        // Шаг 0,215 → диагональ 6√2 делится на 40: ячейки 0,15 ложатся на сетку родителя 1,5 точно.
        var d = Build(Snapshot(meshSize: 0.215, elementLoads: [ElementLoad(7, "uniform", "z", [-10000], "1")]));

        Assert.Single(d.FieldLoads);
        Assert.DoesNotContain(d.Diagnostics, x => x.Code == Mismatch);
    }

    [Fact]
    public void Field_CoarseTrialMesh_Mismatch()
    {
        var d = Build(Snapshot(meshSize: 3, elementLoads: [ElementLoad(7, "uniform", "z", [-10000], "1")]));

        var w = Assert.Single(d.Diagnostics, x => x.Code == Mismatch);
        Assert.False(w.IsError);
        Assert.Equal(new[] { "Полезная" }, w.SourceKeys);
        Assert.Contains("пластины: равномерная", w.Message);
    }
}
