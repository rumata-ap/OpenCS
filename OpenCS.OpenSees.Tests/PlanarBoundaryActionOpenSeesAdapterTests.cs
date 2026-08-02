using CScore.Planar;
using CScore.Fem;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Tests.Fixtures;
using OpenCS.OpenSees.Structural;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public sealed class PlanarBoundaryActionOpenSeesAdapterTests
{
    [Fact]
    public void ApplyMapsForceKinematicAndPreservedSupportToShellModel()
    {
        var model = ShellModelFixtures.Q4Elastic();
        var mapped = new PlanarBoundaryActionMeshMappingResult
        {
            NodalActions = [new(1, new(10, 20, 30), new(1, 2, 3))],
            PrescribedDofs = new Dictionary<(int NodeIndex, int Dof), double>
            {
                [(2, 2)] = 0.01
            },
            PreservedSupportDofs = new HashSet<(int NodeIndex, int Dof)> { (2, 0) }
        };
        var nodeTags = new Dictionary<int, int>
        {
            [0] = 1,
            [1] = 2,
            [2] = 3,
            [3] = 4
        };

        var result = PlanarBoundaryActionOpenSeesAdapter.Apply(model, mapped, nodeTags, 0);

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        Assert.Same(mapped, result.SourceMapping);
        var mappedModel = Assert.IsType<ShellOpenSeesModel>(result.Model);
        var stage = Assert.Single(mappedModel.Stages);
        var load = Assert.Single(stage.Loads);
        Assert.Equal(2, load.NodeTag);
        Assert.Equal(10, load.Fx, 10);
        Assert.Equal(30, load.Fz, 10);
        var kinematic = Assert.Single(stage.KinematicLoads);
        Assert.Equal(3, kinematic.Dof);
        Assert.Equal(0.01, kinematic.Value, 10);
        var preservedNode = mappedModel.Nodes.Single(node => node.Tag == 3);
        Assert.True(preservedNode.Fixed[0]);
    }

    [Fact]
    public void ApplyRejectsUnknownSnapshotNode()
    {
        var model = ShellModelFixtures.Q4Elastic();
        var mapped = new PlanarBoundaryActionMeshMappingResult
        {
            NodalActions = [new(99, PlanarVector3.Zero, PlanarVector3.Zero)]
        };

        var result = PlanarBoundaryActionOpenSeesAdapter.Apply(
            model,
            mapped,
            new Dictionary<int, int> { [0] = 1 },
            0);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "planar_boundary_opensees_node_unknown");
    }

    [Fact]
    public void ApplyRejectsNonCalculableMapping()
    {
        var model = ShellModelFixtures.Q4Elastic();
        var mapped = new PlanarBoundaryActionMeshMappingResult
        {
            Diagnostics = [new FemValidationDiagnostic("mapping_failed", "failed")]
        };

        var result = PlanarBoundaryActionOpenSeesAdapter.Apply(
            model,
            mapped,
            new Dictionary<int, int>(),
            0);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "mapping_failed");
    }
}
