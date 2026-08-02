using System.Text.Json;
using CScore.Fem;
using CScore.Planar;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;

namespace OpenCS.OpenSees.Tests;

public sealed class PlanarStructuralOpenSeesIntegrationTests
{
    [Fact]
    public async Task EqualDofDerivedPoints_RunThroughOpenSees()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        ShellOpenSeesModel baseModel = ShellBeamConnectionFixtures.EqualDofSeam() with
        {
            EqualDofConstraints = []
        };
        var input = BuildPointInput(
            baseModel,
            [
                ("point-2", 10, 2, 6, 1, 0),
                ("point-3", 11, 3, 7, 1, 1)
            ]);

        PlanarOpenSeesConstraintResult built = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult, input.Snapshot, input.Constraints, input.Topology, input.SourceNodeTags);

        Assert.True(built.IsCalculable, string.Join(Environment.NewLine, built.Diagnostics));
        string script = new ShellTclGenerator().Generate(built.Model!);
        Assert.Equal(2, script.Split("equalDOF", StringSplitOptions.None).Length - 1);
        ShellResult result = await RunAsync(executable, built.Model!);

        var node2 = result.Displacements.Single(displacement => displacement.NodeTag == 2);
        var node6 = result.Displacements.Single(displacement => displacement.NodeTag == 6);
        var node3 = result.Displacements.Single(displacement => displacement.NodeTag == 3);
        var node7 = result.Displacements.Single(displacement => displacement.NodeTag == 7);
        Assert.True(Math.Abs(node2.Uz - node6.Uz) < 1e-9);
        Assert.True(Math.Abs(node3.Uz - node7.Uz) < 1e-9);
        Assert.True(Math.Abs(result.Reactions.Sum(reaction => reaction.Fz) - 2000) < 1e-6);
    }

    [Fact]
    public async Task RigidLinkDerivedPoint_TransfersEccentricMomentThroughOpenSees()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        ShellOpenSeesModel baseModel = ShellBeamConnectionFixtures.RigidLinkOffset() with
        {
            RigidLinks = []
        };
        var input = BuildPointInput(
            baseModel,
            [("offset-point", 20, 2, 5, 1, 0.5)]);

        PlanarOpenSeesConstraintResult built = PlanarStructuralOpenSeesAdapter.Apply(
            input.ShellResult,
            input.Snapshot,
            input.Constraints,
            input.Topology,
            input.SourceNodeTags,
            new PlanarOpenSeesConstraintOptions
            {
                EmbeddedMemberPolicy = PlanarOpenSeesConstraintPolicy.RigidLinkBeam
            });

        Assert.True(built.IsCalculable, string.Join(Environment.NewLine, built.Diagnostics));
        string script = new ShellTclGenerator().Generate(built.Model!);
        Assert.Equal(1, script.Split("rigidLink beam", StringSplitOptions.None).Length - 1);
        ShellResult result = await RunAsync(executable, built.Model!);

        double reactionFx = result.Reactions.Sum(reaction => reaction.Fx);
        double reactionMomentY = result.Reactions.Sum(reaction => reaction.My);
        Assert.True(Math.Abs(reactionFx + 1000) < 1e-3);
        Assert.True(Math.Abs(Math.Abs(reactionMomentY) - 500) < 25);
    }

    private static PointCase BuildPointInput(
        ShellOpenSeesModel model,
        IReadOnlyList<(string Id, int SourceMemberId, int SourceNodeId, int HostTag, double X, double Z)> definitions)
    {
        var constraints = new List<PlanarConstraintObject>();
        var mappings = new List<PlanarConstraintMeshMapping>();
        var snapshotNodes = new List<PlanarMeshNode>();
        var sourceNodes = new List<FemNode>();
        var sourceMembers = new List<FemMember>();
        var sourceNodeTags = new Dictionary<int, int>();
        var nodeIndexToTag = new Dictionary<int, int>();

        for (var index = 0; index < definitions.Count; index++)
        {
            (string id, int sourceMemberId, int sourceNodeId, int hostTag, double x, double z) = definitions[index];
            int sourceTag = sourceNodeId;
            const PlanarDofMask mask = PlanarDofMask.UX | PlanarDofMask.UY | PlanarDofMask.UZ |
                                       PlanarDofMask.RX | PlanarDofMask.RY | PlanarDofMask.RZ;
            var master = new PlanarMasterReference("fem-member", sourceMemberId.ToString());
            var constraint = PlanarConstraintObject.Point(
                id,
                new PlanarPoint2D(x, index),
                new PlanarStructuralFacet(PlanarStructuralKind.EmbeddedMember, master, mask),
                new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint));
            constraint.SourceReferences =
            [
                new PlanarSourceReference(
                    sourceMemberId,
                    $"source-{sourceMemberId}",
                    [],
                    [],
                    [sourceNodeId],
                    [sourceNodeId.ToString()])
            ];
            constraint.StructuralRelations =
            [
                new PlanarStructuralRelation(
                    sourceMemberId,
                    $"source-{sourceMemberId}",
                    [],
                    [],
                    master,
                    PlanarStructuralKind.EmbeddedMember,
                    mask)
            ];
            constraints.Add(constraint);
            mappings.Add(new PlanarConstraintMeshMapping
            {
                ConstraintObjectId = id,
                PointNodeIndices = [index],
                SourceReferences = constraint.SourceReferences,
                StructuralRelations = constraint.StructuralRelations
            });
            snapshotNodes.Add(new(index, x, index, x, index, z));
            sourceNodes.Add(new FemNode
            {
                Id = sourceNodeId,
                NodeTag = sourceNodeId.ToString(),
                X = x,
                Y = index,
                Z = definitions[index].Id == "offset-point" ? 0 : z
            });
            sourceMembers.Add(new FemMember
            {
                Id = sourceMemberId,
                ElemTag = $"source-{sourceMemberId}",
                ElemType = "beam",
                NodeIdsJson = JsonSerializer.Serialize(new[] { sourceNodeId })
            });
            sourceNodeTags[sourceNodeId] = sourceTag;
            nodeIndexToTag[index] = hostTag;
        }

        var sourceModelNodes = model.Nodes.ToDictionary(node => node.Tag);
        foreach (FemNode sourceNode in sourceNodes)
        {
            int tag = sourceNodeTags[sourceNode.Id];
            if (!sourceModelNodes.ContainsKey(tag))
                sourceModelNodes[tag] = new(tag, sourceNode.X, sourceNode.Y, sourceNode.Z, new bool[6], $"source:{sourceNode.Id}");
        }

        var shellResult = new PlanarMeshShellModelResult(
            model with { Nodes = sourceModelNodes.Values.OrderBy(node => node.Tag).ToArray() },
            nodeIndexToTag,
            new Dictionary<int, int>(),
            [],
            []);
        var snapshot = new PlanarMeshSnapshot
        {
            IsCalculable = true,
            Nodes = snapshotNodes,
            ConstraintMappings = mappings
        };
        return new PointCase(
            shellResult,
            snapshot,
            constraints,
            new FemSchemaTopology(1, sourceNodes, sourceMembers, []),
            sourceNodeTags);
    }

    private static async Task<ShellResult> RunAsync(string executable, ShellOpenSeesModel model)
    {
        using var fixture = new ShellArtifactFixture();
        string scriptPath = Path.Combine(fixture.Directory, "script.tcl");
        File.WriteAllText(scriptPath, new ShellTclGenerator().Generate(model));
        OpenSeesRunResult run = await new OpenSeesProcessRunner().RunAsync(
            new OpenSeesRunRequest
            {
                ExecutablePath = executable,
                WorkingDirectory = fixture.Directory,
                ScriptPath = scriptPath,
                Timeout = TimeSpan.FromSeconds(30)
            }, CancellationToken.None);

        Assert.Equal(0, run.ExitCode);
        ShellResult result = new ShellResultParser().Parse(
            fixture.Directory, model.Elements.ToDictionary(element => element.Tag));
        Assert.Equal("completed", result.Status);
        return result;
    }

    private sealed record PointCase(
        PlanarMeshShellModelResult ShellResult,
        PlanarMeshSnapshot Snapshot,
        IReadOnlyList<PlanarConstraintObject> Constraints,
        FemSchemaTopology Topology,
        IReadOnlyDictionary<int, int> SourceNodeTags);
}
