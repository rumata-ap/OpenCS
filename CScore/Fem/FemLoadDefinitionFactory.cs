using CScore.Fem.Combinations;

namespace CScore.Fem;

/// <summary>Создаёт материализованные определения сочетаний FEM-загружений.</summary>
public static class FemLoadDefinitionFactory
{
    /// <summary>Строит набор сочетаний СП 20 и сохраняет каждое как независимую формулу коэффициентов.</summary>
    public static IReadOnlyList<FemLoadDefinition> CreateSp20(
        FemSchema schema,
        IReadOnlyList<FemLoadCase> loadCases,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemNodeLoad> loads,
        string combinationType)
    {
        var orderedNodeIds = nodes.Select(node => node.Id).OrderBy(id => id).ToArray();
        var combinations = FemSp20CombinationAdapter.BuildSp20(
            loadCases, nodes, loads, orderedNodeIds, combinationType);

        return combinations.Select(combination => new FemLoadDefinition
        {
            SchemaId = schema.Id,
            Tag = combination.Tag,
            SourceKind = "sp20",
            CombinationType = combination.CombinationType,
            ExpressionJson = new FemLoadExpression
            {
                Mode = FemLoadExpressionMode.Sp20,
                CombinationType = combination.CombinationType,
                Terms = combination.Coefficients
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new FemLoadTerm { LoadCaseId = pair.Key, Coefficient = pair.Value })
                    .ToList()
            }.ToJson()
        }).ToArray();
    }
}
