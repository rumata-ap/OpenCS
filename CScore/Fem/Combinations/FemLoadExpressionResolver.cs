namespace CScore.Fem.Combinations;

/// <summary>Сворачивает выражение загружения в единый список узловых нагрузок (Single/Sum/All).</summary>
public static class FemLoadExpressionResolver
{
    /// <summary>Возвращает суммарные нагрузки по узлам; LoadCaseId результата = 0. Sp20 не поддержан.</summary>
    public static IReadOnlyList<FemNodeLoad> Resolve(
        FemLoadExpression expr, IReadOnlyList<FemLoadCase> cases, IReadOnlyList<FemNodeLoad> allLoads)
    {
        if (expr.Mode == FemLoadExpressionMode.Sp20)
            throw new NotSupportedException("Сочетания СП 20 в линейном расчёте (срез 4) отложены — используйте одно/несколько загружений.");

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
                if (expr.Terms.Count > 0)
                    foreach (var t in expr.Terms) factor[t.LoadCaseId] = t.Coefficient;
                else
                    foreach (var id in expr.LoadCaseIds) factor[id] = 1.0;
                break;
        }

        var byNode = new Dictionary<int, FemNodeLoad>();
        foreach (var l in allLoads)
        {
            if (!factor.TryGetValue(l.LoadCaseId, out double k)) continue;
            if (!byNode.TryGetValue(l.NodeId, out var acc))
                acc = new FemNodeLoad { LoadCaseId = 0, NodeId = l.NodeId, SchemaId = l.SchemaId };
            acc.Fx += l.Fx * k; acc.Fy += l.Fy * k; acc.Fz += l.Fz * k;
            acc.Mx += l.Mx * k; acc.My += l.My * k; acc.Mz += l.Mz * k;
            byNode[l.NodeId] = acc;
        }
        return byNode.Values.ToList();
    }
}
