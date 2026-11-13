using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Fragment;
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

    /// <summary>
    /// Импортный АЖТ колонны с ведомым узлом стены вне плиты (в проёме; «Музей», АЖТ 126056 у колонны 80: углы стен 56/57)
    /// — след по сечению для плиты и тело «по точкам» с тем же ведущим для узла стены; ведомые в плите туда не входят.
    /// </summary>
    [Fact]
    public void ImportedColumnBody_SlaveOffSlab_TransferredByPointsWithSameMaster()
    {
        var slab = Slab("S1", 1, 3, Box(0.45, -0.5, 1.5, 0.5, "h"));
        var wall = Wall("W1", 2, 0.5, 0, 0, 1, 6);
        FemNode[] nodes = [N("1", 0, 0, 0), N("2", 0, 0, 6)];
        (FemMember, CScore.Planar.PlanarRegion?)[] members = [(Bar("C1", "1", "2"), null), slab, wall];
        FemMeshNode[] mesh = [.. Body, M("505", 0.5, 0, 3)];
        var body = new FemRigidBody { MasterNodeTag = "500", Origin = "import:scad", SourceElemTag = "126056" };
        body.SetSlaveNodeTags(["501", "502", "503", "504", "505"]);
        var d = Build(1.5, 4.5, nodes, members, mesh, [CoveredElement("S1")], [body]);

        Assert.All(d.Members.Where(m => m.Member.ElemType == "beam"), m => Assert.NotNull(m.Member.RigidJunctionTraceJson));
        var b = Assert.Single(d.NodeRigidBodies);
        var at = d.Nodes.ToDictionary(n => n.NodeTag);
        Assert.Equal((0.0, 0.0, 3.0), (at[b.MasterNodeTag].X, at[b.MasterNodeTag].Y, at[b.MasterNodeTag].Z));
        var slave = at[Assert.Single(b.SlaveNodeTags)];
        Assert.Equal((0.5, 0.0, 3.0), (slave.X, Math.Round(slave.Y, 12), slave.Z));
        Assert.Equal("import:scad", b.Origin);
        Assert.Contains(d.Diagnostics, x => x.Code == "fragment_rigid_body_trace_points" && x.SourceKeys!.SequenceEqual(["126056"]));
    }

    /// <summary>
    /// Импортный АЖТ на конце горизонтального ребра в плоскости плиты с одним ведомым в плите («Музей», АЖТ 126043: конец
    /// ребра 1870 и верх стены в 0,1 м) — площадки нет, а ребро не колонна: тело переносится «по точкам», след ребру не
    /// задаётся (раньше тело терялось: ребро без сечения с контуром следа не строит).
    /// </summary>
    [Fact]
    public void ImportedRibBody_Degenerate_TransferredByPoints()
    {
        var (nodes, members) = Column();
        FemNode[] withRib = [.. nodes, N("3", 2, 1, 3), N("4", 2.5, 1, 3)];
        (FemMember, CScore.Planar.PlanarRegion?)[] all = [.. members, (Bar("R1", "3", "4"), null)];
        FemMeshNode[] mesh = [M("600", 2, 1, 3), M("601", 2, 1.1, 3)];
        var body = new FemRigidBody { MasterNodeTag = "600", Origin = "import:scad", SourceElemTag = "126043" };
        body.SetSlaveNodeTags(["601"]);
        var d = Build(1.5, 4.5, withRib, all, mesh, [CoveredElement("S1")], [body]);

        var b = Assert.Single(d.NodeRigidBodies);
        var at = d.Nodes.ToDictionary(n => n.NodeTag);
        Assert.Equal((2.0, 1.0, 3.0), (at[b.MasterNodeTag].X, at[b.MasterNodeTag].Y, at[b.MasterNodeTag].Z));
        var slave = at[Assert.Single(b.SlaveNodeTags)];
        Assert.Equal((2.0, 1.1, 3.0), (slave.X, Math.Round(slave.Y, 12), slave.Z));
        Assert.All(d.Members.Where(m => m.Member.ElemType == "beam"), m => Assert.Null(m.Member.RigidJunctionTraceJson));
        Assert.Contains(d.Diagnostics, x => x.Code == "fragment_rigid_body_points" && x.SourceKeys!.SequenceEqual(["126043"]));
        Assert.DoesNotContain(d.Diagnostics, x => x.Code == "fragment_rigid_body_degenerate");
    }

    /// <summary>
    /// Импортный АЖТ на конце ребра в плоскости плиты с невырожденной площадкой ведомых — тоже «по точкам»: сетка явный
    /// след у ребра в плоскости игнорирует (<c>rigid_junction_rib_trace_ignored</c>), и тело терялось бы молча. След —
    /// только телу со стержнем вне плоскости плиты (колонна).
    /// </summary>
    [Fact]
    public void ImportedRibBody_WithPatch_TransferredByPoints()
    {
        var (nodes, members) = Column();
        FemNode[] withRib = [.. nodes, N("3", 2, 1, 3), N("4", 2.5, 1, 3)];
        (FemMember, CScore.Planar.PlanarRegion?)[] all = [.. members, (Bar("R1", "3", "4"), null)];
        FemMeshNode[] mesh = [M("600", 2, 1, 3), M("601", 2, 1.1, 3), M("602", 2.1, 1.1, 3), M("603", 2.1, 1, 3)];
        var body = new FemRigidBody { MasterNodeTag = "600", Origin = "import:scad", SourceElemTag = "126099" };
        body.SetSlaveNodeTags(["601", "602", "603"]);
        var d = Build(1.5, 4.5, withRib, all, mesh, [CoveredElement("S1")], [body]);

        var b = Assert.Single(d.NodeRigidBodies);
        var at = d.Nodes.ToDictionary(n => n.NodeTag);
        Assert.Equal((2.0, 1.0, 3.0), (at[b.MasterNodeTag].X, at[b.MasterNodeTag].Y, at[b.MasterNodeTag].Z));
        Assert.Equal(3, b.SlaveNodeTags.Count);
        Assert.All(d.Members.Where(m => m.Member.ElemType == "beam"), m => Assert.Null(m.Member.RigidJunctionTraceJson));
        Assert.Contains(d.Diagnostics, x => x.Code == "fragment_rigid_body_points" && x.SourceKeys!.SequenceEqual(["126099"]));
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

    /// <summary>Голова сваи: ведущий без КЭ, но с закреплением родителя — узел фрагмента; АЖТ переносится по точкам (спека 4а).</summary>
    [Fact]
    public void IsolatedMasterWithSupport_TransferredAsFragmentNode()
    {
        var (nodes, members) = Column();
        var body = new FemRigidBody { MasterNodeTag = "700", Origin = "import:scad", SourceElemTag = "9" };
        body.SetSlaveNodeTags(["501", "502", "503", "504"]);
        var snapshot = new StoreyFragmentParentSnapshot
        {
            Nodes = nodes,
            Members = members.Select(m => m.Item1).ToList(),
            Regions = members.Where(m => m.Item2 is not null).Select(m => m.Item2!).ToList(),
            MeshNodes = [.. Body, M("700", 3, 3, 2)],
            MeshElements = [CoveredElement("S1")],
            RigidBodies = [body],
            MeshNodeSupports = [new FemMeshNodeSupport { NodeTag = "700", Mask = 3 }],
        };
        var d = StoreyFragmentBuilder.Build(snapshot, new StoreyFragmentRequest(1.5, 4.5, "Этаж 2"));

        var b = Assert.Single(d.NodeRigidBodies);
        var master = d.Nodes.Single(n => n.NodeTag == b.MasterNodeTag);
        Assert.Equal((3.0, 3.0, 2.0), (master.X, master.Y, master.Z));
        Assert.Equal(3, master.DofMask);
        Assert.Equal(4, b.SlaveNodeTags.Count);
        Assert.DoesNotContain(d.Diagnostics, x => x.Code == "fragment_rigid_body_lost");
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
