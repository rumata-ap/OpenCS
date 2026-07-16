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
        var elements = new[]
        {
            new FemElement { Id = 1, SchemaId = 1, ElemTag = "1", NodeIdsJson = "[1,2]" }
        };

        var errors = FemTopologyValidator.Validate(schema, nodes, elements, []);

        Assert.Contains(errors, e => e.Code == "element_node_missing");
    }

    [Fact]
    public void Validate_RejectsZeroLengthBeam()
    {
        var schema = new FemSchema { Id = 1 };
        var nodes = new[] { new FemNode { Id = 1, SchemaId = 1, NodeTag = "1" } };
        var elements = new[]
        {
            new FemElement { Id = 1, SchemaId = 1, ElemTag = "1", NodeIdsJson = "[1,1]" }
        };

        var errors = FemTopologyValidator.Validate(schema, nodes, elements, []);

        Assert.Contains(errors, e => e.Code == "element_zero_length");
    }

    [Fact]
    public void Validate_RejectsDuplicateTagsAndDanglingMemberElement()
    {
        var schema = new FemSchema { Id = 1 };
        var nodes = new[]
        {
            new FemNode { Id = 1, SchemaId = 1, NodeTag = "A" },
            new FemNode { Id = 2, SchemaId = 1, NodeTag = "A" }
        };
        var members = new[]
        {
            new FemMember { Id = 1, SchemaId = 1, Tag = "M1", ElemIdsJson = "[99]" }
        };

        var errors = FemTopologyValidator.Validate(schema, nodes, [], members);

        Assert.Contains(errors, e => e.Code == "node_tag_duplicate");
        Assert.Contains(errors, e => e.Code == "member_element_missing");
    }

    [Fact]
    public void Validate_RejectsInvalidGjConfiguration()
    {
        var schema = new FemSchema { Id = 1 };
        var members = new[]
        {
            new FemMember { Id = 1, SchemaId = 1, Tag = "M1", GjStrategy = "manual", GjManualValue = null },
            new FemMember { Id = 2, SchemaId = 1, Tag = "M2", GjStrategy = "saint_venant", GjTorsionTaskId = null }
        };

        var errors = FemTopologyValidator.Validate(schema, [], [], members);

        Assert.Contains(errors, e => e.Code == "gj_manual_value_missing");
        Assert.Contains(errors, e => e.Code == "gj_torsion_task_missing");
    }

    [Fact]
    public void Validate_MissingCrossSectionIsWarningNotError()
    {
        var schema = new FemSchema { Id = 1 };
        var members = new[] { new FemMember { Id = 1, SchemaId = 1, Tag = "M1" } };

        var errors = FemTopologyValidator.Validate(schema, [], [], members);

        var diagnostic = Assert.Single(errors, e => e.Code == "member_section_missing");
        Assert.False(diagnostic.IsError);
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
        var elements = new[] { new FemElement { ElemTag = "5" }, new FemElement { ElemTag = "6" } };
        Assert.Equal("7", FemTopologyValidator.NextElemTag(elements));
    }
}
