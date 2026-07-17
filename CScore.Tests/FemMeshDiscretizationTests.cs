using CScore.Fem;
using Xunit;

namespace CScore.Tests;

public sealed class FemMeshDiscretizationTests
{
    [Fact]
    public void FemMeshNode_ContainsMeshCoordinatesAndSourceReferences()
    {
        var node = new FemMeshNode
        {
            Id = 10,
            SchemaId = 2,
            NodeTag = "m10",
            X = 1.25,
            Y = -2.5,
            Z = 3.75,
            SourceNodeTag = "N1",
            SourceMemberTag = "M1"
        };

        Assert.Equal(10, node.Id);
        Assert.Equal(2, node.SchemaId);
        Assert.Equal("m10", node.NodeTag);
        Assert.Equal(1.25, node.X);
        Assert.Equal(-2.5, node.Y);
        Assert.Equal(3.75, node.Z);
        Assert.Equal("N1", node.SourceNodeTag);
        Assert.Equal("M1", node.SourceMemberTag);
    }

    [Fact]
    public void FemElement_ContainsMeshMetadataAndReadsFirstTwoNodeIds()
    {
        var element = new FemElement
        {
            Id = 20,
            SchemaId = 2,
            ElemTag = "e20",
            NodeIdsJson = "[10, 11, 12]",
            SourceMemberTag = "M1",
            CrossSectionId = 4,
            GjStrategy = "manual",
            GjManualValue = 123.5,
            GjTorsionTaskId = 9
        };

        Assert.Equal(20, element.Id);
        Assert.Equal(2, element.SchemaId);
        Assert.Equal("e20", element.ElemTag);
        Assert.Equal("M1", element.SourceMemberTag);
        Assert.Equal(4, element.CrossSectionId);
        Assert.Equal("manual", element.GjStrategy);
        Assert.Equal(123.5, element.GjManualValue);
        Assert.Equal(9, element.GjTorsionTaskId);
        Assert.Equal(10, element.Node1);
        Assert.Equal(11, element.Node2);
    }

    [Fact]
    public void FemMember_TargetMeshLengthM_IsNullable()
    {
        var member = new FemMember { TargetMeshLengthM = 0.25 };

        Assert.Equal(0.25, member.TargetMeshLengthM);
        member.TargetMeshLengthM = null;
        Assert.Null(member.TargetMeshLengthM);
    }

    [Fact]
    public void Validate_RejectsNonPositiveOrNonFiniteTargetMeshLength()
    {
        foreach (var value in new[] { 0d, -0.1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var member = new FemMember { Id = 1, ElemTag = "1", TargetMeshLengthM = value };

            var diagnostics = FemTopologyValidator.Validate(new FemSchema { Id = 1 }, [], [member], []);

            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "target_mesh_length_invalid");
        }
    }

    [Fact]
    public void ValidateMesh_AcceptsRegularTwoNodeElement()
    {
        var nodes = new[]
        {
            new FemMeshNode { Id = 1, NodeTag = "1", X = 0, Y = 0, Z = 0 },
            new FemMeshNode { Id = 2, NodeTag = "2", X = 1, Y = 0, Z = 0 }
        };
        var elements = new[]
        {
            new FemElement { Id = 1, ElemTag = "1", NodeIdsJson = "[1,2]" }
        };

        var diagnostics = FemTopologyValidator.ValidateMesh(nodes, elements);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code is "mesh_element_node_missing" or "mesh_element_degenerate");
    }

    [Fact]
    public void ValidateMesh_ReportsElementReferencingMissingNode()
    {
        var nodes = new[] { new FemMeshNode { Id = 1, NodeTag = "1" } };
        var elements = new[]
        {
            new FemElement { Id = 1, ElemTag = "1", NodeIdsJson = "[1,99]" }
        };

        var diagnostics = FemTopologyValidator.ValidateMesh(nodes, elements);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "mesh_element_node_missing");
    }

    [Fact]
    public void ValidateMesh_ReportsElementWithCoincidentNodesAsDegenerate()
    {
        var nodes = new[]
        {
            new FemMeshNode { Id = 1, NodeTag = "1", X = 2, Y = 3, Z = 4 },
            new FemMeshNode { Id = 2, NodeTag = "2", X = 2, Y = 3, Z = 4 }
        };
        var elements = new[]
        {
            new FemElement { Id = 1, ElemTag = "1", NodeIdsJson = "[1,2]" }
        };

        var diagnostics = FemTopologyValidator.ValidateMesh(nodes, elements);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "mesh_element_degenerate");
    }
}
