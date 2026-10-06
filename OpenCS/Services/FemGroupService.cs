using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Создание групп и правка их состава из интерфейса — единственный путь для таблиц, редактора и 3D.
/// Группа КонЭ принимает только существующие конструктивные элементы; группа КЭ — только импортированные
/// КЭ сетки (номера КЭ дискретизации меняются при пересетке). Отброшенное сообщается в журнал.
/// </summary>
public sealed class FemGroupService(DatabaseService db, ILogService log)
{
    /// <summary>Создаёт группу КонЭ. Null — ни один тег не является конструктивным элементом схемы.</summary>
    public FemMemberGroup? CreateMembersGroup(
        FemSchema schema, IEnumerable<string> memberTags, string? tag, string? memberType,
        string origin = FemMemberGroup.OriginManual)
    {
        var accepted = AcceptMemberTags(schema, memberTags);
        if (accepted.Count == 0) return null;
        var group = FemGroupComposition.NewMembersGroup(schema.Id, accepted, tag, memberType, origin);
        Save(schema, group);
        return group;
    }

    /// <summary>Создаёт группу КЭ из номеров КЭ сетки. Null — ни один КЭ не годится (см. журнал).</summary>
    public FemMemberGroup? CreateMeshGroup(
        FemSchema schema, IEnumerable<string> elementTags, string? tag, string? memberType)
    {
        var accepted = AcceptMeshTags(schema, elementTags);
        if (accepted.Count == 0) return null;
        var group = FemGroupComposition.NewMeshGroup(schema.Id, accepted, tag, memberType);
        Save(schema, group);
        return group;
    }

    /// <summary>Пустая группа: группа КЭ у схемы с импортированной сеткой, иначе группа КонЭ.</summary>
    public FemMemberGroup CreateEmptyGroup(FemSchema schema, string tag) =>
        CreateEmptyGroup(schema, tag, HasImportedMesh(schema) ? FemMemberGroup.KindMesh : FemMemberGroup.KindMembers);

    /// <summary>Пустая группа заданного вида.</summary>
    public FemMemberGroup CreateEmptyGroup(FemSchema schema, string tag, string kind)
    {
        var group = kind == FemMemberGroup.KindMesh
            ? FemGroupComposition.NewMeshGroup(schema.Id, [], tag, null)
            : FemGroupComposition.NewMembersGroup(schema.Id, [], tag, null);
        Save(schema, group);
        return group;
    }

    /// <summary>Переименовывает группу. Пустое имя не принимается.</summary>
    public bool Rename(FemMemberGroup group, string tag)
    {
        tag = tag.Trim();
        if (tag.Length == 0 || tag == group.Tag) return false;
        group.Tag = tag;
        db.SaveFemMemberGroup(group);
        return true;
    }

    /// <summary>Добавляет в состав группы КЭ или КонЭ — по её виду. Возвращает число добавленных.</summary>
    public int AddTags(FemSchema schema, FemMemberGroup group, IEnumerable<string> tags)
    {
        var accepted = group.IsMeshGroup ? AcceptMeshTags(schema, tags) : AcceptMemberTags(schema, tags);
        int added = FemGroupComposition.AddTags(group, accepted);
        if (added > 0) db.SaveFemMemberGroup(group);
        return added;
    }

    /// <summary>Убирает теги из состава группы. Возвращает число убранных.</summary>
    public int RemoveTags(FemMemberGroup group, IEnumerable<string> tags)
    {
        int removed = FemGroupComposition.RemoveTags(group, tags);
        if (removed > 0) db.SaveFemMemberGroup(group);
        return removed;
    }

    /// <summary>У схемы есть КЭ сетки, импортированные из внешней программы.</summary>
    public bool HasImportedMesh(FemSchema schema) => db.HasFemImportedMesh(schema.Id);

    void Save(FemSchema schema, FemMemberGroup group)
    {
        db.SaveFemMemberGroup(group);
        schema.MemberGroups.Add(group);
    }

    List<string> AcceptMemberTags(FemSchema schema, IEnumerable<string> memberTags)
    {
        var known = db.GetFemMembers(schema.Id).Select(m => m.ElemTag).ToHashSet(StringComparer.Ordinal);
        var tags = memberTags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var unknown = tags.Where(t => !known.Contains(t)).ToList();
        if (unknown.Count > 0)
            log.Warning(string.Format(Loc.S("FemGroupMembersUnknown"), unknown.Count, Preview(unknown)));
        return tags.Where(known.Contains).ToList();
    }

    List<string> AcceptMeshTags(FemSchema schema, IEnumerable<string> elementTags)
    {
        var check = FemGroupComposition.CheckMeshTags(elementTags, db.GetFemMeshElements(schema.Id));
        if (check.Generated.Count > 0)
            log.Warning(string.Format(Loc.S("FemGroupMeshGenerated"), check.Generated.Count,
                Preview(check.Generated), Preview(check.GeneratedOwners)));
        if (check.Unknown.Count > 0)
            log.Warning(string.Format(Loc.S("FemGroupMeshUnknown"), check.Unknown.Count, Preview(check.Unknown)));
        return [.. check.Accepted];
    }

    static string Preview(IReadOnlyList<string> tags) =>
        string.Join(", ", tags.Take(20)) + (tags.Count > 20 ? ", …" : "");
}
