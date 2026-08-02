using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Planar;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class PlanarStructuralOpenSeesAdapterTests
{
    [Fact]
    public void ConstraintOptions_DefaultToStrictOpenSeesPolicies()
    {
        var options = new PlanarOpenSeesConstraintOptions();

        Assert.Equal(PlanarOpenSeesConstraintPolicy.EqualDof, options.EmbeddedMemberPolicy);
        Assert.Equal(PlanarOpenSeesConstraintPolicy.RigidLinkBeam, options.RigidBodyPolicy);
    }

    [Fact]
    public void Apply_CoincidentEmbeddedPoint_EmitsEqualDof()
    {
        var input = CreatePointInput(PlanarStructuralKind.EmbeddedMember, sourceZ: 0, hostZ: 0);

        PlanarOpenSeesConstraintResult result = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult, input.Snapshot, [input.Constraint], input.Topology, input.SourceNodeTags);

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics));
        var equalDof = Assert.Single(result.Model!.EqualDofConstraints);
        Assert.Equal(100, equalDof.MasterNode);
        Assert.Equal(2, equalDof.SlaveNode);
        Assert.Equal([1, 2, 3, 4, 5, 6], equalDof.Dofs);
        var emission = Assert.Single(result.Emissions);
        Assert.Equal(100, emission.MasterNodeTag);
        Assert.Equal(2, emission.SlaveNodeTag);
        Assert.Equal(10, emission.SourceMemberId);
        Assert.Equal("source-member", emission.SourceMemberTag);
        Assert.Equal([1001], emission.SourceElementIds);
        Assert.Equal([100], emission.SourceNodeIds);
        Assert.Equal([0], emission.HostSnapshotNodeIndices);
    }

    [Fact]
    public void Apply_OffsetEmbeddedPointWithRigidLinkBeam_AllowsOffset()
    {
        var input = CreatePointInput(PlanarStructuralKind.EmbeddedMember, sourceZ: 0, hostZ: 0.5);

        PlanarOpenSeesConstraintResult result = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult,
            input.Snapshot,
            [input.Constraint],
            input.Topology,
            input.SourceNodeTags,
            new PlanarOpenSeesConstraintOptions
            {
                EmbeddedMemberPolicy = PlanarOpenSeesConstraintPolicy.RigidLinkBeam
            });

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(new ShellRigidLinkConstraint(100, 2, ShellRigidLinkType.Beam),
            Assert.Single(result.Model!.RigidLinks));
    }

    [Fact]
    public void Apply_OffsetEmbeddedPointWithEqualDof_BlocksWithoutPartialRelation()
    {
        var input = CreatePointInput(PlanarStructuralKind.EmbeddedMember, sourceZ: 0, hostZ: 0.5);

        PlanarOpenSeesConstraintResult result = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult, input.Snapshot, [input.Constraint], input.Topology, input.SourceNodeTags);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Model);
        Assert.Empty(result.Emissions);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == "planar_opensees_equal_dof_coordinates_mismatch");
    }

    [Fact]
    public void Apply_PointMpc_BlocksAsUnsupported()
    {
        var input = CreatePointInput(PlanarStructuralKind.PointMpc, sourceZ: 0, hostZ: 0);

        PlanarOpenSeesConstraintResult result = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult, input.Snapshot, [input.Constraint], input.Topology, input.SourceNodeTags);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Model);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == "planar_opensees_unsupported_mpc");
    }

    [Fact]
    public void Apply_MissingSourceMaster_BlocksWithoutPartialRelation()
    {
        var input = CreatePointInput(PlanarStructuralKind.EmbeddedMember, sourceZ: 0, hostZ: 0);
        var sourceTags = new Dictionary<int, int>();

        PlanarOpenSeesConstraintResult result = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult, input.Snapshot, [input.Constraint], input.Topology, sourceTags);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Model);
        Assert.Empty(result.Emissions);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == "planar_opensees_source_node_unknown");
    }

    [Fact]
    public void Apply_CoincidentCurve_EmitsRelationForEveryPairedNode()
    {
        var input = CreateCurveInput();

        PlanarOpenSeesConstraintResult result = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult, input.Snapshot, [input.Constraint], input.Topology, input.SourceNodeTags);

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(
            [110, 111, 112],
            result.Model!.EqualDofConstraints.Select(constraint => constraint.MasterNode));
        Assert.Equal([2, 3, 4], result.Model.EqualDofConstraints.Select(constraint => constraint.SlaveNode));
        Assert.Equal(3, result.Emissions.Count);
    }

    [Fact]
    public void Apply_ReversedCurve_UsesReversedSourcePairing()
    {
        var input = CreateCurveInput([new PlanarMeshEdge(1, 2), new PlanarMeshEdge(0, 1)]);

        PlanarOpenSeesConstraintResult result = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult, input.Snapshot, [input.Constraint], input.Topology, input.SourceNodeTags);

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics));
        var masterBySlave = result.Model!.EqualDofConstraints
            .ToDictionary(constraint => constraint.SlaveNode, constraint => constraint.MasterNode);
        Assert.Equal(112, masterBySlave[4]);
        Assert.Equal(111, masterBySlave[3]);
        Assert.Equal(110, masterBySlave[2]);
    }

    [Fact]
    public void Apply_CurveWithDifferentChainLengths_BlocksWithoutPartialRelations()
    {
        var input = CreateCurveInput([new PlanarMeshEdge(0, 1)]);

        PlanarOpenSeesConstraintResult result = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult, input.Snapshot, [input.Constraint], input.Topology, input.SourceNodeTags);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Model);
        Assert.Empty(result.Emissions);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == "planar_opensees_curve_cardinality");
    }

    [Fact]
    public void Apply_OffsetCurveWithEqualDof_BlocksButRigidLinkBeamSucceeds()
    {
        var input = CreateCurveInput(
            [new PlanarMeshEdge(0, 1), new PlanarMeshEdge(1, 2)], sourceZ: 0.5);

        PlanarOpenSeesConstraintResult equalDof = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult, input.Snapshot, [input.Constraint], input.Topology, input.SourceNodeTags);
        PlanarOpenSeesConstraintResult rigidLink = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult,
            input.Snapshot,
            [input.Constraint],
            input.Topology,
            input.SourceNodeTags,
            new PlanarOpenSeesConstraintOptions
            {
                EmbeddedMemberPolicy = PlanarOpenSeesConstraintPolicy.RigidLinkBeam
            });

        Assert.False(equalDof.IsCalculable);
        Assert.Null(equalDof.Model);
        Assert.True(rigidLink.IsCalculable, string.Join(Environment.NewLine, rigidLink.Diagnostics));
        Assert.Equal(3, rigidLink.Model!.RigidLinks.Count);
    }

    [Fact]
    public void Apply_DuplicateConstraintIds_BlocksAtomically()
    {
        var input = CreatePointInput(PlanarStructuralKind.EmbeddedMember, sourceZ: 0, hostZ: 0);

        PlanarOpenSeesConstraintResult result = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult, input.Snapshot, [input.Constraint, input.Constraint], input.Topology, input.SourceNodeTags);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Model);
        Assert.Empty(result.Emissions);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == "planar_opensees_constraint_duplicate");
    }

    [Fact]
    public void Apply_MissingMapping_BlocksStructuralConstraint()
    {
        var input = CreatePointInput(PlanarStructuralKind.EmbeddedMember, sourceZ: 0, hostZ: 0);
        var snapshot = new PlanarMeshSnapshot
        {
            IsCalculable = true,
            Nodes = input.Snapshot.Nodes
        };

        PlanarOpenSeesConstraintResult result = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult, snapshot, [input.Constraint], input.Topology, input.SourceNodeTags);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == "planar_opensees_constraint_mapping_missing");
    }

    [Fact]
    public void Apply_RigidLinkBarWithRotationalMask_Blocks()
    {
        var input = CreatePointInput(PlanarStructuralKind.EmbeddedMember, sourceZ: 0, hostZ: 0);

        PlanarOpenSeesConstraintResult result = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult,
            input.Snapshot,
            [input.Constraint],
            input.Topology,
            input.SourceNodeTags,
            new PlanarOpenSeesConstraintOptions
            {
                EmbeddedMemberPolicy = PlanarOpenSeesConstraintPolicy.RigidLinkBar
            });

        Assert.False(result.IsCalculable);
        Assert.Null(result.Model);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == "planar_opensees_dof_invalid");
    }

    [Fact]
    public void Apply_ExistingSlaveDofConflict_BlocksWithoutAddingRelation()
    {
        var input = CreatePointInput(PlanarStructuralKind.EmbeddedMember, sourceZ: 0, hostZ: 0);
        var model = input.ShellResult.Model with
        {
            EqualDofConstraints = [new ShellEqualDofConstraint(100, 2, [1])]
        };
        var shellResult = input.ShellResult with { Model = model };

        PlanarOpenSeesConstraintResult result = PlanarStructuralOpenSeesAdapter.Apply(
            shellResult, input.Snapshot, [input.Constraint], input.Topology, input.SourceNodeTags);

        Assert.False(result.IsCalculable);
        Assert.Null(result.Model);
        Assert.Empty(result.Emissions);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == "planar_opensees_constraint_conflict");
    }

    [Fact]
    public void Apply_UnknownHostNode_Blocks()
    {
        var input = CreatePointInput(PlanarStructuralKind.EmbeddedMember, sourceZ: 0, hostZ: 0);
        var mapping = new PlanarConstraintMeshMapping
        {
            ConstraintObjectId = input.Constraint.Id,
            PointNodeIndices = [99],
            SourceReferences = input.Constraint.SourceReferences,
            StructuralRelations = input.Constraint.StructuralRelations
        };

        PlanarOpenSeesConstraintResult result = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult,
            SnapshotWithMapping(input, mapping),
            [input.Constraint],
            input.Topology,
            input.SourceNodeTags);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == "planar_opensees_host_node_unknown");
    }

    [Fact]
    public void Apply_EmptyDofMask_Blocks()
    {
        var input = CreatePointInput(PlanarStructuralKind.EmbeddedMember, sourceZ: 0, hostZ: 0);
        var constraint = input.Constraint;
        constraint.DofMask = PlanarDofMask.None;
        constraint.StructuralFacet = new PlanarStructuralFacet(
            PlanarStructuralKind.EmbeddedMember,
            constraint.MasterReference,
            PlanarDofMask.None);
        constraint.StructuralRelations =
        [constraint.StructuralRelations.Single() with { DofMask = PlanarDofMask.None }];
        var mapping = new PlanarConstraintMeshMapping
        {
            ConstraintObjectId = constraint.Id,
            PointNodeIndices = [0],
            SourceReferences = constraint.SourceReferences,
            StructuralRelations = constraint.StructuralRelations
        };

        PlanarOpenSeesConstraintResult result = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult,
            SnapshotWithMapping(input, mapping),
            [constraint],
            input.Topology,
            input.SourceNodeTags);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == "planar_opensees_dof_invalid");
    }

    [Fact]
    public void Apply_SourceReferenceMismatch_Blocks()
    {
        var input = CreatePointInput(PlanarStructuralKind.EmbeddedMember, sourceZ: 0, hostZ: 0);
        var relation = input.Constraint.StructuralRelations.Single() with
        {
            SourceMemberId = 999,
            SourceMemberTag = "missing-member"
        };
        var mapping = new PlanarConstraintMeshMapping
        {
            ConstraintObjectId = input.Constraint.Id,
            PointNodeIndices = [0],
            SourceReferences = input.Constraint.SourceReferences,
            StructuralRelations = [relation]
        };

        PlanarOpenSeesConstraintResult result = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult,
            SnapshotWithMapping(input, mapping),
            [input.Constraint],
            input.Topology,
            input.SourceNodeTags);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == "planar_opensees_source_reference_ambiguous");
    }

    [Fact]
    public void Apply_MasterAndSlaveSameTag_Blocks()
    {
        var input = CreatePointInput(PlanarStructuralKind.EmbeddedMember, sourceZ: 0, hostZ: 0);
        var sourceTags = new Dictionary<int, int> { [100] = 2 };

        PlanarOpenSeesConstraintResult result = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult, input.Snapshot, [input.Constraint], input.Topology, sourceTags);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == "planar_opensees_master_slave_same");
    }

    static PlanarMeshSnapshot SnapshotWithMapping(
        PointInput input,
        PlanarConstraintMeshMapping mapping) => new()
        {
            IsCalculable = true,
            Nodes = input.Snapshot.Nodes,
            ConstraintMappings = [mapping]
        };

    private static PointInput CreatePointInput(
        PlanarStructuralKind kind,
        double sourceZ,
        double hostZ)
    {
        const int sourceNodeId = 100;
        const int sourceNodeTag = 100;
        const int hostNodeTag = 2;
        const int sourceMemberId = 10;
        const string sourceMemberTag = "source-member";
        var master = new PlanarMasterReference("fem-member", sourceMemberId.ToString());
        var mask = PlanarDofMask.UX | PlanarDofMask.UY | PlanarDofMask.UZ |
                   PlanarDofMask.RX | PlanarDofMask.RY | PlanarDofMask.RZ;
        var constraint = PlanarConstraintObject.Point(
            "constraint-1",
            new PlanarPoint2D(0, 0),
            new PlanarStructuralFacet(kind, master, mask),
            new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint));
        constraint.SourceReferences =
        [
            new PlanarSourceReference(
                sourceMemberId, sourceMemberTag, [1001], ["source-element"],
                [sourceNodeId], [sourceNodeId.ToString()])
        ];
        constraint.StructuralRelations =
        [
            new PlanarStructuralRelation(
                sourceMemberId, sourceMemberTag, [1001], ["source-element"],
                master, kind, mask)
        ];

        var mapping = new PlanarConstraintMeshMapping
        {
            ConstraintObjectId = constraint.Id,
            PointNodeIndices = [0],
            SourceReferences = constraint.SourceReferences,
            StructuralRelations = constraint.StructuralRelations
        };
        var snapshot = new PlanarMeshSnapshot
        {
            IsCalculable = true,
            Nodes = [new(0, 0, 0, 0, 0, hostZ)],
            ConstraintMappings = [mapping]
        };
        var model = new ShellOpenSeesModel
        {
            Nodes =
            [
                new(sourceNodeTag, 0, 0, sourceZ, new bool[6], "source"),
                new(hostNodeTag, 0, 0, hostZ, new bool[6], "host")
            ]
        };
        var shellResult = new PlanarMeshShellModelResult(
            model,
            new Dictionary<int, int> { [0] = hostNodeTag },
            new Dictionary<int, int>(),
            [],
            []);
        var topology = new FemSchemaTopology(
            1,
            [new FemNode { Id = sourceNodeId, NodeTag = sourceNodeId.ToString(), X = 0, Y = 0, Z = sourceZ }],
            [new FemMember
            {
                Id = sourceMemberId,
                ElemTag = sourceMemberTag,
                ElemType = "beam",
                NodeIdsJson = JsonSerializer.Serialize(new[] { sourceNodeId })
            }],
            [new FemElement
            {
                Id = 1001,
                ElemTag = "source-element",
                ElemType = "beam",
                SourceMemberTag = sourceMemberTag,
                NodeIdsJson = JsonSerializer.Serialize(new[] { sourceNodeId })
            }]);

        return new PointInput(
            shellResult,
            snapshot,
            constraint,
            topology,
            new Dictionary<int, int> { [sourceNodeId] = sourceNodeTag });
    }

    private static CurveInput CreateCurveInput(
        IReadOnlyList<PlanarMeshEdge>? edges = null,
        double sourceZ = 0)
    {
        const int sourceMemberId = 20;
        const string sourceMemberTag = "source-curve";
        int[] sourceNodeIds = [10, 11, 12];
        int[] sourceNodeTags = [110, 111, 112];
        var master = new PlanarMasterReference("fem-member", sourceMemberId.ToString());
        const PlanarDofMask mask = PlanarDofMask.UX | PlanarDofMask.UY | PlanarDofMask.UZ |
                                   PlanarDofMask.RX | PlanarDofMask.RY | PlanarDofMask.RZ;
        var constraint = PlanarConstraintObject.Curve(
            "curve-1",
            [new PlanarPoint2D(0, 0), new PlanarPoint2D(1, 0), new PlanarPoint2D(2, 0)],
            new PlanarStructuralFacet(PlanarStructuralKind.EmbeddedMember, master, mask),
            new PlanarMeshFacet(PlanarMeshKind.ConformingPartition));
        constraint.SourceReferences =
        [
            new PlanarSourceReference(
                sourceMemberId, sourceMemberTag, [2001, 2002], ["source-e1", "source-e2"],
                sourceNodeIds, sourceNodeTags.Select(tag => tag.ToString()).ToArray())
        ];
        constraint.StructuralRelations =
        [
            new PlanarStructuralRelation(
                sourceMemberId, sourceMemberTag, [2001, 2002], ["source-e1", "source-e2"],
                master, PlanarStructuralKind.EmbeddedMember, mask)
        ];
        var mapping = new PlanarConstraintMeshMapping
        {
            ConstraintObjectId = constraint.Id,
            OrderedCurveEdges = edges ?? [new(0, 1), new(1, 2)],
            SourceReferences = constraint.SourceReferences,
            StructuralRelations = constraint.StructuralRelations
        };
        var snapshot = new PlanarMeshSnapshot
        {
            IsCalculable = true,
            Nodes =
            [
                new(0, 0, 0, 0, 0, 0),
                new(1, 1, 0, 1, 0, 0),
                new(2, 2, 0, 2, 0, 0)
            ],
            ConstraintMappings = [mapping]
        };
        var modelNodes = new List<NormalizedShellNode>();
        for (var i = 0; i < sourceNodeIds.Length; i++)
            modelNodes.Add(new(sourceNodeTags[i], i, 0, sourceZ, new bool[6], $"source-{sourceNodeIds[i]}"));
        modelNodes.AddRange(
        [
            new(2, 0, 0, 0, new bool[6], "host-0"),
            new(3, 1, 0, 0, new bool[6], "host-1"),
            new(4, 2, 0, 0, new bool[6], "host-2")
        ]);
        var shellResult = new PlanarMeshShellModelResult(
            new ShellOpenSeesModel { Nodes = modelNodes },
            new Dictionary<int, int> { [0] = 2, [1] = 3, [2] = 4 },
            new Dictionary<int, int>(),
            [],
            []);
        var topology = new FemSchemaTopology(
            1,
            [
                new FemNode { Id = 10, NodeTag = "10", X = 0, Y = 0, Z = sourceZ },
                new FemNode { Id = 11, NodeTag = "11", X = 1, Y = 0, Z = sourceZ },
                new FemNode { Id = 12, NodeTag = "12", X = 2, Y = 0, Z = sourceZ }
            ],
            [new FemMember
            {
                Id = sourceMemberId,
                ElemTag = sourceMemberTag,
                ElemType = "beam",
                NodeIdsJson = JsonSerializer.Serialize(sourceNodeIds)
            }],
            [
                new FemElement
                {
                    Id = 2001,
                    ElemTag = "source-e1",
                    ElemType = "beam",
                    SourceMemberTag = sourceMemberTag,
                    NodeIdsJson = JsonSerializer.Serialize(new[] { 10, 11 })
                },
                new FemElement
                {
                    Id = 2002,
                    ElemTag = "source-e2",
                    ElemType = "beam",
                    SourceMemberTag = sourceMemberTag,
                    NodeIdsJson = JsonSerializer.Serialize(new[] { 11, 12 })
                }
            ]);

        return new CurveInput(
            shellResult,
            snapshot,
            constraint,
            topology,
            sourceNodeIds.Zip(sourceNodeTags).ToDictionary(pair => pair.First, pair => pair.Second));
    }

    private sealed record PointInput(
        PlanarMeshShellModelResult ShellResult,
        PlanarMeshSnapshot Snapshot,
        PlanarConstraintObject Constraint,
        FemSchemaTopology Topology,
        IReadOnlyDictionary<int, int> SourceNodeTags);

    private sealed record CurveInput(
        PlanarMeshShellModelResult ShellResult,
        PlanarMeshSnapshot Snapshot,
        PlanarConstraintObject Constraint,
        FemSchemaTopology Topology,
        IReadOnlyDictionary<int, int> SourceNodeTags);
}
