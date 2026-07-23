namespace CScore.Fem.Combinations;

/// <summary>Сворачивает выражение загружения в узловые и распределённые нагрузки.</summary>
public static class FemLoadExpressionResolver
{
    /// <summary>Совместимый API для потребителей, которым нужны только узловые нагрузки.</summary>
    public static IReadOnlyList<FemNodeLoad> Resolve(
        FemLoadExpression expr, IReadOnlyList<FemLoadCase> cases, IReadOnlyList<FemNodeLoad> allLoads) =>
        Resolve(expr, cases, allLoads, []).NodeLoads;

    /// <summary>Возвращает результат разрешения выражения для обоих типов FEM-нагрузок.</summary>
    public static FemResolvedLoads Resolve(
        FemLoadExpression expr,
        IReadOnlyList<FemLoadCase> cases,
        IReadOnlyList<FemNodeLoad> allNodeLoads,
        IReadOnlyList<FemMemberLoad> allMemberLoads)
    {
        ArgumentNullException.ThrowIfNull(expr);
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(allNodeLoads);
        ArgumentNullException.ThrowIfNull(allMemberLoads);

        var factors = BuildFactors(expr, cases);
        var byNode = new Dictionary<int, FemNodeLoad>();
        foreach (var load in allNodeLoads)
        {
            if (!factors.TryGetValue(load.LoadCaseId, out double factor)) continue;
            if (!byNode.TryGetValue(load.NodeId, out var acc))
                acc = new FemNodeLoad { LoadCaseId = 0, NodeId = load.NodeId, SchemaId = load.SchemaId };
            acc.Fx += load.Fx * factor;
            acc.Fy += load.Fy * factor;
            acc.Fz += load.Fz * factor;
            acc.Mx += load.Mx * factor;
            acc.My += load.My * factor;
            acc.Mz += load.Mz * factor;
            byNode[load.NodeId] = acc;
        }

        var memberLoads = new List<FemMemberLoad>();
        foreach (var load in allMemberLoads)
        {
            if (!factors.TryGetValue(load.LoadCaseId, out double factor)) continue;
            memberLoads.Add(new FemMemberLoad
            {
                Id = load.Id,
                SchemaId = load.SchemaId,
                LoadCaseId = 0,
                MemberId = load.MemberId,
                CoordinateSystem = load.CoordinateSystem,
                DistributionType = load.DistributionType,
                StartOffsetM = load.StartOffsetM,
                EndOffsetM = load.EndOffsetM,
                QxStart = load.QxStart * factor,
                QyStart = load.QyStart * factor,
                QzStart = load.QzStart * factor,
                QxEnd = load.QxEnd * factor,
                QyEnd = load.QyEnd * factor,
                QzEnd = load.QzEnd * factor,
                Mx = load.Mx * factor,
                My = load.My * factor,
                Mz = load.Mz * factor
            });
        }

        return new FemResolvedLoads(byNode.Values.ToList(), memberLoads);
    }

    static Dictionary<int, double> BuildFactors(FemLoadExpression expr, IReadOnlyList<FemLoadCase> cases)
    {
        var factor = new Dictionary<int, double>();
        switch (expr.Mode)
        {
            case FemLoadExpressionMode.Single:
                foreach (var id in expr.LoadCaseIds) factor[id] = 1.0;
                if (factor.Count == 0 && cases.Count > 0) factor[cases[0].Id] = 1.0;
                break;
            case FemLoadExpressionMode.All:
                foreach (var c in cases) factor[c.Id] = 1.0;
                break;
            case FemLoadExpressionMode.Sum:
            case FemLoadExpressionMode.Sp20:
                if (expr.Terms.Count > 0)
                {
                    foreach (var t in expr.Terms) factor[t.LoadCaseId] = t.Coefficient;
                }
                else if (expr.Mode == FemLoadExpressionMode.Sum)
                {
                    foreach (var id in expr.LoadCaseIds) factor[id] = 1.0;
                }
                else
                {
                    throw new NotSupportedException(
                        "Сочетание СП 20 не содержит материализованных коэффициентов загружений.");
                }
                break;
        }
        return factor;
    }
}
