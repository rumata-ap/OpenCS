namespace CScore.Fem.Fragment;

/// <summary>Копии загружений и определений родителя для фрагмента (Id сохраняются; сервис переназначает их при записи).</summary>
public static class FragmentLoadCopy
{
    /// <summary>Копия загружения со всеми полями (доля длительной части, коэффициент собственного веса, источник).</summary>
    public static FemLoadCase Copy(FemLoadCase lc) => new()
    {
        Id = lc.Id, SchemaId = lc.SchemaId, Tag = lc.Tag, LoadType = lc.LoadType, Sp20Type = lc.Sp20Type,
        Sp20Group = lc.Sp20Group, GammaFUnfav = lc.GammaFUnfav, GammaFFav = lc.GammaFFav, Psi1 = lc.Psi1, Psi2 = lc.Psi2,
        LongTermShare = lc.LongTermShare, SelfWeightFactor = lc.SelfWeightFactor, Origin = lc.Origin,
        SourceLoadNum = lc.SourceLoadNum,
    };

    /// <summary>Копия определения (выражение ссылается на Id загружений родителя).</summary>
    public static FemLoadDefinition Copy(FemLoadDefinition d) => new()
    {
        Id = d.Id, SchemaId = d.SchemaId, Tag = d.Tag, Description = d.Description, ExpressionJson = d.ExpressionJson,
        SourceKind = d.SourceKind, CombinationType = d.CombinationType,
    };

    /// <summary>Копия определения с Id загружений, заменёнными по <paramref name="map"/> (родитель → фрагмент); Id вне карты — как были.</summary>
    public static FemLoadDefinition RemapLoadCases(FemLoadDefinition d, IReadOnlyDictionary<int, int> map)
    {
        var e = d.GetExpression();
        var copy = Copy(d);
        copy.SetExpression(new FemLoadExpression
        {
            Mode = e.Mode,
            CombinationType = e.CombinationType,
            LoadCaseIds = e.LoadCaseIds.Select(Map).ToList(),
            Terms = e.Terms.Select(t => new FemLoadTerm { LoadCaseId = Map(t.LoadCaseId), Coefficient = t.Coefficient, Role = t.Role, Mode = t.Mode }).ToList(),
        });
        return copy;

        int Map(int id) => map.TryGetValue(id, out int n) ? n : id;
    }
}
