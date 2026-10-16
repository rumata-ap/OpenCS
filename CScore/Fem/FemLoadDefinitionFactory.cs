using CScore.Combinations;
using CScore.Fem.Combinations;

namespace CScore.Fem;

/// <summary>Создаёт материализованные определения сочетаний FEM-загружений.</summary>
public static class FemLoadDefinitionFactory
{
    /// <summary>
    /// Строит набор сочетаний СП 20 по вектору нагрузок (запасной путь) и сохраняет каждое как независимую формулу;
    /// слагаемые — «авто» с ролью из комбинатора, коэффициенты — под вид величин нагрузок.
    /// </summary>
    public static IReadOnlyList<FemLoadDefinition> CreateSp20(
        FemSchema schema,
        IReadOnlyList<FemLoadCase> loadCases,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemNodeLoad> loads,
        string combinationType,
        IReadOnlyList<FemMemberLoad>? memberLoads = null,
        Sp20GammaDefaults? gammaDefaults = null,
        Sp20LoadValueKind valueKind = Sp20LoadValueKind.Normative)
    {
        var orderedNodeIds = nodes.Select(node => node.Id).OrderBy(id => id).ToArray();
        var combinations = FemSp20CombinationAdapter.BuildSp20(
            loadCases, nodes, loads, orderedNodeIds, combinationType, memberLoads, gammaDefaults);
        return FromCombinations(schema, loadCases, combinations, "sp20", FemLoadExpressionMode.Sp20,
            gammaDefaults, valueKind);
    }

    /// <summary>
    /// Определения по готовым сочетаниям: у загружения с известной ролью слагаемое «авто» (коэффициент пересчитан по
    /// роли, типу и виду величин), иначе — заданное число.
    /// </summary>
    public static IReadOnlyList<FemLoadDefinition> FromCombinations(
        FemSchema schema,
        IReadOnlyList<FemLoadCase> loadCases,
        IReadOnlyList<FemLoadCombination> combinations,
        string sourceKind,
        FemLoadExpressionMode mode,
        Sp20GammaDefaults? gammaDefaults = null,
        Sp20LoadValueKind valueKind = Sp20LoadValueKind.Normative)
    {
        var gamma = gammaDefaults ?? Sp20GammaDefaults.Standard;
        return combinations.Select(combination =>
        {
            var expression = new FemLoadExpression
            {
                Mode = mode,
                CombinationType = combination.CombinationType,
                Terms = combination.Coefficients
                    .OrderBy(pair => pair.Key)
                    .Select(pair => combination.Roles.TryGetValue(pair.Key, out var role)
                        ? new FemLoadTerm
                        {
                            LoadCaseId = pair.Key, Coefficient = pair.Value, Role = FemLoadTermRoles.ToKey(role),
                            Mode = FemLoadTerm.AutoMode,
                        }
                        : new FemLoadTerm { LoadCaseId = pair.Key, Coefficient = pair.Value })
                    .ToList()
            };
            expression = FemCombinationCoefficients.Recompute(expression, loadCases, combination.CombinationType,
                valueKind, gamma);
            return new FemLoadDefinition
            {
                SchemaId = schema.Id,
                Tag = combination.Tag,
                SourceKind = sourceKind,
                CombinationType = combination.CombinationType,
                ExpressionJson = expression.ToJson()
            };
        }).ToArray();
    }
}
