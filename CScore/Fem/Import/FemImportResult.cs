using System.Text.Json;
using CScore.Planar;

namespace CScore.Fem.Import;

/// <summary>Конструктивный элемент импорта и КЭ сетки, которые он покрывает (их <c>SourceMemberTag</c>
/// при сохранении указывает на него).</summary>
/// <param name="Member">Элемент. У элемента с КЭ сетки <c>MeshSource</c> — imported: геометрия заблокирована,
/// дискретизация его сетку не трогает.</param>
/// <param name="Region">Плоский регион плиты/стены; null — стержень.</param>
/// <param name="ElementTags">Номера КЭ сетки элемента; пусто — источник сетку не передал.</param>
public sealed record FemImportMember(FemMember Member, PlanarRegion? Region, IReadOnlyList<string> ElementTags);

/// <summary>
/// Результат импорта расчётной схемы из внешней программы — единый контракт для всех импортёров.
/// Источник отдаёт, что умеет: ЛИРА/SCAD — сетку и группы КЭ; программы с моделью из конструктивных
/// элементов (Robot, RFEM) — элементы и группы КонЭ, сетку — если её выдают. Id и SchemaId объектов
/// проставляет сохранение (<c>DatabaseService.SaveFemImport</c>).
/// </summary>
public sealed record FemImportResult(
    IReadOnlyList<FemMeshNode> MeshNodes,
    IReadOnlyList<FemElement> MeshElements,
    IReadOnlyList<FemNode> MemberNodes,
    IReadOnlyList<FemImportMember> Members,
    IReadOnlyList<FemMemberGroup> Groups)
{
    /// <summary>Только сетка и группы КЭ (импорт ЛИРЫ/SCAD).</summary>
    public static FemImportResult MeshOnly(
        IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemElement> meshElements, IReadOnlyList<FemMemberGroup> groups)
        => new(meshNodes, meshElements, [], [], groups);

    /// <summary>
    /// Убирает из групп теги, которых нет в результате (КЭ пропущенных при чтении типов, удалённые
    /// элементы источника). Возвращает число убранных тегов.
    /// </summary>
    public int PruneMissingGroupTags()
    {
        var meshTags = MeshElements.Select(e => e.ElemTag).ToHashSet(StringComparer.Ordinal);
        var memberTags = Members.Select(m => m.Member.ElemTag).ToHashSet(StringComparer.Ordinal);
        int removed = 0;
        foreach (var group in Groups)
        {
            var known = group.IsMeshGroup ? meshTags : memberTags;
            removed += FemGroupComposition.RemoveTags(group, group.Tags.Where(t => !known.Contains(t)).ToList());
        }
        return removed;
    }

    /// <summary>
    /// Проверка целостности перед сохранением: уникальность тегов, ссылки элементов на узлы и КЭ, КЭ не
    /// принадлежит двум элементам, состав групп — по их виду. Сохранять можно, если среди диагностик нет
    /// ошибок (<see cref="FemValidationDiagnostic.IsError"/>); КЭ сетки с отсутствующим узлом — предупреждение
    /// (импорты ЛИРЫ/SCAD сохраняли такие КЭ и раньше, 3D-вид их пропускает).
    /// </summary>
    public IReadOnlyList<FemValidationDiagnostic> Validate()
    {
        var errors = new List<FemValidationDiagnostic>();

        var meshNodeTags = Unique(MeshNodes.Select(n => n.NodeTag), "import_mesh_node_duplicate", "Узел сетки", errors);
        var meshTags = Unique(MeshElements.Select(e => e.ElemTag), "import_mesh_element_duplicate", "КЭ сетки", errors);
        var nodeTags = Unique(MemberNodes.Select(n => n.NodeTag), "import_node_duplicate", "Узел", errors);
        var memberTags = Unique(Members.Select(m => m.Member.ElemTag), "import_member_duplicate", "Конструктивный элемент", errors);

        foreach (var e in MeshElements)
            foreach (var tag in NodeTags(e.NodeIdsJson))
                if (!meshNodeTags.Contains(tag))
                    errors.Add(new("import_mesh_node_missing", $"КЭ сетки {e.ElemTag} ссылается на отсутствующий узел {tag}.", IsError: false));

        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (member, region, elementTags) in Members)
        {
            if (region == null)
                foreach (var tag in NodeTags(member.NodeIdsJson))
                    if (!nodeTags.Contains(tag))
                        errors.Add(new("import_member_node_missing",
                            $"Элемент «{member.ElemTag}» ссылается на отсутствующий узел {tag}."));
            foreach (var tag in elementTags)
            {
                if (!meshTags.Contains(tag))
                    errors.Add(new("import_member_element_missing",
                        $"Элемент «{member.ElemTag}» ссылается на отсутствующий КЭ сетки {tag}."));
                else if (!owner.TryAdd(tag, member.ElemTag))
                    errors.Add(new("import_element_two_members",
                        $"КЭ сетки {tag} отнесён к двум элементам: «{owner[tag]}» и «{member.ElemTag}»."));
            }
        }

        foreach (var group in Groups)
        {
            if (group.Kind is not (FemMemberGroup.KindMesh or FemMemberGroup.KindMembers))
            {
                errors.Add(new("import_group_kind", $"У группы «{group.Tag}» неизвестный вид «{group.Kind}»."));
                continue;
            }
            var known = group.IsMeshGroup ? meshTags : memberTags;
            foreach (var tag in group.Tags)
                if (!known.Contains(tag))
                    errors.Add(new("import_group_tag_missing", group.IsMeshGroup
                        ? $"Группа КЭ «{group.Tag}» ссылается на отсутствующий КЭ сетки {tag}."
                        : $"Группа КонЭ «{group.Tag}» ссылается на отсутствующий элемент «{tag}»."));
        }
        return errors;
    }

    static HashSet<string> Unique(IEnumerable<string> tags, string code, string what, List<FemValidationDiagnostic> errors)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
            if (!set.Add(tag))
                errors.Add(new(code, $"{what} «{tag}» повторяется."));
        return set;
    }

    static IEnumerable<string> NodeTags(string json)
    {
        try { return (JsonSerializer.Deserialize<int[]>(json) ?? []).Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        catch (JsonException) { return []; }
    }
}
