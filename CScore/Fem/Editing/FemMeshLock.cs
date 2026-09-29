using System.Text.Json;

namespace CScore.Fem.Editing;

/// <summary>
/// Блокировка геометрии элементов с импортированной сеткой (<see cref="FemMember.IsMeshLocked"/>):
/// их узлы нельзя двигать и удалять — сетка ЛИРЫ, к которой они привязаны, не перестраивается.
/// </summary>
public static class FemMeshLock
{
    /// <summary>Теги узлов, на которые опираются заблокированные элементы.</summary>
    public static HashSet<string> LockedNodeTags(IEnumerable<FemMember> members)
    {
        var tags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in members.Where(m => m.IsMeshLocked))
            foreach (var id in JsonSerializer.Deserialize<int[]>(member.NodeIdsJson) ?? [])
                tags.Add(id.ToString());
        return tags;
    }

    /// <summary>Заблокированные элементы, опирающиеся на данные узлы (пусто — узлы свободны).</summary>
    public static List<FemMember> LockedMembersOf(IEnumerable<FemMember> members, IEnumerable<string> nodeTags)
    {
        var tags = nodeTags.ToHashSet(StringComparer.Ordinal);
        return members
            .Where(m => m.IsMeshLocked
                && (JsonSerializer.Deserialize<int[]>(m.NodeIdsJson) ?? []).Any(id => tags.Contains(id.ToString())))
            .ToList();
    }
}
