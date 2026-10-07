namespace CScore.Fem.Loads;

/// <summary>Цель нагрузки на КЭ → КЭ сетки (оба уровня схемы: КонЭ, группы КонЭ и КЭ, список КЭ).</summary>
public static class FemLoadTargets
{
    /// <summary>
    /// КЭ цели без повторов, в порядке цели. Отсутствующие теги, группы и КонЭ без КЭ — диагностики-ошибки: нагрузка
    /// не должна теряться молча.
    /// </summary>
    public static IReadOnlyList<FemElement> Resolve(FemElementLoad load, FemLoadMeshContext mesh,
        List<FemValidationDiagnostic> diagnostics)
    {
        var result = new List<FemElement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(FemElement e) { if (seen.Add(e.ElemTag)) result.Add(e); }
        var missing = new List<string>();

        void AddElements(IEnumerable<string> tags)
        {
            foreach (var t in tags)
                if (mesh.ElementsByTag.TryGetValue(t, out var e)) Add(e);
                else missing.Add(t);
            if (missing.Count > 0)
                diagnostics.Add(new("element_load_element_missing",
                    $"Нагрузка {Describe(load)}: нет КЭ сетки {Sample(missing)}.", true, missing.ToArray()));
        }

        void AddMembers(IEnumerable<string> tags)
        {
            foreach (var t in tags)
                if (mesh.ElementsByMemberTag.TryGetValue(t, out var list)) foreach (var e in list) Add(e);
                else missing.Add(t);
            if (missing.Count > 0)
                diagnostics.Add(new("element_load_member_without_mesh",
                    $"Нагрузка {Describe(load)}: у КонЭ {Sample(missing)} нет КЭ сетки (не дискретизированы?).", true,
                    missing.ToArray()));
        }

        switch (load.TargetKind)
        {
            case FemLoadTargetKinds.Elements:
                AddElements(load.TargetTags);
                break;
            case FemLoadTargetKinds.Members:
                AddMembers(load.TargetTags);
                break;
            case FemLoadTargetKinds.Group:
                if (load.GroupId is not { } id || !mesh.GroupsById.TryGetValue(id, out var group))
                    diagnostics.Add(new("element_load_group_missing", $"Нагрузка {Describe(load)}: группа {load.GroupId} не найдена."));
                else if (group.IsMeshGroup) AddElements(group.Tags);
                else AddMembers(group.Tags);
                break;
            default:
                diagnostics.Add(new("element_load_target_kind", $"Нагрузка {Describe(load)}: неизвестный вид цели «{load.TargetKind}»."));
                break;
        }
        if (result.Count == 0 && diagnostics.All(d => d.Code is not ("element_load_group_missing" or "element_load_target_kind")))
            diagnostics.Add(new("element_load_target_empty", $"Нагрузка {Describe(load)}: цель пуста."));
        return result;
    }

    internal static string Describe(FemElementLoad load) =>
        load.Id > 0 ? $"№{load.Id} ({load.LoadKind})" : $"({load.LoadKind})";

    internal static string Sample(IReadOnlyList<string> tags) =>
        tags.Count <= 5 ? string.Join(", ", tags) : string.Join(", ", tags.Take(5)) + $" … (всего {tags.Count})";
}
