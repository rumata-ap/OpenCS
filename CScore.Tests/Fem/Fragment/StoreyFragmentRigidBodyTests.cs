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
    public void BodyAwayFromColumn_InSlab_TransferredByPoints()
    {
        var (nodes, members) = Column();
        var far = Body.Select(n => M(n.NodeTag, n.X + 2, n.Y, n.Z)).ToArray();
        var d = Build(1.5, 4.5, nodes, members, far, [CoveredElement("S1")], [Rigid("import:scad")]);

        var body = Assert.Single(d.NodeRigidBodies);
        Assert.Equal("77", body.SourceElemTag);
        Assert.Equal("import:scad", body.Origin);
        Assert.Equal(4, body.SlaveNodeTags.Count);
        var master = d.Nodes.Single(n => n.NodeTag == body.MasterNodeTag);
        Assert.Equal((2.0, 0.0, 3.0), (master.X, master.Y, master.Z));
        Assert.Contains(d.Diagnostics, x => x.Code == "fragment_rigid_body_points" && x.SourceKeys!.SequenceEqual(["77"]));
        Assert.DoesNotContain(d.Diagnostics, x => x.Code == "fragment_rigid_body_lost");
    }

    /// <summary>Колонна у стены: ведущий на оси колонны, ведомые в стене; ведомый выше полосы отбрасывается.</summary>
    [Fact]
    public void ColumnAtWall_ByPoints_SlaveAboveBandDropped()
    {
        var (nodes, members) = Column();
        var wall = Wall("W1", 2, -3, 0.4, 0, 6, 6);
        FemMeshNode[] mesh = [M("600", 0, 0, 2), M("601", 0, 0.4, 2), M("602", 0, 0.4, 2.45), M("603", 0, 0.4, 5)];
        var body = new FemRigidBody { MasterNodeTag = "600", Origin = "import:scad", SourceElemTag = "125938" };
        body.SetSlaveNodeTags(["601", "602", "603"]);
        var d = Build(1.5, 4.5, nodes, [.. members, wall], mesh, [CoveredElement("S1")], [body]);

        var b = Assert.Single(d.NodeRigidBodies);
        var at = d.Nodes.ToDictionary(n => n.NodeTag);
        Assert.Equal((0.0, 0.0, 2.0), (at[b.MasterNodeTag].X, at[b.MasterNodeTag].Y, at[b.MasterNodeTag].Z));
        Assert.Equal([2.0, 2.45], b.SlaveNodeTags.Select(t => at[t].Z));
        Assert.All(b.SlaveNodeTags, t => Assert.Equal(0.4, at[t].Y, 9));
        Assert.Contains(d.Diagnostics, x => x.Code == "fragment_rigid_body_slaves_dropped" && x.SourceKeys!.SequenceEqual(["125938"]));
    }

    [Fact]
    public void MasterOrAllSlavesOutsideFragment_Lost_OneRowPerReason()
    {
        var (nodes, members) = Column();
        // Ведущий в воздухе (не на стержне и не в пластине) — два тела; ведомые в воздухе — одно.
        FemMeshNode[] mesh = [M("700", 4, 4, 2), M("701", 0, 0, 2), M("702", 5, 5, 2), M("703", 4, 4, 2.5)];
        FemRigidBody B(string master, string source, string slave)
        {
            var b = new FemRigidBody { MasterNodeTag = master, Origin = "import:scad", SourceElemTag = source };
            b.SetSlaveNodeTags([slave]);
            return b;
        }
        var d = Build(1.5, 4.5, nodes, members, mesh, [CoveredElement("S1")],
            [B("700", "12", "701"), B("703", "5", "701"), B("701", "8", "702")]);

        Assert.Empty(d.NodeRigidBodies);
        var lost = d.Diagnostics.Where(x => x.Code == "fragment_rigid_body_lost").ToList();
        Assert.Equal(2, lost.Count);
        Assert.Equal(["5", "12"], lost[0].SourceKeys);
        Assert.Contains("ведущий", lost[0].Message);
        Assert.Equal(["8"], lost[1].SourceKeys);
        // Узлы для непереносимых тел не создаются.
        Assert.DoesNotContain(d.Nodes, n => n.X == 4 && n.Y == 4);
    }

    /// <summary>Узлы сетки источника в миллиметрах от оси КонЭ/плоскости стены — привязываются проекцией; дальше
    /// <see cref="CScore.Fem.Fragment.StoreyFragmentBuilder.SourceSnapM"/> — нет.</summary>
    [Fact]
    public void PointsMillimetresOff_SnappedOntoAxisAndWallPlane()
    {
        var (nodes, members) = Column();
        var wall = Wall("W1", 2, -3, 0.4, 0, 6, 6);
        FemMeshNode[] mesh = [M("900", 0.003, 0, 2), M("901", 0.5, 0.404, 2), M("902", 0.02, 0, 2.5)];
        var near = new FemRigidBody { MasterNodeTag = "900", Origin = "import:scad", SourceElemTag = "1" };
        near.SetSlaveNodeTags(["901"]);
        var far = new FemRigidBody { MasterNodeTag = "902", Origin = "import:scad", SourceElemTag = "2" };
        far.SetSlaveNodeTags(["901"]);
        var d = Build(1.5, 4.5, nodes, [.. members, wall], mesh, [CoveredElement("S1")], [near, far]);

        var b = Assert.Single(d.NodeRigidBodies);
        var at = d.Nodes.ToDictionary(n => n.NodeTag);
        Assert.Equal((0.0, 0.0, 2.0), (at[b.MasterNodeTag].X, at[b.MasterNodeTag].Y, at[b.MasterNodeTag].Z));
        var slave = at[Assert.Single(b.SlaveNodeTags)];
        Assert.Equal((0.5, 0.4, 2.0), (slave.X, Math.Round(slave.Y, 12), slave.Z));
        Assert.Equal(["2"], d.Diagnostics.Single(x => x.Code == "fragment_rigid_body_lost").SourceKeys);
    }

    [Fact]
    public void LiraCoupledGroup_KeepsKindAndMask()
    {
        var (nodes, members) = Column();
        var wall = Wall("W1", 2, -3, 0.4, 0, 6, 6);
        FemMeshNode[] mesh = [M("800", 1, 0.4, 2), M("801", 1, 0.4, 2.5), M("802", 1, 0.4, 3.5)];
        var body = new FemRigidBody { MasterNodeTag = "800", Origin = "import:lira", SourceElemTag = "3", Kind = FemRigidBodyKinds.Coupled, Mask = 0b000100 };
        body.SetSlaveNodeTags(["801", "802"]);
        var d = Build(1.5, 4.5, nodes, [.. members, wall], mesh, [CoveredElement("S1")], [body]);

        var b = Assert.Single(d.NodeRigidBodies);
        Assert.True(b.IsCoupled);
        Assert.Equal(0b000100, b.Mask);
        Assert.Equal(2, b.SlaveNodeTags.Count);
        Assert.Contains("ЛИРЫ", d.Diagnostics.Single(x => x.Code == "fragment_rigid_body_points").Message);
    }
}
