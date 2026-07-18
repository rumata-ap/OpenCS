namespace CScore.Fem;

/// <summary>Собирает результирующие узловые воздействия для отображения без зависимостей от UI.</summary>
public static class FemLoadDisplayResolver
{
    const double ZeroTolerance = 1e-12;

    /// <summary>Возвращает ненулевые узловые нагрузки одного исходного загружения.</summary>
    public static IReadOnlyList<FemResolvedNodeLoad> ResolveLoadCase(
        FemLoadCase loadCase, IReadOnlyList<FemNodeLoad> nodeLoads)
        => ResolveTerms([new FemLoadTerm { LoadCaseId = loadCase.Id, Coefficient = 1 }], nodeLoads);

    /// <summary>Возвращает сумму всех слагаемых сохранённого определения нагрузки.</summary>
    public static IReadOnlyList<FemResolvedNodeLoad> ResolveDefinition(
        FemLoadDefinition definition, IReadOnlyList<FemNodeLoad> nodeLoads)
        => ResolveTerms(definition.GetExpression().Terms, nodeLoads);

    static IReadOnlyList<FemResolvedNodeLoad> ResolveTerms(
        IReadOnlyList<FemLoadTerm> terms, IReadOnlyList<FemNodeLoad> nodeLoads)
    {
        var result = new Dictionary<int, double[]>();
        foreach (var term in terms)
        {
            foreach (var load in nodeLoads.Where(load => load.LoadCaseId == term.LoadCaseId))
            {
                if (!result.TryGetValue(load.NodeId, out var values))
                    result.Add(load.NodeId, values = new double[6]);
                values[0] += term.Coefficient * load.Fx;
                values[1] += term.Coefficient * load.Fy;
                values[2] += term.Coefficient * load.Fz;
                values[3] += term.Coefficient * load.Mx;
                values[4] += term.Coefficient * load.My;
                values[5] += term.Coefficient * load.Mz;
            }
        }

        return result
            .Where(pair => pair.Value.Any(value => Math.Abs(value) > ZeroTolerance))
            .OrderBy(pair => pair.Key)
            .Select(pair => new FemResolvedNodeLoad(pair.Key,
                pair.Value[0], pair.Value[1], pair.Value[2], pair.Value[3], pair.Value[4], pair.Value[5]))
            .ToArray();
    }
}

/// <summary>Результирующие шесть компонент узлового воздействия для графического отображения.</summary>
public sealed record FemResolvedNodeLoad(
    int NodeId, double Fx, double Fy, double Fz, double Mx, double My, double Mz);
