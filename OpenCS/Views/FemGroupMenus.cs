using System.Windows.Controls;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.Views;

/// <summary>
/// Пункты меню «Добавить в группу» / «Убрать из группы» — общие для таблиц схемы и 3D-вида.
/// Что делать с выбранной группой, решает вызывающий: таблицы пишут через сервис групп, 3D — через сеанс редактора.
/// </summary>
internal static class FemGroupMenus
{
    /// <summary>Группы вида по имени; недоступна группа, где уже есть все выделенные.</summary>
    public static void FillAdd(ItemCollection items, IEnumerable<FemMemberGroup> groups, string kind,
        IReadOnlyCollection<string> tags, Action<FemMemberGroup> add)
    {
        if (tags.Count == 0) { items.Add(Disabled("FemGroupNoSelection")); return; }
        foreach (var group in Sorted(groups))
        {
            var present = group.Tags.ToHashSet(StringComparer.Ordinal);
            var item = new MenuItem { Header = Header(group), IsEnabled = tags.Any(t => !present.Contains(t)) };
            item.Click += (_, _) => add(group);
            items.Add(item);
        }
        if (items.Count == 0)
            items.Add(Disabled(kind == FemMemberGroup.KindMesh ? "FemMeshGroupsNone" : "FemMemberGroupsNone"));
    }

    /// <summary>Только группы, где есть выделенные.</summary>
    public static void FillRemove(ItemCollection items, IEnumerable<FemMemberGroup> groups,
        IReadOnlyCollection<string> tags, Action<FemMemberGroup> remove)
    {
        foreach (var group in Sorted(groups))
        {
            var present = group.Tags.ToHashSet(StringComparer.Ordinal);
            if (!tags.Any(present.Contains)) continue;
            var item = new MenuItem { Header = Header(group) };
            item.Click += (_, _) => remove(group);
            items.Add(item);
        }
        if (items.Count == 0)
            items.Add(Disabled(tags.Count == 0 ? "FemGroupNoSelection" : "FemGroupSelectionInNoGroup"));
    }

    static IEnumerable<FemMemberGroup> Sorted(IEnumerable<FemMemberGroup> groups) =>
        groups.OrderBy(g => g.Tag, StringComparer.CurrentCulture);

    static string Header(FemMemberGroup group) => $"{group.Tag} ({group.Tags.Count})";

    static MenuItem Disabled(string key) => new() { Header = Loc.S(key), IsEnabled = false };
}
