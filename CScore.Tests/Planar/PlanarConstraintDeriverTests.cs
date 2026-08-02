using System.Text.Json;
using CScore.Fem;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public sealed class PlanarConstraintDeriverTests
{
    [Fact]
    public void Derive_TransformsForeignNodeToLocalPointWithoutMutatingRegion()
    {
        var region = Region();
        var fingerprint = region.GeometryFingerprint;
        var topology = new FemSchemaTopology(
            1,
            [
                Node(1, 10, 21, 32),
                Node(2, 11, 21, 32)
            ],
            [HostMember(region.Id), Member(10, "bar", "beam", 1, 2)],
            []);
        region.Frame = new Frame3D(
            new PlanarVector3(10, 20, 30),
            new PlanarVector3(0, 1, 0),
            new PlanarVector3(0, 0, 1),
            new PlanarVector3(1, 0, 0));

        var result = PlanarConstraintDeriver.Derive(topology, region, new());

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics));
        var point = Assert.Single(result.Constraints, c => c.Geometry.Kind == PlanarConstraintGeometryKind.Point);
        Assert.Equal(new PlanarPoint2D(1, 2), point.Geometry.Points[0]);
        Assert.True(point.IsDerived);
        Assert.Equal(PlanarStructuralKind.EmbeddedMember, point.StructuralFacet.Kind);
        Assert.Equal(PlanarDofMask.UX | PlanarDofMask.UY | PlanarDofMask.UZ |
                     PlanarDofMask.RX | PlanarDofMask.RY | PlanarDofMask.RZ, point.DofMask);
        Assert.NotEmpty(point.SourceReferences);
        Assert.NotEmpty(point.StructuralRelations);
        Assert.Equal(fingerprint, region.GeometryFingerprint);
        Assert.Empty(region.ConstraintObjects);
    }

    [Fact]
    public void Derive_ExcludesHostShellMemberAndItsMeshElements()
    {
        var region = Region();
        var topology = new FemSchemaTopology(
            1,
            [Node(1, 1, 1, 0), Node(2, 3, 1, 0), Node(3, 3, 3, 0), Node(4, 1, 3, 0)],
            [HostMember(region.Id)],
            [Element(500, "host-e1", "shell", "host", 1, 2, 3, 4)]);

        var result = PlanarConstraintDeriver.Derive(topology, region, new());

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Empty(result.Constraints);
    }

    [Fact]
    public void Derive_RecognizesTransverseBarAsPoint()
    {
        var topology = new FemSchemaTopology(
            1,
            [Node(1, 2, 1, -1), Node(2, 2, 1, 1)],
            [HostMember(77), Member(10, "bar", "beam", 1, 2)],
            []);

        var result = PlanarConstraintDeriver.Derive(topology, Region(), new());

        var point = Assert.Single(result.Constraints, c => c.Geometry.Kind == PlanarConstraintGeometryKind.Point);
        Assert.Equal(new PlanarPoint2D(2, 1), point.Geometry.Points[0]);
        Assert.Equal(PlanarDofMask.UX | PlanarDofMask.UY | PlanarDofMask.UZ, point.DofMask);
        Assert.Equal(PlanarMeshKind.EmbeddedPoint, point.MeshFacet.Kind);
    }

    [Fact]
    public void Derive_RecognizesCoplanarBarAsCurve()
    {
        var topology = new FemSchemaTopology(
            1,
            [Node(1, 0.5, 2, 0), Node(2, 3.5, 2, 0)],
            [HostMember(77), Member(10, "bar", "beam", 1, 2)],
            []);

        var result = PlanarConstraintDeriver.Derive(topology, Region(), new());

        var curve = Assert.Single(result.Constraints, c => c.Geometry.Kind == PlanarConstraintGeometryKind.Curve);
        Assert.Equal([new PlanarPoint2D(0.5, 2), new PlanarPoint2D(3.5, 2)], curve.Geometry.Points);
        Assert.Equal(PlanarMeshKind.ConformingPartition, curve.MeshFacet.Kind);
        Assert.Equal(PlanarDofMask.UX | PlanarDofMask.UY | PlanarDofMask.UZ, curve.DofMask);
    }

    [Fact]
    public void Derive_IntersectsWallShellAndMergesAdjacentElements()
    {
        var topology = new FemSchemaTopology(
            1,
            [
                Node(1, 1, 1, -1), Node(2, 3, 1, -1), Node(3, 3, 1, 1), Node(4, 1, 1, 1),
                Node(5, 3, 1, -1), Node(6, 4, 1, -1), Node(7, 4, 1, 1), Node(8, 3, 1, 1)
            ],
            [HostMember(77), Member(20, "wall", "shell")],
            [
                Element(201, "wall-e1", "shell", "wall", 1, 2, 3, 4),
                Element(202, "wall-e2", "shell", "wall", 5, 6, 7, 8)
            ]);

        var result = PlanarConstraintDeriver.Derive(topology, Region(), new());

        var curve = Assert.Single(result.Constraints, c => c.Geometry.Kind == PlanarConstraintGeometryKind.Curve);
        Assert.Equal([new PlanarPoint2D(1, 1), new PlanarPoint2D(3, 1), new PlanarPoint2D(4, 1)], curve.Geometry.Points);
        Assert.Equal([201, 202], curve.SourceReferences.SelectMany(r => r.ElementIds).OrderBy(id => id));
        Assert.Equal(PlanarDofMask.UX | PlanarDofMask.UY | PlanarDofMask.UZ, curve.DofMask);
    }

    [Fact]
    public void Derive_DeduplicatesSharedNodeAndRetainsBothRelations()
    {
        var topology = new FemSchemaTopology(
            1,
            [Node(1, 2, 2, 0), Node(2, 0, 2, 0), Node(3, 4, 2, 0)],
            [HostMember(77), Member(10, "left", "beam", 1, 2), Member(11, "right", "beam", 1, 3)],
            []);

        var result = PlanarConstraintDeriver.Derive(topology, Region(), new());

        var shared = Assert.Single(result.Constraints, c =>
            c.Geometry.Kind == PlanarConstraintGeometryKind.Point &&
            c.Geometry.Points[0] == new PlanarPoint2D(2, 2));
        Assert.Equal(2, shared.StructuralRelations.Count);
        Assert.Equal([10, 11], shared.StructuralRelations.Select(r => r.SourceMemberId).OrderBy(id => id));
    }

    [Fact]
    public void Derive_ChangesFingerprintWhenTopologyChanges()
    {
        var topology = new FemSchemaTopology(
            1,
            [Node(1, 1, 1, -1), Node(2, 1, 1, 1)],
            [HostMember(77), Member(10, "bar", "beam", 1, 2)],
            []);
        var first = PlanarConstraintDeriver.Derive(topology, Region(), new());

        topology.Nodes[0].X = 1.25;
        var second = PlanarConstraintDeriver.Derive(topology, Region(), new());

        Assert.NotEqual(first.SourceFingerprint, second.SourceFingerprint);
    }

    [Fact]
    public void Derive_ReturnsBlockingDiagnosticForMissingConnectivityNode()
    {
        var topology = new FemSchemaTopology(
            1,
            [Node(1, 1, 1, 0)],
            [HostMember(77), Member(10, "broken", "beam", 1, 99)],
            []);

        var result = PlanarConstraintDeriver.Derive(topology, Region(), new());

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Code == "planar_constraint_source_node_missing");
    }

    [Fact]
    public void Derive_DoesNotCreateLociOutsideHullOrInsideHole()
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] },
            [new Contour { X = [1, 3, 3, 1], Y = [1, 1, 3, 3] }]);
        region.Id = 77;
        var topology = new FemSchemaTopology(
            1,
            [Node(1, 2, 2, 0), Node(2, 2.5, 2, 0), Node(3, 5, 2, 0), Node(4, 6, 2, 0)],
            [HostMember(region.Id), Member(10, "hole", "beam", 1, 2), Member(11, "outside", "beam", 3, 4)],
            []);

        var result = PlanarConstraintDeriver.Derive(topology, region, new());

        Assert.Empty(result.Constraints);
        Assert.Contains(result.Diagnostics, d => d.Code == "planar_constraint_locus_inside_hole");
    }

    [Fact]
    public void Derive_BlocksConflictingDofPoliciesOnOneGeometryLocus()
    {
        var topology = new FemSchemaTopology(
            1,
            [
                Node(1, 1, 2, 0), Node(2, 3, 2, 0),
                Node(3, 1, 2, -1), Node(4, 3, 2, -1), Node(5, 3, 2, 1), Node(6, 1, 2, 1)
            ],
            [
                HostMember(77), Member(10, "bar", "beam", 1, 2), Member(20, "wall", "shell")
            ],
            [Element(201, "wall-e1", "shell", "wall", 3, 4, 5, 6)]);
        var options = new PlanarConstraintDerivationOptions
        {
            CoplanarBarDofMask = PlanarDofMask.UX,
            WallLineDofMask = PlanarDofMask.UY
        };

        var result = PlanarConstraintDeriver.Derive(topology, Region(), options);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "planar_constraint_dof_conflict");
    }

    static PlanarRegion Region()
    {
        var region = PlanarRegion.CreateFromContour(new Contour
        {
            X = [0, 4, 4, 0],
            Y = [0, 0, 4, 4]
        });
        region.Id = 77;
        return region;
    }

    static FemNode Node(int id, double x, double y, double z) => new()
    {
        Id = id,
        SchemaId = 1,
        NodeTag = id.ToString(),
        X = x,
        Y = y,
        Z = z
    };

    static FemMember Member(int id, string tag, string type, params int[] nodeIds) => Member(id, tag, type, null, nodeIds);

    static FemMember HostMember(int regionId) => new()
    {
        Id = 100,
        SchemaId = 1,
        ElemTag = "host",
        ElemType = "shell",
        PlanarRegionId = regionId,
        NodeIdsJson = "[]"
    };

    static FemMember Member(int id, string tag, string type, int? planarRegionId, params int[] nodeIds) => new()
    {
        Id = id,
        SchemaId = 1,
        ElemTag = tag,
        ElemType = type,
        PlanarRegionId = planarRegionId,
        NodeIdsJson = JsonSerializer.Serialize(nodeIds)
    };

    static FemElement Element(int id, string tag, string type, string sourceMemberTag, params int[] nodeIds) => new()
    {
        Id = id,
        SchemaId = 1,
        ElemTag = tag,
        ElemType = type,
        SourceMemberTag = sourceMemberTag,
        NodeIdsJson = JsonSerializer.Serialize(nodeIds)
    };
}
