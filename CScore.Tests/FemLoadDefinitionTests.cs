using CScore.Fem;
using CScore.Fem.Editing;
using Xunit;

namespace CScore.Tests;

public sealed class FemLoadDefinitionTests
{
    [Fact]
    public void Validator_RejectsTermReferencingUnknownLoadCase()
    {
        var schema = new FemSchema { Id = 7 };
        var definition = new FemLoadDefinition { SchemaId = 7, Tag = "C1" };
        definition.SetExpression(new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            Terms = [new FemLoadTerm { LoadCaseId = 99, Coefficient = 1 }]
        });

        var diagnostics = FemLoadDefinitionValidator.Validate(schema, [definition], []);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "load_definition_load_case_missing");
    }

    [Fact]
    public void Session_AllocatesDistinctNegativeIdsForNewNodesAndLoadCases()
    {
        var session = new FemSchemaEditSession(new FemSchema { Id = 7 });

        int node1 = session.AllocateTemporaryNodeId();
        int node2 = session.AllocateTemporaryNodeId();
        int loadCase1 = session.AllocateTemporaryLoadCaseId();
        int loadCase2 = session.AllocateTemporaryLoadCaseId();

        Assert.True(node1 < 0);
        Assert.True(loadCase1 < 0);
        Assert.NotEqual(node1, node2);
        Assert.NotEqual(loadCase1, loadCase2);
    }

    [Fact]
    public void Resolver_SumsDefinitionTermsAtSameNode()
    {
        var definition = new FemLoadDefinition { SchemaId = 1, Tag = "C1" };
        definition.SetExpression(new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            Terms =
            [
                new FemLoadTerm { LoadCaseId = 10, Coefficient = 1 },
                new FemLoadTerm { LoadCaseId = 20, Coefficient = 2 }
            ]
        });
        var loads = new[]
        {
            new FemNodeLoad { LoadCaseId = 10, NodeId = 3, Fz = -4, My = 2 },
            new FemNodeLoad { LoadCaseId = 20, NodeId = 3, Fz = -4, My = 1 }
        };

        var result = FemLoadDisplayResolver.ResolveDefinition(definition, loads);

        var nodeLoad = Assert.Single(result);
        Assert.Equal(-12, nodeLoad.Fz);
        Assert.Equal(4, nodeLoad.My);
    }

    [Fact]
    public void Resolver_OmitsNodeWhenAllResolvedComponentsAreZero()
    {
        var definition = new FemLoadDefinition { SchemaId = 1, Tag = "C1" };
        definition.SetExpression(new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            Terms =
            [
                new FemLoadTerm { LoadCaseId = 10, Coefficient = 1 },
                new FemLoadTerm { LoadCaseId = 20, Coefficient = -1 }
            ]
        });
        var loads = new[]
        {
            new FemNodeLoad { LoadCaseId = 10, NodeId = 3, Fz = -4 },
            new FemNodeLoad { LoadCaseId = 20, NodeId = 3, Fz = -4 }
        };

        Assert.Empty(FemLoadDisplayResolver.ResolveDefinition(definition, loads));
    }

    [Fact]
    public void Sp20Factory_MaterializesDefinitionsWithLoadCaseTerms()
    {
        var schema = new FemSchema { Id = 7 };
        var node = new FemNode { Id = 1, SchemaId = schema.Id, NodeTag = "1" };
        var cases = new[]
        {
            new FemLoadCase { Id = 10, SchemaId = schema.Id, Tag = "G", Sp20Type = "permanent" },
            new FemLoadCase { Id = 11, SchemaId = schema.Id, Tag = "Q", Sp20Type = "short_term" }
        };
        var loads = new[]
        {
            new FemNodeLoad { SchemaId = schema.Id, LoadCaseId = 10, NodeId = 1, Fz = -10 },
            new FemNodeLoad { SchemaId = schema.Id, LoadCaseId = 11, NodeId = 1, Fz = -5 }
        };

        var definitions = FemLoadDefinitionFactory.CreateSp20(schema, cases, [node], loads, "fundamental");

        Assert.NotEmpty(definitions);
        Assert.All(definitions, definition =>
        {
            Assert.Equal(schema.Id, definition.SchemaId);
            Assert.Equal("sp20", definition.SourceKind);
            Assert.NotEmpty(definition.GetExpression().Terms);
        });
    }
}
