using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views.Helpers;

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
        AddViewActions(items, app, schema, [member.ElemTag]);
        items.Add(Item("FemFieldAssign",
            () => app.CreateFemField(schema, new CScore.Fem.Fields.FemFieldTargets { MemberTags = [member.ElemTag] })));

        // Пластины плоских областей перемещаются и копируются вместе с областью — в её редакторе.
        if (member.PlanarRegionId == null)
        {
            items.Add(new Separator());
            items.Add(Item("FemMemberMoveCtx", () => app.TransformFemFragmentInEditor(schema, [], [member.ElemTag], copy: false)));
            items.Add(Item("FemMemberCopyCtx", () => app.TransformFemFragmentInEditor(schema, [], [member.ElemTag], copy: true)));
        }

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
        else
        {
            items.Add(new Separator());
            items.Add(Item("FemMemberDeleteCtx", () => app.InFemSchemaEditor(schema, page => page.DeleteMember(member.ElemTag))));
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
        if (all.Count > 0)
        {
            AddViewActions(items, app, schema, all.Select(m => m.ElemTag).ToList());
            items.Add(new Separator());
        }
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
        items.Add(Item("FemBoundarySelect", () => app.InFemSchemaEditor(item.Schema, page => page.SelectInSchema([node.NodeTag], []))));
        items.Add(Item("FemNodeMoveCtx", () => app.TransformFemFragmentInEditor(item.Schema, [node.NodeTag], [], copy: false)));
        items.Add(Item("FemNodeCopyCtx", () => app.TransformFemFragmentInEditor(item.Schema, [node.NodeTag], [], copy: true)));

        var adjacent = item.AdjacentMembers();
        var members = CommandIcons.Item("FemNodeTreeMembers");
        members.IsEnabled = adjacent.Count > 0;
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

        items.Add(new Separator());
        items.Add(Item("FemNodeDeleteCtx", () => app.InFemSchemaEditor(item.Schema, page => page.DeleteNodes([node.NodeTag]))));
    }

    /// <summary>Меню типа ГУ: изменить параметры для всех объектов типа, правка состава типа (список, назначить выделенным,
    /// снять с выделенных), мозаика переменных пружин, выделить объекты в редакторе схемы.</summary>
    public static void FillBoundaryGroup(ItemCollection items, FemBoundaryGroupItem group, AppViewModel app)
    {
        if (group.IsEditable)
            items.Add(Item("FemBoundaryEdit", () =>
            {
                if (group.Kind == FemBoundaryKind.LineSupports) app.EditFemLineSupportGroup(group);
                else app.EditFemNodalSupportGroup(group);
            }));
        if (group.IsEditable)
        {
            items.Add(Item("FemBoundaryObjects", () => app.EditFemBoundaryGroupObjects(group)));
            items.Add(Item("FemBoundaryAssignSelected", () => app.AssignFemBoundaryGroupToSelection(group, remove: false)));
            items.Add(Item("FemBoundaryRemoveSelected", () => app.AssignFemBoundaryGroupToSelection(group, remove: true)));
        }
        if (group.Kind == FemBoundaryKind.LineSupports)
            items.Add(Item("FemBoundaryLineDelete", () => app.DeleteFemLineSupportGroup(group)));
        else if (group.Kind == FemBoundaryKind.NodalSupports && group.IsEditable)
            items.Add(Item("FemBoundaryNodalDelete", () => app.DeleteFemNodalSupportGroup(group)));
        if (group.IsMosaic)
            items.Add(Item("FemBoundaryShowOnSchema",
                () => app.ShowFemBoundaryMosaic(group.Schema, PlateRebarMosaicSourceKind.Springs)));
        items.Add(Item("FemBoundarySelect", () => app.SelectFemBoundaryInEditor(group.Schema, group.Targets())));
    }

    /// <summary>Меню ветки ГУ: у упругого основания — мозаика C1, у прочих — выделить все объекты.</summary>
    public static void FillBoundaryCategory(ItemCollection items, FemBoundaryCategoryNode category, AppViewModel app)
    {
        if (category.Kind == FemBoundaryKind.Foundation)
            items.Add(Item("FemBoundaryShowOnSchema",
                () => app.ShowFemBoundaryMosaic(category.Schema, PlateRebarMosaicSourceKind.Foundation)));
        else
            items.Add(Item("FemBoundarySelect", () => app.SelectFemBoundaryInEditor(category.Schema, category.Targets())));
    }

    /// <summary>Меню ветки «Поля из таблиц»: новое поле на всю схему.</summary>
    public static void FillFields(ItemCollection items, FemFieldsSubNode node, AppViewModel app) =>
        items.Add(Item("FemFieldAssign", () => app.CreateFemField(node.Schema, new CScore.Fem.Fields.FemFieldTargets())));

    /// <summary>Меню поля: изменить, повторить с новой таблицей, показать, включить/выключить, порядок, удалить.</summary>
    public static void FillField(ItemCollection items, FemFieldItem field, AppViewModel app)
    {
        items.Add(Item("FemFieldEdit", () => app.EditFemField(field)));
        items.Add(Item("FemFieldRepeat", () => app.RepeatFemFieldWithNewTable(field)));
        items.Add(Item("FemFieldShow", () => app.ShowFemField(field)));
        items.Add(new Separator());
        items.Add(Item(field.Enabled ? "FemFieldDisable" : "FemFieldEnable", () => app.ToggleFemField(field)));
        items.Add(Item("FemFieldUp", () => app.MoveFemField(field, -1)));
        items.Add(Item("FemFieldDown", () => app.MoveFemField(field, +1)));
        items.Add(new Separator());
        items.Add(Item("FemFieldDelete", () => app.DeleteFemField(field)));
    }

    /// <summary>Меню узла «Узлы»: открыть редактор схемы.</summary>
    public static void FillNodes(ItemCollection items, FemSchema schema, AppViewModel app) =>
        items.Add(Item("FemNodeTreeOpenEditor", () => app.CurrentFemSchema = schema));

    /// <summary>«Выделить в схеме», «Показать только», «Скрыть» для КонЭ — в редакторе схемы; он открывается, если
    /// открыт другой вид.</summary>
    static void AddViewActions(ItemCollection items, AppViewModel app, FemSchema schema, IReadOnlyCollection<string> memberTags)
    {
        items.Add(Item("FemBoundarySelect", () => app.InFemSchemaEditor(schema, page => page.SelectInSchema([], memberTags))));
        items.Add(Item("Fem3DFragmentShowOnlyCtx",
            () => app.InFemSchemaEditor(schema, page => page.ApplyMembersFragment(memberTags, FemFragmentAction.ShowOnly))));
        items.Add(Item("Fem3DFragmentHideCtx",
            () => app.InFemSchemaEditor(schema, page => page.ApplyMembersFragment(memberTags, FemFragmentAction.Hide))));
    }

    /// <summary>Подменю импорта усилий по источнику схемы (ЛИРА, SCAD); у прочих схем — ничего.</summary>
    static void AddForceImport(ItemCollection items, FemSchema schema, Action<string> lira, Action<string> scad)
    {
        if (schema.SourceType == "lira")
        {
            var sub = CommandIcons.Item("ImportSubMenuLiraForces");
            sub.Items.Add(Item("ImportLiraForcesFromApi", () => lira("lc")));
            sub.Items.Add(Item("ImportLiraRsnFromApi", () => lira("rsn")));
            sub.Items.Add(Item("ImportLiraRsuFromApi", () => lira("rsu")));
            items.Add(sub);
        }
        else if (schema.SourceType == "scad")
        {
            var sub = CommandIcons.Item("ImportSubMenuScadForces");
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

        var add = CommandIcons.Item("FemMemberTreeAddToGroup");
        FemGroupMenus.FillAdd(add.Items, groups, FemMemberGroup.KindMembers, tags, group =>
        {
            int added = app.FemGroups.AddTags(schema, group, tags);
            app.LogService.Info(string.Format(Loc.S("FemGroupTagsAdded"), added, group.Tag));
        });
        items.Add(add);

        var remove = CommandIcons.Item("FemMemberTreeRemoveFromGroup");
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

    internal static MenuItem Item(string key, Action action)
    {
        var item = CommandIcons.Item(key);
        item.Click += (_, _) => action();
        return item;
    }

    static MenuItem Command(string key, System.Windows.Input.ICommand command, object parameter) =>
        CommandIcons.Apply(new MenuItem { Header = Loc.S(key), Command = command, CommandParameter = parameter }, key);
}
