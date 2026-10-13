using CScore;
using CScore.Fem;
using CScore.Fem.Combinations;
using CScore.Fem.Loads;
using CScore.PlateStrip;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Источник нагрузок схемы для переноса на полосу: загружение или сочетание.</summary>
public sealed record StripLoadSource(
    string Label, IReadOnlyList<(FemLoadCase LoadCase, double Factor)> Terms);

/// <summary>Перенос нагрузок схемы эквивалентного сечения на полосу: выбор источника и сборка сетки схемы.</summary>
public static class StripSchemaLoadService
{
    /// <summary>Загружения и сочетания схемы, из которых можно перенести нагрузки; пусто — их нет.</summary>
    public static IReadOnlyList<StripLoadSource> Sources(DatabaseService db, int schemaId)
    {
        ArgumentNullException.ThrowIfNull(db);
        var cases = db.GetFemLoadCases(schemaId);
        var result = new List<StripLoadSource>();
        foreach (var lc in cases.OrderBy(c => c.Tag, StringComparer.CurrentCulture))
            result.Add(new StripLoadSource(lc.Tag, [(lc, 1.0)]));

        foreach (var definition in db.GetFemLoadDefinitions(schemaId)
                     .OrderBy(d => d.Tag, StringComparer.CurrentCulture))
        {
            var terms = FemLoadExpressionResolver.Terms(definition.GetExpression(), cases);
            // Определение одного загружения с единичным коэффициентом уже в списке загружений.
            if (terms.Count == 0 || (terms.Count == 1 && terms[0].Factor == 1.0)) continue;
            result.Add(new StripLoadSource($"{definition.Tag} ({Loc.S("StripRunSourceCombination")})", terms));
        }
        return result;
    }

    /// <summary>Перенос нагрузок источника на полосу эквивалентного сечения (тяжёлый — вызывать вне UI-потока).</summary>
    public static StripSchemaLoadTransferResult Transfer(
        DatabaseService db, EquivalentSection equivalent, StripLoadSource source, int intervals)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(equivalent);
        ArgumentNullException.ThrowIfNull(source);

        int schemaId = equivalent.SourceSchemaId;
        var memberTags = db.GetFemMembers(schemaId)
            .Where(m => m.PlanarRegionId == equivalent.SourceRegionId && m.ElemTag.Length > 0)
            .Select(m => m.ElemTag)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var mesh = new FemLoadMeshContext(
            db.GetFemMeshNodes(schemaId), db.GetFemMeshElements(schemaId),
            db.FemSchemas.FirstOrDefault(s => s.Id == schemaId)?.MemberGroups.ToList(),
            FemSelfWeightSourceFactory.Create(db, schemaId));

        var ids = source.Terms.Select(t => t.LoadCase.Id).ToHashSet();
        var elementLoads = db.GetFemElementLoads(schemaId).Where(l => ids.Contains(l.LoadCaseId)).ToList();
        var nodeLoads = db.GetFemMeshNodeLoads(schemaId).Where(l => ids.Contains(l.LoadCaseId)).ToList();

        return StripSchemaLoadTransfer.Transfer(
            equivalent.Strip, source.Terms, elementLoads, nodeLoads, mesh, memberTags, intervals);
    }
}
