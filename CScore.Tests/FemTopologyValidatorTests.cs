using CScore.Fem;
using Xunit;

namespace CScore.Tests;

public sealed class FemTopologyValidatorTests
{
    [Fact]
    public void Validate_RejectsElementReferencingMissingNode()
    {
        var schema = new FemSchema { Id = 1 };
        var nodes = new[] { new FemNode { Id = 1, SchemaId = 1, NodeTag = "1" } };
        var members = new[]
        {
            new FemMember { Id = 1, SchemaId = 1, ElemTag = "1", NodeIdsJson = "[1,2]" }
        };

        var errors = FemTopologyValidator.Validate(schema, nodes, members, []);

        Assert.Contains(errors, e => e.Code == "element_node_missing");
    }

    [Fact]
    public void Validate_RejectsZeroLengthBeam()
    {
        var schema = new FemSchema { Id = 1 };
        var nodes = new[] { new FemNode { Id = 1, SchemaId = 1, NodeTag = "1" } };
        var members = new[]
        {
            new FemMember { Id = 1, SchemaId = 1, ElemTag = "1", NodeIdsJson = "[1,1]" }
        };

        var errors = FemTopologyValidator.Validate(schema, nodes, members, []);

        Assert.Contains(errors, e => e.Code == "element_zero_length");
    }

    [Fact]
    public void Validate_RejectsDuplicateTagsAndDanglingGroupMember()
    {
        var schema = new FemSchema { Id = 1 };
        var nodes = new[]
        {
            new FemNode { Id = 1, SchemaId = 1, NodeTag = "A" },
            new FemNode { Id = 2, SchemaId = 1, NodeTag = "A" }
        };
        var groups = new[]
        {
            new FemMemberGroup { Id = 1, SchemaId = 1, Tag = "M1", MemberTagsJson = "[99]" }
        };

        var errors = FemTopologyValidator.Validate(schema, nodes, [], groups);

        Assert.Contains(errors, e => e.Code == "node_tag_duplicate");
        Assert.Contains(errors, e => e.Code == "member_element_missing");
    }

    [Fact]
    public void Validate_FlagsIncompleteGjConfigurationAsWarningNotError()
    {
        var schema = new FemSchema { Id = 1 };
        var members = new[]
        {
            new FemMember { Id = 1, SchemaId = 1, ElemTag = "1", GjStrategy = "manual", GjManualValue = null },
            new FemMember { Id = 2, SchemaId = 1, ElemTag = "2", GjStrategy = "saint_venant", GjTorsionTaskId = null }
        };

        var errors = FemTopologyValidator.Validate(schema, [], members, []);

        var manualMissing = Assert.Single(errors, e => e.Code == "gj_manual_value_missing");
        var taskMissing = Assert.Single(errors, e => e.Code == "gj_torsion_task_missing");
        Assert.False(manualMissing.IsError);
        Assert.False(taskMissing.IsError);
    }

    [Fact]
    public void Validate_RejectsUnknownGjStrategyAsError()
    {
        var schema = new FemSchema { Id = 1 };
        var members = new[] { new FemMember { Id = 1, SchemaId = 1, ElemTag = "1", GjStrategy = "bogus" } };

        var errors = FemTopologyValidator.Validate(schema, [], members, []);

        var diagnostic = Assert.Single(errors, e => e.Code == "gj_strategy_invalid");
        Assert.True(diagnostic.IsError);
    }

    [Fact]
    public void Validate_MissingCrossSectionIsWarningNotError()
    {
        var schema = new FemSchema { Id = 1 };
        var members = new[] { new FemMember { Id = 1, SchemaId = 1, ElemTag = "1" } };

        var errors = FemTopologyValidator.Validate(schema, [], members, []);

        var diagnostic = Assert.Single(errors, e => e.Code == "member_section_missing");
        Assert.False(diagnostic.IsError);
    }

    [Fact]
    public void Validate_DoesNotFlagMultipleUnsavedNodesOrElementsAsDuplicateId()
    {
        var schema = new FemSchema { Id = 1 };
        var nodes = new[]
        {
            new FemNode { Id = 0, SchemaId = 1, NodeTag = "1" },
            new FemNode { Id = 0, SchemaId = 1, NodeTag = "2" }
        };
        var members = new[]
        {
            new FemMember { Id = 0, SchemaId = 1, ElemTag = "1", NodeIdsJson = "[1,2]" },
            new FemMember { Id = 0, SchemaId = 1, ElemTag = "2", NodeIdsJson = "[2,1]" }
        };

        var errors = FemTopologyValidator.Validate(schema, nodes, members, []);

        Assert.DoesNotContain(errors, e => e.Code is "node_id_duplicate" or "element_id_duplicate");
    }

    [Fact]
    public void NextNodeTag_ReturnsMaxPlusOne()
    {
        var nodes = new[]
        {
            new FemNode { NodeTag = "3" },
            new FemNode { NodeTag = "10" },
            new FemNode { NodeTag = "not-a-number" }
        };

        Assert.Equal("11", FemTopologyValidator.NextNodeTag(nodes));
    }

    [Fact]
    public void NextNodeTag_ReturnsOneForEmptySchema()
        => Assert.Equal("1", FemTopologyValidator.NextNodeTag([]));

    [Fact]
    public void NextElemTag_ReturnsMaxPlusOne()
    {
        var members = new[] { new FemMember { ElemTag = "5" }, new FemMember { ElemTag = "6" } };
        Assert.Equal("7", FemTopologyValidator.NextElemTag(members));
    }
}
