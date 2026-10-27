using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Loads;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Нагрузки КЭ схемы для расчёта: записанные в БД плюс, у фрагмента этажа, нагрузки-поля, разложенные на текущую
/// сетку по опорной сетке родителя (<see cref="FragmentFieldLoadResolver"/>, спека фрагмента разд. 4).
/// </summary>
public static class FragmentLoadService
{
    /// <summary>
    /// Нагрузки КЭ схемы <paramref name="schemaId"/> на сетке <paramref name="meshNodes"/>/<paramref name="mesh"/>.
    /// Без нагрузок-полей — ровно <see cref="DatabaseService.GetFemElementLoads"/>; поля без сетки пропускаются.
    /// </summary>
    public static List<FemElementLoad> ElementLoads(DatabaseService db, int schemaId, IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemElement> mesh, IReadOnlyList<FemMemberGroup> groups, List<FemValidationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(db);
        var loads = db.GetFemElementLoads(schemaId);
        if (mesh.Count == 0 || !db.HasFragmentFieldLoads(schemaId)) return loads;

        var refs = db.GetFragmentRefElements(schemaId);
        var context = new FemLoadMeshContext(meshNodes, mesh, groups);
        loads.AddRange(FragmentFieldLoadResolver.Resolve(db.GetFragmentFieldLoads(schemaId), new ParentMeshMapper(refs), refs,
            context, diagnostics));
        return loads;
    }
}
