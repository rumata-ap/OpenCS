using CScore.Fem;
using Xunit;

namespace CScore.Tests.Fem;

public sealed class FemNodeRigidBodiesTests
{
    static FemNode N(string tag, double x, double y, double z) => new() { NodeTag = tag, X = x, Y = y, Z = z };

    static FemMeshNode M(string tag, double x, double y, double z, string? source = null) =>
        new() { NodeTag = tag, X = x, Y = y, Z = z, SourceNodeTag = source };

    static FemRigidBody Body(string master, string source, params string[] slaves)
    {
        var b = new FemRigidBody { MasterNodeTag = master, Origin = "import:scad", SourceElemTag = source, Mask = 63 };
        b.SetSlaveNodeTags(slaves);
        return b;
    }

    static readonly FemNode[] Nodes = [N("1", 0, 0, 3), N("2", 0.3, 0, 3), N("3", 0.3, 0, 3.45), N("4", 5, 5, 5)];

    [Fact]
    public void Maps_BySourceTagAndByCoordinates()
    {
        // Узел 1 — по SourceNodeTag (узел стержня), 2 и 3 — по координатам (узлы пластины в пределах 1 мм).
        FemMeshNode[] mesh = [M("10", 0, 0, 3, "1"), M("20", 0.3004, 0, 3), M("30", 0.3, 0.0003, 3.45)];
        var r = FemNodeRigidBodies.Materialize(mesh, Nodes, [Body("1", "125938", "2", "3")], new HashSet<string>(), []);

        var b = Assert.Single(r.Bodies);
        Assert.Equal("10", b.MasterNodeTag);
        Assert.Equal(["20", "30"], b.SlaveNodeTags);
        Assert.Equal(FemNodeRigidBodies.Provenance, b.Origin);
        Assert.Equal("125938", b.SourceElemTag);
        Assert.Equal(FemRigidBodyKinds.Rigid, b.Kind);
        Assert.Empty(r.Diagnostics);
    }

    [Fact]
    public void UnmeshedSlave_Dropped_BodyKept_UnmeshedMaster_BodyDropped()
    {
        FemMeshNode[] mesh = [M("10", 0, 0, 3, "1"), M("20", 0.3, 0, 3)];
        var r = FemNodeRigidBodies.Materialize(mesh, Nodes,
            [Body("1", "A", "2", "4"), Body("4", "B", "2")], new HashSet<string>(), []);

        var b = Assert.Single(r.Bodies);
        Assert.Equal("A", b.SourceElemTag);
        Assert.Equal(["20"], b.SlaveNodeTags);
        Assert.Contains(r.Diagnostics, d => d.Code == "node_rigid_point_unmeshed" && d.SourceKeys!.Contains("4"));
        Assert.Contains(r.Diagnostics, d => d.Code == "node_rigid_body_dropped" && d.SourceKeys!.SequenceEqual(["B"]));
    }

    [Fact]
    public void BlockedAndBusySlaves_Dropped_MasterSlaveOfOther_BodyDropped()
    {
        FemMeshNode[] mesh = [M("10", 0, 0, 3, "1"), M("20", 0.3, 0, 3), M("30", 0.3, 0, 3.45)];
        var junction = new FemRigidBody { MasterNodeTag = "99", Origin = RigidJunctionTraceSet.Provenance };
        junction.SetSlaveNodeTags(["30"]);

        // Ведомый 20 закреплён, 30 ведом телом примыкания → без ведомых тело не создаётся.
        var r = FemNodeRigidBodies.Materialize(mesh, Nodes, [Body("1", "A", "2", "3")], new HashSet<string> { "20" }, [junction]);
        Assert.Empty(r.Bodies);
        Assert.Contains(r.Diagnostics, d => d.Code == "node_rigid_slave_blocked" && d.SourceKeys!.SequenceEqual(["20", "30"]));

        // Ведущий (узел 3 → 30) ведом телом примыкания → тело не создаётся.
        var r2 = FemNodeRigidBodies.Materialize(mesh, Nodes, [Body("3", "C", "2")], new HashSet<string>(), [junction]);
        Assert.Empty(r2.Bodies);
        Assert.Contains(r2.Diagnostics, d => d.Code == "node_rigid_body_dropped");
    }

    [Fact]
    public void SecondBody_CannotReuseSlave_NorChainOnIt()
    {
        FemMeshNode[] mesh = [M("10", 0, 0, 3, "1"), M("20", 0.3, 0, 3), M("30", 0.3, 0, 3.45)];
        var r = FemNodeRigidBodies.Materialize(mesh, Nodes,
            [Body("1", "A", "2"), Body("3", "B", "2"), Body("2", "C", "3")], new HashSet<string>(), []);

        Assert.Equal(["A"], r.Bodies.Select(b => b.SourceElemTag));
        Assert.Contains(r.Diagnostics, d => d.Code == "node_rigid_body_dropped" && d.SourceKeys!.SequenceEqual(["B", "C"]));
    }

    [Fact]
    public void CoupledKindAndMask_Kept_SlaveAtMaster_Ignored()
    {
        FemMeshNode[] mesh = [M("10", 0, 0, 3, "1"), M("20", 0.3, 0, 3)];
        var body = Body("1", "7", "1", "2");
        body.Kind = FemRigidBodyKinds.Coupled;
        body.Mask = 0b000111;
        var b = Assert.Single(FemNodeRigidBodies.Materialize(mesh, Nodes, [body], new HashSet<string>(), []).Bodies);
        Assert.True(b.IsCoupled);
        Assert.Equal(0b000111, b.Mask);
        Assert.Equal(["20"], b.SlaveNodeTags);
    }
}
