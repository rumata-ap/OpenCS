using CScore.Fem;
using Xunit;

namespace CScore.Tests;

/// <summary>Пустое сочетание (без слагаемых с ненулевым коэффициентом) не сохраняется; автослагаемое — не пустое.</summary>
public sealed class FemLoadDefinitionEmptyTests
{
    static IReadOnlyList<FemValidationDiagnostic> Validate(FemLoadExpression expression)
    {
        var schema = new FemSchema { Id = 7 };
        var definition = new FemLoadDefinition { Id = 1, SchemaId = 7, Tag = "Сочетание_01" };
        definition.SetExpression(expression);
        return FemLoadDefinitionValidator.Validate(schema, [definition], [new FemLoadCase { Id = 3, SchemaId = 7, Tag = "ВЕТЕР" }]);
    }

    [Fact]
    public void Validator_RejectsCombinationWithoutTerms()
    {
        var d = Validate(new FemLoadExpression { Mode = FemLoadExpressionMode.Sp20 });
        Assert.Contains(d, x => x.Code == "load_definition_empty");
    }

    [Fact]
    public void Validator_RejectsCombinationWithZeroCoefficientsOnly()
    {
        var d = Validate(new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum, Terms = [new FemLoadTerm { LoadCaseId = 3, Coefficient = 0 }],
        });
        Assert.Contains(d, x => x.Code == "load_definition_empty");
    }

    [Fact]
    public void Validator_AcceptsCombinationWithNonzeroOrAutoTermOrLoadCase()
    {
        Assert.DoesNotContain(Validate(new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum, Terms = [new FemLoadTerm { LoadCaseId = 3, Coefficient = 1.2 }],
        }), x => x.Code == "load_definition_empty");
        Assert.DoesNotContain(Validate(new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sp20, Terms = [new FemLoadTerm { LoadCaseId = 3, Coefficient = 0, Mode = FemLoadTerm.AutoMode, Role = "Leading" }],
        }), x => x.Code == "load_definition_empty");
        Assert.DoesNotContain(Validate(new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Single, LoadCaseIds = [3],
        }), x => x.Code == "load_definition_empty");
        Assert.DoesNotContain(Validate(new FemLoadExpression { Mode = FemLoadExpressionMode.All }),
            x => x.Code == "load_definition_empty");
    }
}
