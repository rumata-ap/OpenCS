using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>
/// Контекстные меню конструктивных элементов в дереве схемы: у одного КонЭ — проверки, эпюры, импорт
/// усилий, группы, свойства; у узлов «Конструктивные элементы», «Стержни», «Пластины» — действия над
/// всеми КонЭ узла. Меню собирается при открытии: подменю групп зависят от текущего состава групп.
/// </summary>
internal static class FemMemberTreeMenus
{
    /// <summary>Заполняет меню одного КонЭ.</summary>
    public static void FillMember(ItemCollection items, FemMemberTreeItem item, AppViewModel app)
    {
        var member = item.Member;
        var schema = item.Schema;

        items.Add(Item("FemCheckAddUls", () => app.AddFemCheck(member)));
        if (item.IsShell)
            items.Add(Item("FemCheckAddSls", () => app.AddSlsFemCheck(member)));

        items.Add(new Separator());
        if (!item.IsShell)
            items.Add(Item("FemBarDiagramMenu", () => app.ShowBarDiagrams(member)));
        AddForceImport(items, schema, kind => app.ImportLiraForcesCommand(kind).Execute(member),
            kind => app.ImportScadForcesCommand(kind).Execute(member));

        items.Add(new Separator());
        items.Add(Item("FemMemberTreeGroupFromMember",
            () => CreateGroup(app, schema, [member], member.ElemTag)));
        AddGroupSubmenus(items, app, schema, [member.ElemTag]);

        if (item.IsShell && member.PlanarRegionId is int regionId)
        {
            items.Add(new Separator());
            items.Add(Item("FemMemberTreeProperties", () => EditPlanar(app, schema, member, regionId)));
            items.Add(Item("FemShellDelete", () => DeletePlanar(app, schema, member, regionId)));
        }
    }

    /// <summary>Действия над всеми КонЭ узла «Стержни» (<paramref name="shells"/> = false), «Пластины» (true)
    /// или «Конструктивные элементы» (null — все).</summary>
    public static void FillCategory(ItemCollection items, FemSchema schema, IReadOnlyList<FemMemberTreeItem> members,
        AppViewModel app, bool? shells)
    {
        if (shells == true)
        {
            items.Add(Command("FemCreatePlateMode", app.CreatePlateModeCommand, schema));
            items.Add(Command("FemCreateWallMode", app.CreateWallModeCommand, schema));
            items.Add(Command("FemCreateSpatialPlateMode", app.CreateSpatialPlateModeCommand, schema));
            items.Add(new Separator());
        }

        var all = members.Select(m => m.Member).ToList();
        var groupAll = Item(shells switch
        {
            true  => "FemMemberTreeGroupAllShells",
            false => "FemMemberTreeGroupAllBars",
            null  => "FemMemberTreeGroupAll",
        }, () => CreateGroup(app, schema, all, null));
        groupAll.IsEnabled = all.Count > 0;
        items.Add(groupAll);

        if (shells != true)
        {
            var auto = Item("FemGroupAuto", () => app.AutoGroupFemMembersBySection(schema));
            auto.IsEnabled = all.Any(m => m.ElemType == "beam");
            items.Add(auto);
        }
        items.Add(Command("FemMembersGroupNew", app.NewFemMembersGroupCommand, schema));
    }

    /// <summary>Заполняет меню узла схемы. Узлы правятся только в редакторе схемы (через его сеанс):
    /// отсюда он открывается сразу на свойствах узла.</summary>
    public static void FillNode(ItemCollection items, FemNodeTreeItem item, AppViewModel app)
    {
        var node = item.Node;
        items.Add(Item("FemNodeTreeProperties", () => app.OpenFemNodeInEditor(item.Schema, node.NodeTag)));

        var adjacent = item.AdjacentMembers();
        var members = new MenuItem { Header = Loc.S("FemNodeTreeMembers"), IsEnabled = adjacent.Count > 0 };
        foreach (var member in adjacent)
        {
            var open = new MenuItem { Header = member.Member.ElemTag };
            open.Click += (_, _) => app.CurrentPage = new FemMemberPage(member, app);
            members.Items.Add(open);
        }
        items.Add(members);
        var group = Item("FemNodeTreeGroupFromMembers",
            () => CreateGroup(app, item.Schema, adjacent.Select(i => i.Member).ToList(), null));
        group.IsEnabled = adjacent.Count > 0;
        items.Add(group);

        items.Add(new Separator());
        items.Add(Item("FemNodeTreeCopyCoords", () =>
        {
            try { Clipboard.SetText(FormattableString.Invariant($"{node.X}\t{node.Y}\t{node.Z}")); }
            catch { /* буфер обмена занят другим процессом (CLIPBRD_E_CANT_OPEN) */ }
        }));
    }

    /// <summary>Меню узла «Узлы»: открыть редактор схемы.</summary>
    public static void FillNodes(ItemCollection items, FemSchema schema, AppViewModel app) =>
        items.Add(Item("FemNodeTreeOpenEditor", () => app.CurrentFemSchema = schema));

    /// <summary>Подменю импорта усилий по источнику схемы (ЛИРА, SCAD); у прочих схем — ничего.</summary>
    static void AddForceImport(ItemCollection items, FemSchema schema, Action<string> lira, Action<string> scad)
    {
        if (schema.SourceType == "lira")
        {
            var sub = new MenuItem { Header = Loc.S("ImportSubMenuLiraForces") };
            sub.Items.Add(Item("ImportLiraForcesFromApi", () => lira("lc")));
            sub.Items.Add(Item("ImportLiraRsnFromApi", () => lira("rsn")));
            sub.Items.Add(Item("ImportLiraRsuFromApi", () => lira("rsu")));
            items.Add(sub);
        }
        else if (schema.SourceType == "scad")
        {
            var sub = new MenuItem { Header = Loc.S("ImportSubMenuScadForces") };
            sub.Items.Add(Item("ScadForcesLoadCasesFromApi", () => scad("lc")));
            sub.Items.Add(Item("ScadForcesCombinationsFromApi", () => scad("rsn")));
            sub.Items.Add(Item("ScadForcesRsuFromApi", () => scad("rsu")));
            items.Add(sub);
        }
    }

    /// <summary>«Добавить в группу ▸» / «Убрать из группы ▸» для групп КонЭ схемы.</summary>
    static void AddGroupSubmenus(ItemCollection items, AppViewModel app, FemSchema schema, IReadOnlyCollection<string> tags)
    {
        var groups = schema.MemberGroups.Where(g => g.Kind == FemMemberGroup.KindMembers).ToList();

        var add = new MenuItem { Header = Loc.S("FemMemberTreeAddToGroup") };
        FemGroupMenus.FillAdd(add.Items, groups, FemMemberGroup.KindMembers, tags, group =>
        {
            int added = app.FemGroups.AddTags(schema, group, tags);
            app.LogService.Info(string.Format(Loc.S("FemGroupTagsAdded"), added, group.Tag));
        });
        items.Add(add);

        var remove = new MenuItem { Header = Loc.S("FemMemberTreeRemoveFromGroup") };
        FemGroupMenus.FillRemove(remove.Items, groups, tags, group =>
        {
            int removed = app.FemGroups.RemoveTags(group, tags);
            app.LogService.Info(string.Format(Loc.S("FemGroupTagsRemoved"), removed, group.Tag));
        });
        items.Add(remove);
    }

    /// <summary>Группа КонЭ из элементов: диалог имени и типа, как «Группа КонЭ из выделенных…» в таблицах.</summary>
    /// <param name="defaultTag">Имя по умолчанию; null — по составу.</param>
    static void CreateGroup(AppViewModel app, FemSchema schema, IReadOnlyCollection<FemMember> members, string? defaultTag)
    {
        if (members.Count == 0) return;
        string initialTag = defaultTag ?? FemGroupComposition.DefaultTag(FemMemberGroup.KindMembers, members.Count);
        var dlg = new FemMemberDialog("", initialTag, AppViewModel.FemMembersGroupType(members), showRange: false,
            title: Loc.S("FemMembersGroupDlgTitle")) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;
        // Имя по умолчанию не трогали — пусть сервис посчитает его по фактическому составу.
        string? tag = defaultTag == null && dlg.MemberTag == initialTag ? null : dlg.MemberTag;
        if (app.FemGroups.CreateMembersGroup(schema, members.Select(m => m.ElemTag), tag, dlg.MemberType) is { } group)
            app.LogService.Info(string.Format(Loc.S("FemGroupCreated"), group.Tag, group.Tags.Count));
    }

    /// <summary>Свойства плоского элемента — тот же диалог, что двойной щелчок в таблице «Пластины».</summary>
    static void EditPlanar(AppViewModel app, FemSchema schema, FemMember member, int regionId)
    {
        var region = app.db.GetPlanarRegions(schema.Id).FirstOrDefault(r => r.Id == regionId);
        if (region == null) return;
        new PlanarRegionMemberDialog(app, schema, region.Frame, member, region)
        {
            Owner = Application.Current.MainWindow
        }.ShowDialog();
        app.RefreshFemSchemaTreeCounts(schema);
    }

    /// <summary>Удаление плоского элемента с его областью — как кнопка «Удалить» таблицы «Пластины».</summary>
    static void DeletePlanar(AppViewModel app, FemSchema schema, FemMember member, int regionId)
    {
        if (MessageBox.Show(string.Format(Loc.S("FemMemberTreeDeleteConfirm"), member.ElemTag), Loc.S("Confirmation"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        app.db.DeleteFemMember(member);
        app.db.DeletePlanarRegion(regionId);
        app.RefreshFemSchemaTreeCounts(schema);
    }

    static MenuItem Item(string key, Action action)
    {
        var item = new MenuItem { Header = Loc.S(key) };
        item.Click += (_, _) => action();
        return item;
    }

    static MenuItem Command(string key, System.Windows.Input.ICommand command, object parameter) =>
        new() { Header = Loc.S(key), Command = command, CommandParameter = parameter };
}
