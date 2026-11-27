using CScore;
using CScore.Fem;
using CScore.Import;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Наборы усилий импорта на цель схемы — общее для меню («Усилия … ЛИРЫ/SCAD») и консоли (<c>import forces --schema
/// --target</c>): номера КЭ цели, привязка наборов к группе или конструктивному элементу, цели и сборка наборов SCAD API.
/// </summary>
public static class FemForceImport
{
    /// <summary>Номера КЭ сетки цели (группа КЭ/КонЭ или конструктивный элемент).</summary>
    public static int[] TargetElements(DatabaseService db, IFemCheckable target) => target switch
    {
        FemMemberGroup group => [.. db.GetFemCheckScope(group).ElementNumbers],
        FemMember element => [.. db.GetFemCheckScope(element).ElementNumbers],
        _ => [],
    };

    /// <summary>Схема цели; 0 — цель не группа и не элемент.</summary>
    public static int TargetSchemaId(IFemCheckable target) => target switch
    {
        FemMemberGroup group => group.SchemaId,
        FemMember element => element.SchemaId,
        _ => 0,
    };

    /// <summary>
    /// Записывает наборы: номер — следующий свободный (если не задан), привязка — к конструктивному элементу
    /// (<see cref="ForceSet.SourceElementId"/>) или группе (<see cref="ForceSet.SourceMemberId"/>); у группы и элемента
    /// разные таблицы, Id могут совпадать — поэтому разные колонки.
    /// </summary>
    public static void Save(DatabaseService db, IReadOnlyList<ForceSet> sets, IFemCheckable? target)
    {
        int next = db.ForceSets.Select(f => f.Num).DefaultIfEmpty(0).Max() + 1;
        foreach (var fs in sets)
        {
            if (fs.Num <= 0) fs.Num = next++;
            if (target != null && fs.SourceSchemaId == null && TargetSchemaId(target) is > 0 and var schemaId)
                fs.SourceSchemaId = schemaId;
            if (target is FemMember element)
            {
                fs.SourceElementId = element.Id;
                fs.SourceElementTag = element.ElemTag;
            }
            else if (target is FemMemberGroup group)
                fs.SourceMemberId = group.Id;
            db.SaveForceSet(fs);
        }
    }

    /// <summary>Параметры чтения усилий SCAD (т → кН, инверсия моментов) из настроек импорта.</summary>
    public static ScadXlsImportOptions ScadOptions(LiraImportSettings s) => new()
    {
        TonToKnFactor = s.TonToKnFactor,
        InvertBarBendingMoments = s.InvertBarBendingMoments,
        InvertShellBendingMoments = s.InvertShellBendingMoments,
    };

    /// <summary>
    /// КЭ для чтения усилий SCAD API: КЭ цели (или все КЭ схемы) с видом по сетке; номер КЭ SCAD = тег КЭ сетки.
    /// <c>NotInMesh</c> — номера цели, которых нет в сетке.
    /// </summary>
    public static (Dictionary<int, ScadElementKind> Targets, int NotInMesh) ScadApiTargets(DatabaseService db, FemSchema schema,
        IFemCheckable? target)
    {
        var kindById = new Dictionary<int, ScadElementKind>();
        foreach (var e in db.GetFemMeshElements(schema.Id))
            if (int.TryParse(e.ElemTag, out int id))
                kindById[id] = e.ElemType == "shell" ? ScadElementKind.Shell : ScadElementKind.Beam;
        int[] ids = target != null ? TargetElements(db, target).Distinct().ToArray() : [.. kindById.Keys];
        var targets = ids.Where(kindById.ContainsKey).ToDictionary(id => id, id => kindById[id]);
        return (targets, ids.Length - targets.Count);
    }

    /// <summary>Наборы из прочитанного SCAD API по виду (загружения, комбинации, РСУ).</summary>
    internal static List<ForceSet> BuildScadApiSets(Scad.ScadApiForceReadResult read, Scad.ScadForceReadKind kind, int schemaId,
        string targetTag, ScadXlsImportOptions options) => kind switch
    {
        Scad.ScadForceReadKind.LoadCases => ScadForceSetBuilder.LoadCases(read.Forces, read.Catalog, schemaId, targetTag, options),
        Scad.ScadForceReadKind.Combinations => ScadForceSetBuilder.Combinations(read.Forces, read.Catalog, schemaId, targetTag, options),
        _ => ScadForceSetBuilder.Rsu(read.Rsu, schemaId, targetTag, options),
    };

    /// <summary>Замечания чтения SCAD API (КЭ вне сетки, нет в проекте, не того вида, без результатов) — в журнал.</summary>
    internal static void ReportScadApiRead(ILogService log, Scad.ScadApiForceReadResult read, int notInMesh)
    {
        if (notInMesh > 0) log.Warning(string.Format(Loc.S("ScadForcesNotInMesh"), notInMesh));
        if (read.MissingElements > 0) log.Warning(string.Format(Loc.S("ScadForcesMissing"), read.MissingElements));
        if (read.WrongKindElements > 0) log.Warning(string.Format(Loc.S("ScadForcesWrongKind"), read.WrongKindElements));
        if (read.NoResultElements > 0) log.Info(string.Format(Loc.S("ScadForcesNoResult"), read.NoResultElements));
    }
}
