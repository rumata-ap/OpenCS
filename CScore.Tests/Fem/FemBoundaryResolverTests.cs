using CScore.Fem;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Сведение ГУ обоих уровней схемы к узлам и КЭ сетки.</summary>
public sealed class FemBoundaryResolverTests
{
    static FemMeshNode Mesh(string tag, string? source = null) => new() { NodeTag = tag, SourceNodeTag = source };

    static FemRigidBody Body(string master, int mask, params string[] slaves)
    {
        var body = new FemRigidBody { MasterNodeTag = master, Mask = mask };
        body.SetSlaveNodeTags(slaves);
        return body;
    }

    static FemResolvedBoundary Resolve(IReadOnlyList<FemNode>? nodes = null, IReadOnlyList<FemMeshNode>? mesh = null,
        IReadOnlyList<FemElement>? elements = null, IReadOnlyList<FemMeshNodeSupport>? supports = null,
        IReadOnlyList<FemSpring>? springs = null, IReadOnlyList<FemRigidBody>? bodies = null) =>
        FemBoundaryResolver.Resolve(nodes ?? [], mesh ?? [], elements ?? [], supports ?? [], springs ?? [], bodies ?? []);

    [Fact]
    public void Supports_BothLevels_MergeByMeshNode()
    {
        var r = Resolve(
            nodes: [new FemNode { NodeTag = "A", DofMask = 0b000111 }, new FemNode { NodeTag = "B" }],
            mesh: [Mesh("1", "A"), Mesh("2"), Mesh("3", "B")],
            supports: [new FemMeshNodeSupport { NodeTag = "1", Mask = 0b111000 }, new FemMeshNodeSupport { NodeTag = "2", Mask = 0b000011 }]);

        Assert.Equal(2, r.SupportMasks.Count);
        Assert.Equal(FemBoundaryDofs.All, r.SupportMasks["1"]);
        Assert.Equal(0b000011, r.SupportMasks["2"]);
        Assert.Empty(r.Diagnostics);
    }

    [Fact]
    public void Supports_MissingNodes_AreWarnings()
    {
        var r = Resolve(
            nodes: [new FemNode { NodeTag = "A", DofMask = 7 }],
            mesh: [Mesh("1")],
            supports: [new FemMeshNodeSupport { NodeTag = "99", Mask = 7 }]);

        Assert.Empty(r.SupportMasks);
        Assert.False(r.HasErrors);
        Assert.Equal(["node_support_not_in_mesh", "mesh_support_node_missing"], r.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void Springs_SumPerNode_BothLevels_DropFixedDofs()
    {
        var onNode = new FemSpring { TargetKind = FemSpringTargetKinds.Node, NodeTag = "A", Kz = 1e6 };
        var onMesh = new FemSpring { NodeTag = "1", Kz = 2e6, Kx = 5e5 };
        var fixedOnly = new FemSpring { NodeTag = "2", Kx = 3e5 };
        var r = Resolve(
            mesh: [Mesh("1", "A"), Mesh("2")],
            supports: [new FemMeshNodeSupport { NodeTag = "1", Mask = 0b000001 }, new FemMeshNodeSupport { NodeTag = "2", Mask = 0b000001 }],
            springs: [onNode, onMesh, fixedOnly]);

        var spring = Assert.Single(r.Springs);
        Assert.Equal("1", spring.MeshNodeTag);
        Assert.Equal([0, 0, 3e6, 0, 0, 0], spring.Stiffnesses);
        var warning = Assert.Single(r.Diagnostics);
        Assert.Equal("spring_in_fixed_dof", warning.Code);
        Assert.False(warning.IsError);
        Assert.Equal(["1", "2"], warning.SourceKeys);
    }

    [Fact]
    public void Springs_OnMissingNode_Warning()
    {
        var r = Resolve(mesh: [Mesh("1")],
            springs: [new FemSpring { NodeTag = "7", Kz = 1 }, new FemSpring { TargetKind = FemSpringTargetKinds.Node, NodeTag = "A", Kz = 1 }]);

        Assert.Empty(r.Springs);
        Assert.Equal(["7", "A"], Assert.Single(r.Diagnostics).SourceKeys);
    }

    [Fact]
    public void RigidBodies_MissingAndSelfSlaves_DroppedWithWarnings()
    {
        var r = Resolve(
            mesh: [Mesh("1"), Mesh("2"), Mesh("3")],
            bodies: [Body("1", FemBoundaryDofs.All, "2", "1", "9"), Body("8", FemBoundaryDofs.All, "3"), Body("3", 0b000111, "9")]);

        var body = Assert.Single(r.RigidBodies);
        Assert.Equal(("1", FemBoundaryDofs.All), (body.MasterNodeTag, body.Mask));
        Assert.Equal(["2"], body.SlaveNodeTags);
        Assert.False(r.HasErrors);
        Assert.Equal(["rigid_master_missing", "rigid_slave_missing", "rigid_slave_is_master"], r.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void RigidBodies_SlaveTwiceOrFixed_AreErrors()
    {
        var r = Resolve(
            mesh: [Mesh("1"), Mesh("2"), Mesh("3"), Mesh("4")],
            supports: [new FemMeshNodeSupport { NodeTag = "4", Mask = 0b000100 }, new FemMeshNodeSupport { NodeTag = "1", Mask = FemBoundaryDofs.All }],
            bodies: [Body("1", FemBoundaryDofs.All, "2", "4"), Body("3", 0b000011, "2", "4")]);

        // Ведущий закреплён — допустимо (музей: 601 ведущий на опоре); ведомый 4 закреплён по Z первого тела, второе
        // тело объединяет только X Y — с его закреплением не пересекается.
        Assert.True(r.HasErrors);
        var twice = r.Diagnostics.Single(d => d.Code == "rigid_slave_twice");
        Assert.Equal(["2", "4"], twice.SourceKeys);
        Assert.Equal(["4"], r.Diagnostics.Single(d => d.Code == "rigid_slave_supported").SourceKeys);
    }

    [Fact]
    public void ElementProps_ReleasesOnBeams_C1OnShells()
    {
        var r = Resolve(elements:
        [
            new FemElement { ElemTag = "1", ElemType = "beam", ReleaseI = 0, ReleaseJ = 0b110000 },
            new FemElement { ElemTag = "2", ElemType = "beam", ReleaseI = 0, ReleaseJ = 0 },
            new FemElement { ElemTag = "3", ElemType = "shell", ReleaseI = 0b010000, FoundationC1 = 8.5e6 },
            new FemElement { ElemTag = "4", ElemType = "beam", FoundationC1 = 1e6 },
            new FemElement { ElemTag = "5", ElemType = "shell", FoundationC1 = -1 },
        ]);

        Assert.Equal((0, 0b110000), Assert.Single(r.Releases).Value);
        Assert.Equal(8.5e6, r.FoundationC1["3"]);
        Assert.Single(r.FoundationC1);
        Assert.False(r.HasErrors);
        Assert.Equal(["release_not_beam", "foundation_not_shell", "foundation_negative"], r.Diagnostics.Select(d => d.Code));
    }
}
