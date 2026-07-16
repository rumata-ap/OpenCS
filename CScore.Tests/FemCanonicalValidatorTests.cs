using CScore.Fem;
using Xunit;

namespace CScore.Tests;

public sealed class FemCanonicalValidatorTests
{
    [Fact]
    public void Validator_RejectsLoadFromAnotherSchema()
    {
        var schema = new FemSchema { Id = 1 };
        var cases = new[] { new FemLoadCase { Id = 2, SchemaId = 2 } };
        var nodes = new[] { new FemNode { Id = 3, SchemaId = 1 } };
        var loads = new[] { new FemNodeLoad { SchemaId = 1, LoadCaseId = 2, NodeId = 3 } };

        var errors = FemCanonicalValidator.Validate(schema, cases, nodes, loads);

        Assert.Contains(errors, error => error.Code == "load_case_schema_mismatch");
    }

    [Fact]
    public void Validator_RejectsUnknownSp20Type()
    {
        var errors = FemCanonicalValidator.Validate(
            new FemSchema { Id = 1 },
            [new FemLoadCase { Id = 2, SchemaId = 1, Tag = "Q", Sp20Type = "unknown" }],
            [], []);

        Assert.Contains(errors, error => error.Code == "sp20_type_invalid");
    }

    [Fact]
    public void Validator_RejectsNonFiniteLoadsAndDuplicateTags()
    {
        var errors = FemCanonicalValidator.Validate(
            new FemSchema { Id = 1 },
            [
                new FemLoadCase { Id = 1, SchemaId = 1, Tag = "Q", Sp20Type = "short_term" },
                new FemLoadCase { Id = 2, SchemaId = 1, Tag = "Q", Sp20Type = "short_term" }
            ],
            [new FemNode { Id = 10, SchemaId = 1 }],
            [new FemNodeLoad { SchemaId = 1, LoadCaseId = 1, NodeId = 10, Fx = double.NaN }]);

        Assert.Contains(errors, error => error.Code == "load_tag_duplicate");
        Assert.Contains(errors, error => error.Code == "load_component_not_finite");
    }
}
