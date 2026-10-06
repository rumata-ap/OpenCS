namespace CScore.Fem;

/// <summary>Разбор выбранных КЭ для группы КЭ: принятые (импортированные) КЭ, КЭ дискретизации с их
/// конструктивными элементами и неизвестные теги.</summary>
/// <param name="Accepted">Номера импортированных КЭ — их можно включить в группу КЭ.</param>
/// <param name="Generated">Номера КЭ, построенных дискретизацией: номера меняются при пересетке,
/// в группу КЭ они не включаются.</param>
/// <param name="GeneratedOwners">Конструктивные элементы, из которых построены <see cref="Generated"/> —
/// из них можно собрать группу КонЭ.</param>
/// <param name="Unknown">Теги, которых нет в сетке схемы.</param>
public sealed record FemMeshTagCheck(
    IReadOnlyList<string> Accepted,
    IReadOnlyList<string> Generated,
    IReadOnlyList<string> GeneratedOwners,
    IReadOnlyList<string> Unknown)
{
    /// <summary>Все выбранные КЭ годятся для группы КЭ.</summary>
    public bool AllAccepted => Generated.Count == 0 && Unknown.Count == 0;
}

/// <summary>Правила состава групп КЭ и групп КонЭ — единые для ручных команд, редактора и импорта.</summary>
public static class FemGroupComposition
{
    /// <summary>Новая группа КонЭ из тегов конструктивных элементов.</summary>
    public static FemMemberGroup NewMembersGroup(
        int schemaId, IEnumerable<string> memberTags, string? tag, string? memberType,
        string origin = FemMemberGroup.OriginManual)
        => NewGroup(schemaId, FemMemberGroup.KindMembers, memberTags, tag, memberType, origin);

    /// <summary>Новая группа КЭ из номеров КЭ сетки (проверку импортированности делает вызывающий —
    /// см. <see cref="CheckMeshTags"/>; импорт пишет свои КЭ без проверки).</summary>
    public static FemMemberGroup NewMeshGroup(
        int schemaId, IEnumerable<string> elementTags, string? tag, string? memberType,
        string origin = FemMemberGroup.OriginManual)
        => NewGroup(schemaId, FemMemberGroup.KindMesh, elementTags, tag, memberType, origin);

    static FemMemberGroup NewGroup(
        int schemaId, string kind, IEnumerable<string> tags, string? tag, string? memberType, string origin)
    {
        var group = new FemMemberGroup
        {
            SchemaId   = schemaId,
            Kind       = kind,
            Origin     = origin,
            MemberType = FemMemberTypes.Normalize(memberType),
        };
        group.SetTags(tags);
        group.Tag = string.IsNullOrWhiteSpace(tag) ? DefaultTag(kind, group.Tags.Count) : tag.Trim();
        return group;
    }

    /// <summary>Имя группы по умолчанию: «Группа КЭ (12)» / «Группа КонЭ (3)».</summary>
    public static string DefaultTag(string kind, int count) =>
        kind == FemMemberGroup.KindMesh ? $"Группа КЭ ({count})" : $"Группа КонЭ ({count})";

    /// <summary>Добавляет теги в состав (без повторов). Возвращает число добавленных.</summary>
    public static int AddTags(FemMemberGroup group, IEnumerable<string> tags)
    {
        int before = group.Tags.Count;
        group.SetTags(group.Tags.Concat(tags));
        return group.Tags.Count - before;
    }

    /// <summary>Убирает теги из состава. Возвращает число убранных.</summary>
    public static int RemoveTags(FemMemberGroup group, IEnumerable<string> tags)
    {
        var drop = tags.Select(t => t.Trim()).ToHashSet(StringComparer.Ordinal);
        int before = group.Tags.Count;
        group.SetTags(group.Tags.Where(t => !drop.Contains(t)));
        return before - group.Tags.Count;
    }

    /// <summary>Убирает удалённые конструктивные элементы из состава групп КонЭ (группы КЭ не трогаются).
    /// Возвращает прежний JSON изменённых групп — для отмены.</summary>
    public static List<(FemMemberGroup group, string oldJson)> RemoveMemberTags(
        IEnumerable<FemMemberGroup> groups, IReadOnlyCollection<string> memberTags)
    {
        var edits = new List<(FemMemberGroup, string)>();
        if (memberTags.Count == 0) return edits;
        foreach (var group in groups.Where(g => !g.IsMeshGroup))
        {
            string old = group.MemberTagsJson;
            if (RemoveTags(group, memberTags) > 0) edits.Add((group, old));
        }
        return edits;
    }

    /// <summary>Разбирает выбранные КЭ для группы КЭ: только импортированные КЭ сохраняют номера между
    /// пересетками; для КЭ дискретизации возвращаются их конструктивные элементы.</summary>
    public static FemMeshTagCheck CheckMeshTags(IEnumerable<string> elementTags, IEnumerable<FemElement> meshElements)
    {
        var byTag = new Dictionary<string, FemElement>(StringComparer.Ordinal);
        foreach (var e in meshElements) byTag.TryAdd(e.ElemTag, e);

        var accepted = new List<string>();
        var generated = new List<string>();
        var owners = new List<string>();
        var unknown = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var seenOwners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in elementTags)
        {
            var tag = raw.Trim();
            if (tag.Length == 0 || !seen.Add(tag)) continue;
            if (!byTag.TryGetValue(tag, out var element)) { unknown.Add(tag); continue; }
            if (element.Origin == FemMember.MeshSourceImported) { accepted.Add(tag); continue; }
            generated.Add(tag);
            if (element.SourceMemberTag is { } owner && seenOwners.Add(owner)) owners.Add(owner);
        }
        return new FemMeshTagCheck(accepted, generated, owners, unknown);
    }
}
