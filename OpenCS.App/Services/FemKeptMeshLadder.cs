using CScore.Fem;

namespace OpenCS.Services;

/// <summary>
/// Лестница перемешивания сохранённой сетки фрагмента (спека «сохранение сетки родителя», разд. 5): сборка с ошибками
/// связности сохранённой сетки (<see cref="FemSchemaMeshBuildResult.ImportedMembersAtFault"/>) → шаг 1 — снять замок с
/// виновных КонЭ (повторяется, пока ошибки называют новые виновные КонЭ с замком); ошибки остались → шаг 2 — со всех
/// КонЭ с замком; ошибки шага 2 — итог (обычные ошибки сборки).
/// Шаг, которому нечего снимать сверх прошлого, пропускается.
/// </summary>
public static class FemKeptMeshLadder
{
    /// <summary>Нужна ли лестница: ошибки сборки и нарушения связности сохранённой сетки.</summary>
    public static bool Needed(FemSchemaMeshBuildResult result) => result.HasErrors && result.ImportedMembersAtFault.Count > 0;

    /// <param name="first">Сборка без снятия замков.</param>
    /// <param name="members">КонЭ схемы (замки — <see cref="FemMember.IsMeshLocked"/>).</param>
    /// <param name="attempt">Сборка со снятием замков с КонЭ (теги, номер шага, причина); null — снимать нечего.</param>
    public static async Task<FemSchemaMeshBuildResult> RunAsync(FemSchemaMeshBuildResult first, IReadOnlyList<FemMember> members,
        Func<IReadOnlySet<string>, int, string, Task<FemSchemaMeshBuildResult?>> attempt)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(attempt);
        if (!Needed(first)) return first;
        var locked = new SortedSet<string>(members.Where(m => m.IsMeshLocked).Select(m => m.ElemTag), StringComparer.Ordinal);
        var faulty = new SortedSet<string>(first.ImportedMembersAtFault.Where(locked.Contains), StringComparer.Ordinal);
        var last = first;
        IReadOnlySet<string> done = new HashSet<string>(StringComparer.Ordinal);
        // Шаг 1 повторяется: перестройка виновных может сделать виновными соседние сохранённые КонЭ (стержень в плоскости
        // перестроенной плиты) — они добавляются, пока ошибки связности называют новые КонЭ с замком.
        string reason = Reason(first);
        while (faulty.Count > 0 && await attempt(faulty, 1, reason) is { } step1)
        {
            if (!step1.HasErrors) return step1;
            last = step1;
            done = step1.Unlock?.UnlockedMemberTags ?? done;
            var more = step1.ImportedMembersAtFault.Where(t => locked.Contains(t) && !faulty.Contains(t)).ToList();
            if (more.Count == 0) break;
            faulty.UnionWith(more);
            reason = Reason(step1);
        }
        if (locked.All(done.Contains)) return last;
        return await attempt(locked, 2, Reason(last)) ?? last;
    }

    /// <summary>Причина шага — первая ошибка связности сохранённой сетки (иначе первая ошибка сборки).</summary>
    static string Reason(FemSchemaMeshBuildResult result)
    {
        var errors = result.Diagnostics.Where(d => d.IsError).ToList();
        var d = errors.FirstOrDefault(x => x.Code.StartsWith("planar_materialize_imported_", StringComparison.Ordinal)) ?? errors.FirstOrDefault();
        return d?.Message ?? "нарушена связность с новой сеткой";
    }
}
