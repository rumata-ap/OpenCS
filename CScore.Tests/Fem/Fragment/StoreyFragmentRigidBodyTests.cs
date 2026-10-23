using System.Text.Json;
using CScore.Fem;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentFixtures;

namespace CScore.Tests.Fem.Fragment;

public sealed class StoreyFragmentRigidBodyTests
{
    static FemMeshNode M(string tag, double x, double y, double z) => new() { NodeTag = tag, X = x, Y = y, Z = z };

    static readonly FemMeshNode[] Body =
        [M("500", 0, 0, 3), M("501", 0.3, 0.3, 3), M("502", -0.3, 0.3, 3), M("503", -0.3, -0.3, 3), M("504", 0.3, -0.3, 3)];

    static FemRigidBody Rigid(string origin)
    {
        var body = new FemRigidBody { MasterNodeTag = "500", Origin = origin, SourceElemTag = "77" };
        body.SetSlaveNodeTags(["501", "502", "503", "504"]);
        return body;
    }

    static (FemNode[] Nodes, (FemMember, CScore.Planar.PlanarRegion?)[] Members) Column() =>
        ([N("1", 0, 0, 0), N("2", 0, 0, 6)], [(Bar("C1", "1", "2"), null), Slab("S1", 1, 3)]);

    [Fact]
    public void ImportedBody_BecomesExplicitTraceOnBothParts()
    {
        var (nodes, members) = Column();
        var d = Build(1.5, 4.5, nodes, members, Body, [CoveredElement("S1")], [Rigid("import:lira")]);

        var bars = d.Members.Where(m => m.Member.ElemType == "beam").ToList();
        Assert.Equal(2, bars.Count);
        foreach (var bar in bars)
        {
            var pts = JsonSerializer.Deserialize<double[][]>(bar.Member.RigidJunctionTraceJson!)!;
            Assert.Equal(4, pts.Length);
            Assert.All(pts, p => Assert.Equal(3, p[2], 9));
            var poly = pts.Select(p => new CScore.Planar.PlanarPoint2D(p[0], p[1])).ToList();
            Assert.Equal(0.36, Math.Abs(CScore.Planar.PlanarPolygonOps.SignedArea(poly)), 9);
        }
    }

    [Fact]
    public void ManualBody_ReportedLost_AutoBodySilent()
    {
        var (nodes, members) = Column();
        var manual = Build(1.5, 4.5, nodes, members, Body, [CoveredElement("S1")], [Rigid(FemLoadOrigin.Manual)]);
        Assert.Contains(manual.Diagnostics, x => x.Code == "fragment_rigid_body_lost");
        Assert.All(manual.Members, m => Assert.Null(m.Member.RigidJunctionTraceJson));

        var auto = Build(1.5, 4.5, nodes, members, Body, [CoveredElement("S1")], [Rigid("auto:junction")]);
        Assert.DoesNotContain(auto.Diagnostics, x => x.Code.StartsWith("fragment_rigid_body", StringComparison.Ordinal));
    }

    [Fact]
    public void BodyAwayFromColumn_ReportedLost()
    {
        var (nodes, members) = Column();
        var far = Body.Select(n => M(n.NodeTag, n.X + 2, n.Y, n.Z)).ToArray();
        var d = Build(1.5, 4.5, nodes, members, far, [CoveredElement("S1")], [Rigid("import:scad")]);
        Assert.Contains(d.Diagnostics, x => x.Code == "fragment_rigid_body_lost");
    }
}
